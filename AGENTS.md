# Acrux — Agent Guide

## Build & run

```powershell
dotnet build                          # build all projects
dotnet run --project Acrux        # launch the browser app
```

## Project structure

| Project | Description |
|---------|-------------|
| `Acrux/` | Desktop app entry — `Program.cs` → `BrowserApp.RunAsync()` |
| `Acrux.BrowserUi/` | Chrome UI: tab strip, omnibox, DevTools, settings pages (shell-side only) |
| `Acrux.Core/` | Core engine — CSS, DOM, Layout, JS, Network, Performance, Fonts |
| `Acrux.Rendering/` | SkiaSharp display list, paint, raster |
| `Acrux.PageContract/` | Shell ↔ engine IPC contract (messages, frame channel) — no engine types |
| `Acrux.PageHost/` | `PageEngine`: the pipeline that runs inside a tab's child process |
| `Acrux.JsEngineHost/` | Out-of-process JS engine host (only compiled with `UseMultipleJsEngine=true`) |
| `Acrux.Platform/` | Platform abstraction — Win32 / X11 / Cocoa |
| `Acrux.Native/` | Native P/Invoke interop (IME, etc.) |
| `Acrux.Input/` | Input method (IME) |
| `Acrux.Core.Tests/` | xUnit tests + hand-rolled micro-benchmarks |
| `Acrux.SmokeTest/`, `Acrux.PerfSmokeTest/` | Headless engine / performance smoke tests |

Solution format: `.slnx` (new XML-based format, VS 2022+ / `dotnet` CLI).

Project dependency order: `Core` → `Platform`+`Input`+`Native` → `Rendering` → `Acrux` (app).

## Naming

The product is **Acrux**. Inside it, the engines carry their own brand names, and those
names live in prose, type vocabulary and headings — **not** in the namespace tree, which
mirrors the folder tree one-to-one:

| Brand | What it is | Where the name appears |
|-------|------------|------------------------|
| Acrux | the browser (shell + product) | everything |
| Cruxism | the core engine | `Acrux.Core` assembly, docs |
| Aurora | the layout engine | `AuroraBox`, `LayoutAurora`, `Acrux.Core.Layout` |
| Prism | the rendering engine | `Acrux.Rendering` assembly, headings |

A namespace-qualified reference (`Dom.Element`) resolves through the *enclosing* namespace
chain, so promoting a subsystem to its own root (bare `Aurora`) silently breaks hundreds of
those references. Keep subsystems under `Acrux.*`.

## Framework & toolchain

- **.NET 10.0**, nullable enabled, implicit usings everywhere.
- `AllowUnsafeBlocks` in: `Acrux`, `Rendering`, `Platform`, `Native`.
- **AOT**, Acrux is based on AOT and JsEngineHost is normal(js engine can't aot) , so avoid reflection and make sure the project is cross-platfrom.
- **SkiaSharp 4.150.1** for all rendering (CPU + OpenGL GPU).
- **AngleSharp** for HTML parsing, **JavaScriptEngineSwitcher.*`** for JS engines.
- Some documents about html standard in ./docs.
- Embedded resources in `Acrux.Core/Resources/Html/` and `Resources/Css/`.
- No `Directory.Build.props` — each project self-configures.

## Testing

- **Read `docs/CSS-HANDOFF.md` first** for CSS standard verification: verified feature list with
  measured rules, known gaps with Edge data, the five headless channels, the snapshot scripts,
  and the traps that produced false diagnoses in past sessions.
- **xUnit** (`Microsoft.NET.Test.Sdk` 18.7.0).
- Tests are only in `Acrux.Core.Tests/`.
- `Acrux.Core.Tests/Performance/` contains ~14 test files for performance subsystems.
- `Acrux.Core.Tests/Benchmarks/MicroBenchmarks.cs` — hand-rolled throughput tests using `ITestOutputHelper`, runnable via `dotnet test`.
- No integration tests (no browser-level UI tests).
- However,there are some problems in test,so never run test!!!

## Project conventions

- Namespace matches folder structure (e.g. `Acrux.Core.Performance.Scheduling`).
- Single solution file at root: `Acrux.slnx`.
- No CI workflows, no pre-commit hooks, no lint/styling config.
- `docs/` directories contain reference notes about DOM/CSS/browser API surface.
- Test pages: `test_css_features.html`, `test_js.html`, `test_wrapping.html`.
- Never never lose the exist function, unless user want to delete or change it.
- Use Chinese in chat, but use English in code.