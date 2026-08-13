# CefGlue.Avalonia fork — backlog

Three defects of the OSR browser stack share one owner: `Xilium.CefGlue.Avalonia` / `Xilium.CefGlue.Common` (120.6099.207), consumed through `WebViewControl-Avalonia` 3.120.11. None of them is reachable from our code — every workaround so far has been a hack against the package's private visual tree.

This file is the task list for forking that package. Package sources: <https://github.com/OutSystems/CefGlue>. Check first whether a newer upstream release already fixes any of it. §4 is not a CefGlue defect — it is here because the Avalonia pin that the fork exists to lift is what blocks its fix too.

Measured facts are recorded so nobody re-derives them.

---

## 1. DPI: the device scale factor never follows the monitor

**Symptom.** After a window moves to a monitor with a different scale, CEF keeps rendering at the old scale: a window on a 100% monitor with a 461×390 DIP browser produced a `577×488` bitmap (= 1.25 × bounds, the scale of the monitor the window was born on).

**Three separate causes.**

1. `AvaloniaOffScreenControlHost.OnAttachedToVisualTree` is the **only** writer of `RenderSurface.DeviceScaleFactor`. Nothing subscribes to `TopLevel.ScalingChanged`, so a DPI change after attach never reaches the surface.
2. `CommonOffscreenBrowserAdapter.HandleScreenInfoChanged` calls `BrowserHost.WasResized()`. That re-queries the view rect only. The documented trigger for a scale change is `CefBrowserHost::NotifyScreenInfoChanged()` — it exists in the managed binding (`Xilium.CefGlue`, `cef_browser_host_t.notify_screen_info_changed`) and is **never called anywhere in either package**.
3. `AvaloniaRenderSurface.CreateBitmap` hardcodes the DPI:

   ```csharp
   _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96.0, 96.0), ...);
   ```

   while the base class already computes the right value (`OffScreenRenderSurface.Dpi => DeviceScaleFactor * 96f`). The WPF binding of the same library does it correctly — `new WriteableBitmap(width, height, Dpi, Dpi, ...)` — and hosts a bare `Image { Stretch = None }` with no `Viewbox` at all.

**Fork changes.** Subscribe to `ScalingChanged` next to the attach handler; call `NotifyScreenInfoChanged()` in `HandleScreenInfoChanged` (keep `WasResized()`); pass `Dpi` into `CreateBitmap`.

**Acceptance.** Move a browser panel between the 125% and 100% monitors: the bitmap pixel size must follow `ceil(scale × bounds)` of the *current* monitor, and the page must keep `devicePixelRatio` equal to that scale.

---

## 2. Resize: the stale frame is stretched (rubber band)

**Symptom.** Any OSR panel stretches like rubber during a resize drag and snaps back when the motion stops.

**Cause.** `CreateBitmap` declaring 96 dpi (defect 1.3) forces `Viewbox { Stretch = Fill }` to squeeze the bitmap back to the control bounds, and that scale is recomputed from live bounds on every frame:

```csharp
var scale = Stretch.CalculateScaling(finalSize, desiredSize, StretchDirection);   // Viewbox.ArrangeOverride
```

Meanwhile no frame lands during the drag — `OffScreenRenderSurface.Render` drops every frame whose size differs from the current target (`if (width != ScaledWidth || height != ScaledHeight) return;`) and the target keeps moving. So the scale drifts continuously against a frozen bitmap.

**Current workaround** — `PresentationLayer/Native/CrossPlatform/CefGlueWebBrowser/OsrFrameScaler.cs`: walks into the package's private tree, sets `Viewbox.Stretch = None`, `Image.Stretch = Fill`, and re-applies `Image.Width/Height` from the control bounds whenever `Image.Source` changes. It works, but it depends on the package's tree shape, and it only re-syncs when CefGlue allocates a **new bitmap** — a scale change that leaves the pixel size intact goes unnoticed.

