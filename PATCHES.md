# Patches carried by this fork

Base: `OutSystems/CefGlue` `main` (120.6099.211) plus PR #249 "Feature support AvaloniaUI 12"
(`selway`, unmerged upstream), which this fork carries as its first two commits with the original
authorship. Everything below is on top of that.

One commit per patch — a CEF bump upstream should be a rebase with cherry-picks, not a merge.

| Patch | Files | Why |
|---|---|---|
| Drop ReactiveUI and DiagnosticsSupport | `AvaloniaBrowserProcessHandler.cs`, `Platform/AvaloniaOffScreenControlHost.cs`, `Platform/ExtendedAvaloniaNativeControlHost.cs`, `CefGlue.Avalonia.csproj`, `Directory.Packages.props` | PR #249 pulled in `AvaloniaUI.DiagnosticsSupport` (commercial, unused here) and `ReactiveUI.Avalonia` (used only for `AvaloniaScheduler`). The package now depends on Avalonia alone. |
| Host the OSR frame at 96 dpi with `Stretch.Fill`, drop the Viewbox | `AvaloniaRenderSurface.cs`, `Platform/AvaloniaOffScreenControlHost.cs` | The Viewbox recomputed its scale from live bounds on every arrange, which is what stretched the stale frame during a resize drag. A real DPI on the bitmap is not the way out: `Image.Render` derives `sourceRect` from `Bitmap.Size` (DIP) while the backend consumes it as pixels, so any DPI other than 96 samples only the top-left `1/scale` of the frame and blows it up to full size. The logical size is carried by `Image.Width/Height` plus `Stretch.Fill` instead. |
| Follow the monitor scale after attach | `Platform/AvaloniaOffScreenControlHost.cs` | `OnAttachedToVisualTree` was the only writer of `DeviceScaleFactor`; nothing listened to `TopLevel.ScalingChanged`, so a window moved across monitors kept the scale it was born on. |
| Notify CEF when the device scale changes | `CefGlue.Common/CommonOffscreenBrowserAdapter.cs` | `WasResized()` re-queries the view rect only. `NotifyScreenInfoChanged()` was commented out over a SurfaceSync crash fixed in chromium 79.0.3909.0 — forty versions below the one this binding targets. |
| Show frames that lag the target size | `CefGlue.Common/Helpers/OffScreenRenderSurface.cs` | `Render` dropped every frame whose size differed from the current target. Measured over a five-second drag: 142 drops against 67 frames shown, so the newly exposed area stayed blank until the drag ended. |
| Anchor the frame to the corner | `AvaloniaRenderSurface.cs`, `Platform/AvaloniaOffScreenControlHost.cs` | `Image.Render` centers the source in its bounds, so a frame trailing the control by 8px lost 4px at each edge and the page jumped upwards on every shrink step. Sizing the image to the frame makes centering a no-op; the control clips the overflow. |
| Let the OSR frame follow the host's `AllowDrop` | `Platform/AvaloniaOffScreenControlHost.cs` | The browser could not be a drop target at all. `AllowDrop` inherits down the *logical* tree, the frame is a visual-only child, and `DragDropDevice.GetTarget` tests `AllowDrop` on the visual it hit alone — no ancestor walk — so it found no target over the browser and raised no drag event anywhere, not even on the hosting window. The hardcoded `SetAllowDrop(control, true)` never helped: that control draws nothing and hit-testing is draw-list based, so it is never the hit. The frame now binds to the control's value, which the host supplies (`WebViewControl-Avalonia` binds the browser to its `WebView` the same way), so a window that refuses drops still refuses them. |
| Drop the mouse-leave manufactured by the press capture | `Platform/AvaloniaOffScreenControlHost.cs` | `OnPointerPressed` re-targets capture to the outer control on left press, Avalonia recomputes pointer-over, and the resulting `PointerExited` was forwarded unfiltered — so Chromium dispatched `mouseleave` down the whole element chain while the button was still held. Anything the page reveals only under `:hover` loses its box between `mousedown` and `mouseup`, and the `click` then retargets to an ancestor: in the terminal the connection-screen favourite star could be cleared but never set, because only the "off" glyph is hover-gated. The button state is no help — measured in the page, the manufactured `mouseout` arrives with `ev.buttons == 0`, so Avalonia raises it without button properties. The capture is the signal: a captured pointer has left nothing, and a genuine exit arrives after `OnPointerReleased` drops the capture. |
| Run the test suite off-screen | `CefGlue.Tests/TestBase.cs` | The terminal hosts every browser off-screen, and nearly every patch here lives in the off-screen host. Upstream initialises CEF with its windowed default, where `BaseCefBrowser` never creates that host and input goes straight to CEF's own window, so a regression in it could not fail a test. The whole suite passes off-screen. |

## Invariants

- **Assembly name stays `Xilium.CefGlue.Avalonia`**, namespace with it. `Xilium.CefGlue.Common.dll`
  carries `InternalsVisibleTo("Xilium.CefGlue.Avalonia")` and nothing here is strong-named, so
  internal access is by name alone; `WebViewControl.Avalonia.dll` also holds an assembly ref to it.
- Package ids stay `CefGlue.Avalonia` / `CefGlue.Common`. Version range `120.7000.x` is ours.
  `WebViewControl-Avalonia` depends on `CefGlue.Avalonia >= 120.6099.207`, so a direct reference to
  our package wins with nothing to exclude.

## Building the packages

```
dotnet build CefGlue.Avalonia/CefGlue.Avalonia.csproj -c Release -p:Platform=x64
```

`-p:Platform=x64` is not optional. `CefGlue.Common.csproj` pulls the browser subprocess from
`CefGlue.BrowserProcess/bin/$(Platform)/$(Configuration)/net8.0/{win,linux,osx}-x64/publish`; without
it the output lands in `bin/Release/...`, the glob misses, and `CefGlue.Common` packs at ~320 KB
instead of ~98 MB. That package looks fine until runtime, where `CefRuntimeLoader` throws
`FileNotFoundException: Unable to find SubProcess`.

Packages land in `../Nuget/output` (outside the repo). Sanity check before publishing: `CefGlue.Common`
≈ 98 MB with `bin/win-x64/Xilium.CefGlue.BrowserProcess.exe` inside.

Both packages must be published together — `CefGlue.Avalonia` declares an exact-version dependency on
`CefGlue.Common` of the same build.
