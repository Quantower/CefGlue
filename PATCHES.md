# Patches carried by this fork

Base: `OutSystems/CefGlue` `main` (120.6099.211) plus PR #249 "Feature support AvaloniaUI 12"
(`selway`, unmerged upstream), which this fork carries as its first two commits with the original
authorship. Everything below is on top of that.

One commit per patch — a CEF bump upstream should be a rebase with cherry-picks, not a merge.

| Patch | Files | Why |
|---|---|---|
| Drop ReactiveUI and DiagnosticsSupport | `AvaloniaBrowserProcessHandler.cs`, `Platform/AvaloniaOffScreenControlHost.cs`, `Platform/ExtendedAvaloniaNativeControlHost.cs`, `CefGlue.Avalonia.csproj`, `Directory.Packages.props` | PR #249 pulled in `AvaloniaUI.DiagnosticsSupport` (commercial, unused here) and `ReactiveUI.Avalonia` (used only for `AvaloniaScheduler`). The package now depends on Avalonia alone. |
| Declare the OSR bitmap at the real DPI, drop the Viewbox | `AvaloniaRenderSurface.cs`, `Platform/AvaloniaOffScreenControlHost.cs` | `CreateBitmap` hardcoded 96 dpi, so a `Viewbox{Fill}` had to squeeze the frame back to the control bounds — and its scale is recomputed from live bounds on every arrange, which is what stretches the stale frame during a resize drag. |
| Follow the monitor scale after attach | `Platform/AvaloniaOffScreenControlHost.cs` | `OnAttachedToVisualTree` was the only writer of `DeviceScaleFactor`; nothing listened to `TopLevel.ScalingChanged`, so a window moved across monitors kept the scale it was born on. |
| Notify CEF when the device scale changes | `CefGlue.Common/CommonOffscreenBrowserAdapter.cs` | `WasResized()` re-queries the view rect only. `NotifyScreenInfoChanged()` was commented out over a SurfaceSync crash fixed in chromium 79.0.3909.0 — forty versions below the one this binding targets. |
| Show frames that lag the target size | `CefGlue.Common/Helpers/OffScreenRenderSurface.cs` | `Render` dropped every frame whose size differed from the current target. Measured over a five-second drag: 142 drops against 67 frames shown, so the newly exposed area stayed blank until the drag ended. |
| Anchor the frame to the corner | `AvaloniaRenderSurface.cs`, `Platform/AvaloniaOffScreenControlHost.cs` | `Image.Render` centers the source in its bounds, so a frame trailing the control by 8px lost 4px at each edge and the page jumped upwards on every shrink step. Sizing the image to the frame makes centering a no-op; the control clips the overflow. |

Not taken: the OSR host registers its own OS drop target (`DragDrop.SetAllowDrop` in the
`AvaloniaOffScreenControlHost` constructor), which swallows drags before the hosting window sees
them. Left alone until the DOM-level alternative is measured — see
`docs/cefglue-avalonia-fork.md` §3 in the terminal repo.

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