**Fork change.** Fixing 1.3 makes the whole thing unnecessary: with an honest DPI on the bitmap, drop the `Viewbox` and host `Image { Stretch = None }` like the WPF binding does. Then `OsrFrameScaler` can be deleted.

**Acceptance.** Drag-resize a browser panel: no stretching, only the usual clip/reveal lag; delete `OsrFrameScaler` and confirm sizes stay correct at 100% and 125%.

---

## 3. Drag'n'drop: browser panels cannot be drop targets

**Symptom.** DnD works for native/Skia panels (source, target, cross-window) but browser panels are not drop targets. Affects the Order Entry family and the HTML regions of hybrids (Group, Ladder / Market Depth, Order Flow Surface).

**Cause.** `AvaloniaOffScreenControlHost` registers its own OS drop target over the browser region:

```csharp
DragDrop.SetAllowDrop((Interactive)control, true);
control.AddHandler(DragDrop.DragEnterEvent, OnDragEnter, RoutingStrategies.Tunnel | Bubble, false);
// … DragLeave / DragOver / Drop
```

so the drag never reaches our window-level handling in `QuantowerAvaloniaWindow.InitializeDragDrop`. `CefSharp.Wpf` did not do this, which is why the WPF host covered browser panels for free.

**Already rejected** (do not re-derive — full analysis in `docs/superpowers/specs/2026-07-11-avalonia-dragdrop-design.md`):
- hooking WebViewControl's CEF drag API — no public/protected drop hook (`FilesDragging`/`TextDragging` are internal and drag-only, `DragHandler` is private);
- a Win32 `IDropTarget` wrap on the browser HWND — works, Windows-only, rejected to keep the host cross-platform.

**Fork change.** Make the drop-target registration opt-out, or forward the drag events to the hosting control instead of swallowing them.

**Acceptance.** Drop a symbol onto an Order Entry panel and onto the HTML region of a hybrid panel; the same `NativeDragEventArgs` contract as native panels.

### Option that may remove this item from the fork entirely

The earlier "HTML drop-zone" experiment was recorded as *"CEF does not forward external OS drags into the page DOM"*. That conclusion looks wrong — the forwarding is wired:

```csharp
private void HandleDragEnter(CefMouseEvent mouseEvent, CefDragData dragData, CefDragOperationsMask effects)
{
    BrowserHost?.DragTargetDragEnter(dragData, mouseEvent, effects);
    BrowserHost?.DragTargetDragOver(mouseEvent, effects);
}
```

What is missing is the payload. `DragEventArgsExtensions.GetDragData` maps **only** two formats:

```csharp
if (e.Data.Contains(DataFormats.FileNames)) { /* AddFile */ }
if (e.Data.Contains(DataFormats.Text))      { val.SetFragmentText(GetText(e.Data)); }
return val;                                  // anything else → an empty CefDragData
```

and `QuantowerAvaloniaWindow.DoDragDrop` puts in a custom format only:

```csharp
var dataObject = new DataObject();
dataObject.Set(DRAG_DATA_FORMAT, data);      // object[] — meaningless to CEF
```

So CEF was handed an empty drag and had nothing to offer the page — which is exactly the symptom that was observed ("script runs, bridge runs, no DOM drag events").

Sketch of the approach:

1. In `DoDragDrop`, additionally set `DataFormats.Text` to a marker (a GUID of the current drag); keep the real `object[]` in a static slot next to it — the drag is in-process.
2. CEF then receives a non-empty `CefDragData`, so `dragenter` / `dragover` / `drop` should reach the page.
3. In the shared panel template, handle those on `document` — `preventDefault()` on `dragover` is mandatory or `drop` never fires — and call the existing `bound` bridge.
4. Resolve the marker back to the payload on the .NET side and raise the same `NativeDragEventArgs` contract native panels get.

Attractive because it needs **neither the fork nor platform-specific code** — the whole path is inside CEF, so macOS should behave the same as Windows.

