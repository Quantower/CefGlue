# CLAUDE.md — CefGlue (Quantower fork)

Fork of [OutSystems/CefGlue](https://github.com/OutSystems/CefGlue), MIT. It exists for one reason:
the Quantower terminal is pinned to Avalonia 11.3.4 solely because `WebViewControl-Avalonia` →
`CefGlue.Avalonia` is built against Avalonia 11, and Avalonia 12 removed the drag-drop API that
`CefGlue.Avalonia` uses (`IDataObject`, `DataObject`, `DataFormats`, `DragDrop.DoDragDrop`,
`DragEventArgs.Data`). Lifting that pin is the point; the off-screen rendering fixes ride along.

Read `PATCHES.md` first — it lists every deviation from upstream and why.

## What lives where

Only `CefGlue.Avalonia` and `CefGlue.Common` matter to the terminal:

- `CefGlue.Avalonia` → `Xilium.CefGlue.Avalonia.dll`, the Avalonia host and OSR surface. Small,
  ~20 types, this is where nearly every patch is.
- `CefGlue.Common` → `Xilium.CefGlue.Common.dll` + `Xilium.CefGlue.dll` + the browser subprocess for
  win/linux/osx. No Avalonia dependency at all. ~98 MB packed.
- `CefGlue.WPF`, `CefGlue.Demo.*`, `CefGlue.Interop.Gen` — untouched; the demo is still the quickest
  way to run a browser without the terminal.

`BaseCefBrowser.cs` physically lives in `CefGlue.Common` but is `<Compile Include>`-linked into the
platform assemblies, so it compiles into `Xilium.CefGlue.Avalonia.dll` too. Editing it touches both.

## Rules

- Never rename the assemblies or namespaces — see the invariants in `PATCHES.md`.
- Keep patches as separate commits, one per defect, so a CEF bump is a rebase with cherry-picks.
- `main` mirrors upstream untouched. Work happens on `quantower/*` branches.
- Upstream is reachable as the `upstream` remote; `origin` is the fork.
- Always build with `-p:Platform=x64` (see `PATCHES.md` for what breaks otherwise).

## Upstream state (checked 2026-08-13)

- `main` has not moved since 2025-10-22. Releases 120.6099.207…211 are identical in every file this
  fork touches — bumping the package version alone fixes nothing.
- PR #249 "Feature support AvaloniaUI 12" is the Avalonia 12 port this fork is based on. Still open.
- PR #208 bumps CEF to 134.3.9 (draft, unstable). Taking it later means rebasing this branch onto it
  and republishing both packages.
