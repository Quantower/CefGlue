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
| Send the Enter char event that Avalonia never raises | `Platform/AvaloniaOffScreenControlHost.cs`, `CefGlue.Tests/Input/KeyboardTests.cs` | The host sends CEF a char event only from `TextInput`, and Avalonia raises no `TextInput` for control characters — `Avalonia.Win32` 12.1.0 drops every `WM_CHAR` below 32. Enter reached Chromium as keydown and keyup with no keypress, and Blink inserts the line break on the keypress: in the terminal no `<textarea>` in a browser panel took a newline, and `onkeypress` handlers waiting for Enter never fired. Measured in the page: `keydown`, `keyup` for Enter against `keypress`, `beforeinput`, `input` for a letter. Only plain and Shift+Enter get the char — the combinations Windows turns into `\r`; Ctrl+Enter becomes `\n` there and Alt+Enter a system char. |
| Open the tooltip after `ToolTip.ShowDelay` and drop the stale `Tip` | `Platform/AvaloniaOffScreenControlHost.cs` | `SetTooltip` forced `ShowDelay` to 0 and opened the tooltip the moment CEF reported the text. Off-screen, Chromium reports it 1–2 ms after the mouse move that lands on a `title` and leaves any delay to the host, so the browser had none and the host could not set one; CefGlue.WPF and CefSharp wait half a second. On empty text the host only closed the tooltip and left `Tip` on the control, so Avalonia's `ToolTipService` reopened the old text the next time the pointer entered the browser, over an element with no `title`, and it hung there until a click (reproduced headless against Avalonia 12.1.0). The host now reads `ToolTip.ShowDelay` from the control — `WebViewControl-Avalonia` binds it to its own — opens after it only if the pointer is still over the browser, and clears `Tip` together with the text. The next patch replaces the host's timer with `ToolTipService`'s own and stops swapping a new text into an open tooltip. |
| Restart the tooltip delay for every new text | `Platform/AvaloniaOffScreenControlHost.cs` | Crossing from one `title` to an adjacent one, Chromium sends the new text with no empty one in between, and the host swapped it into the open tooltip, so the next tooltip appeared at once. The terminal makes every tooltip wait the full delay, its own controls included (`ToolTip.BetweenShowDelay = -1`). `ToolTipService` starts `ShowDelay` only when the tip control under the pointer changes, and Chromium's text arrives after the last pointer event, so nothing tells the service about it. The host now clears `Tip` and makes the service re-check the pointer, which closes the old tooltip and drops the control, then sets the new text and makes it re-check again, which starts a fresh delay. The service owns the opening, so the host's timer is gone. The host re-checks only while the pointer is over the browser: toggling the service stops the pending delay of whatever control it tracks, and the empty text Chromium sends on leave arrives after the service has moved on to the control the pointer went to (measured: that control's tooltip never opened). The re-check is the one `TopLevel` runs when `ToolTip.ServiceEnabled` turns on, and the host toggles it on the `TopLevel` itself: for any other visual, Avalonia 12.1 adds the visual's offset in its parent to the rectangle a second time and skips the check when the pointer falls outside it (measured headless: a control at x = 200 in its parent never got its new tooltip). |

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