Unverified, and worth measuring before committing to it:
- whether Chromium surfaces `SetFragmentText` to the DOM as `text/plain` in `dataTransfer`;
- the marker becomes visible if the drag leaves the application;
- hover feedback comes from CEF, not from Avalonia.

---

## 4. Square window corners — the Avalonia 12 upgrade fixes this, the fork does not

Not a CefGlue defect. It is listed here because it is held hostage by the same pin: the fix exists only in Avalonia 12, and the 11.3.4 pin exists only for WebViewControl.

**Symptom.** Win11 rounds the frame of panel windows again after an unbind (ungrouping the last child) or a minimize → restore. Square only right after the window first opens.

**Cause.** `Avalonia.Win32.WindowImpl.ExtendClientArea()` (11.3.4) writes the corner preference itself, unconditionally:

```csharp
if (_isClientAreaExtended && WindowState != FullScreen) {
    DwmExtendFrameIntoClientArea(_hwnd, ref margins);
    int pref = 2;                                   // DWMWCP_ROUND
    DwmSetWindowAttribute(_hwnd, 33, &pref, 4);     // DWMWA_WINDOW_CORNER_PREFERENCE
}
```

and it runs on every hide/show and every window-state change: `WM_SIZE` when the state changes, `WM_SHOWWINDOW` (24) and `WM_WINDOWPOSCHANGED` (71) carrying `SWP_SHOWWINDOW`/`SWP_HIDEWINDOW` (both via `OnShowHideMessage`), `ShowWindow(state)`, `SetFullScreen`, and the three `SetExtendClientArea*Hint` setters.

Our counter-hack — `WindowsCornerStyle.MakeSquare` (`DWMWCP_DONOTROUND`), called from `QuantowerAvaloniaWindow.OnOpened`, the `Position` setter and `WindowState == Normal` — wins the race only in `OnOpened`, which runs after `impl.Show()`. The other two run *before* `ExtendClientArea`: in `WM_SIZE` our `WindowStateChanged` handler is one line above the overwrite. The shadow the frame carries is part of the same DWM frame; nothing in our code targets it separately.

**Why not patch around it in 11.x.** There is no post-`ExtendClientArea` hook: `ExtendClientAreaToDecorationsChanged` reaches `Window` through `SetAndRaise`, silent when the value is unchanged; `WindowImpl` is `internal`, so no override; `Win32Properties.AddWndProcHookCallback` fires *before* Avalonia's `WndProc`. What is left is subclassing `GWLP_WNDPROC` for a synchronous re-apply, or a deferred re-apply posted from the hook — both were rejected in favour of the upgrade.

**What the upgrade gives.** In 12.1.0 the preference moved out of `ExtendClientArea` into its own `_cornerPreference`, written only by `SetWindowCornerPreference(...)` — `DwmSetWindowAttribute(_hwnd, 33, …)` appears exactly once in that `WindowImpl`, inside `UpdateWindowCornerPreference()`. It is public as an attached property:

```csharp
Win32Properties.SetWindowCornerPreference(window, Win32Properties.WindowCornerPreference.DoNotRound);
```

**Acceptance.** Set the attached property in the `QuantowerAvaloniaWindow` constructor; delete `WindowsCornerStyle` and its three call sites; unbind a panel from a group and minimize → restore it — the frame stays square in both cases.

---

## Notes for whoever picks this up

- Avalonia is pinned at **11.3.4** only for WebViewControl compatibility (commit `015fe1b661`). A minimal Avalonia 12 window no longer loses its logical size across a DPI change, and 12 is the only place the corner-preference fix exists (§4) — so an upgrade may be a better first move than the fork.
- Keep the fork's package id and version scheme distinguishable, publish to the private feed (`nuget.deployment.me`), and pin it in `CefGlueWebBrowser.csproj` next to `WebViewControl-Avalonia`.
- `WebViewControl-Avalonia` sits on top of `CefGlue.Avalonia`; defects 1 and 2 live in the latter, defect 3 in the OSR host of the same package, so a single fork covers all three.
