# PenDynamicsLab Overview

PenDynamicsLab is a Windows desktop tool for exploring how drawing tablets and their pens work, and for experimenting with how that input maps to better-looking, more expressive strokes. It targets artists, pen tablet users, and developers who want to understand or fine-tune the pipeline from raw pen pressure to brush output.

The current build re-implements the feature set of [WebPressureExplorer](https://thesevenpens.github.io/WebPressureExplorer/) as a native Avalonia + SkiaSharp app, sitting on top of the [WinPenKit](https://github.com/TheSevenPens/WinPenKit) input library. Future work will extend beyond the web version into more expressive stroke rendering.

## What it does

The window is split into a fixed left panel and a tabbed right panel:

- **Pressure curve editor** (left, 472 px) — Split into two columns. On the left, an interactive chart showing the pressure mapping function in real time, whose right-click menu carries the export actions. On the right, a column of collapsible cards: **Curve** (type, amount, range and bezier controls), **Smoothing** (algorithm and amount), **Processing order**, and **Presets**. The Curve and Smoothing headers show `(OFF)` whenever that stage currently does nothing, so you can tell at a glance whether the pipeline is actually altering anything.

- **Right-hand tabs** — Three workflows, each in its own tab:
  - **Stroke** — A single drawing canvas with the full pressure pipeline applied (smoothing + curve).
  - **Stroke compare** — A split surface: the top half applies the full pipeline ("Pressure processing: ON"), the bottom half uses raw unprocessed pen pressure ("Pressure processing: OFF"). Drawing in either half mirrors the stroke to the other for direct visual comparison. The processed canvas is the *same* surface shown in the Stroke tab, so content carries across tabs.
  - **Pressure response** — Pen hardware measurement data (physical grams-force vs logical pressure %) charted on its own, with an optional overlay showing what the active curve does to it.

A shared brush ribbon (size, color, pressure target, draw-at-zero, Clear) sits at the top of whichever stroke tab is active. A top ribbon shows the live pen telemetry (proximity, raw/screen/app/canvas position, raw/normalized pressure, azimuth/altitude/twist) and lets the user pick which input API to use (Wintab, Wintab high-res, Avalonia pointer).

## Key features

- **Six curve types** — passthrough (identity, the default), flat (constant), basic (power law), extended (power law with full input/output range controls), sigmoid (S-curve), and bezier (custom cubic bezier with up to 16 points)
- **Bezier presets** — built-in shapes (Linear, Soft, Firm, S-Curve, Light Touch, Heavy, Step) for quick setup
- **Draggable control nodes** — pink/cyan min/max nodes on extended/sigmoid curves; full bezier anchor/handle dragging with broken vs mirrored handle modes; right-click context menu in the plot to add/remove points or change handle mode
- **Pressure smoothing** — Passthrough (none, the default) or EMA with an adjustable amount, plus configurable application order (smooth-then-curve or curve-then-smooth)
- **Min approach modes** (clamp vs cut) controlling how the curve behaves below the input minimum
- **Live pressure indicators** on the chart showing raw (purple) and effective (green) pressure positions in real time, plus matching indicators projected onto the response chart. These stay live on every tab, including Pressure response, which has no canvas of its own.
- **Pressure response data** — load pen hardware measurement data from bundled WACOM samples or uploaded JSON files, with optional curve-effect overlay. The first bundled sample auto-loads at startup so the tab shows something immediately.
- **Image export** — right-click the curve chart to copy or save it (full chart or plot area alone); every stroke canvas has an Export menu (copy to clipboard or save as PNG)
- **Brush controls** — adjustable brush size (1-200 px), stroke colour mode dropdown (black or random palette), pressure-target dropdown (size or opacity), draw-at-zero-pressure toggle, Clear. One `BrushRibbon` instance is reparented between the stroke tabs, so settings stay in sync.
- **Clear via keyboard** — Delete or Backspace clears both canvases, unless a text box has focus
- **User presets** — save (via "Save settings" and an inline name box), load, and delete named parameter configurations (curve type + sliders + smoothing + bezier points), persisted to `%LOCALAPPDATA%\PenDynamicsLab\presets.json`
- **Direct value editing** — click any LabeledSlider value to type an exact number; right-click for Min / Max / Reset
- **Driver warning** — dismissible banner reminding users to set their tablet driver's pressure curve to default
- **Multiple input APIs** — Wintab, Wintab high-res digitizer, and Avalonia's pointer pipeline, switchable at runtime

## Tech stack

- **C# 14** on **.NET 10** (`net10.0-windows`)
- **Avalonia 11.2** — UI framework
- **SkiaSharp 3.116** — 2D graphics for the drawing canvases
- **xUnit 2.9** — math parity tests
- **WinPenKit** (sibling repo) — pen input abstraction over Wintab and Avalonia pointer events

The drawing canvases use SkiaSharp with `WriteableBitmap` interop for high-throughput rendering. The chart controls render via Avalonia's `DrawingContext` directly — no Skia interop needed since they're simple line geometry.

## Running the app

PenDynamicsLab references WinPenKit by **project path**, not by NuGet package, so the repo must be cloned as a sibling directory:

```
GitHub/
├── PenDynamicsLab/
└── WinPenKit/
```

```bash
dotnet build PenDynamicsLab.slnx
dotnet run --project PenDynamicsLab.csproj
```

Tests (30, all pinning `CurveMath`):

```bash
dotnet test PenDynamicsLab.Tests/PenDynamicsLab.Tests.csproj
```

## Releases

Pushing a `v*` tag triggers `.github/workflows/release.yml`, which checks out both PenDynamicsLab and WinPenKit as siblings, publishes a self-contained single-file `win-x64` build, zips it, and attaches it to a new GitHub Release with generated notes.

## Project structure

```
PenDynamicsLab.csproj           Main app project (WinExe, net10.0-windows)
PenDynamicsLab.slnx             Solution file
App.axaml / App.axaml.cs        Avalonia application entry
MainWindow.axaml / .cs          Window layout, controls, pressure pipeline
Program.cs                      Avalonia bootstrap

Controls/
  PressureChartControl.cs       Curve chart + draggable nodes / bezier editor / context menus
  SectionCard.axaml / .cs       Collapsible titled card used by the left-hand control column
  PressureResponseChartControl.cs  Pen hardware response data chart
  LabeledSlider.axaml / .cs     Reusable slider with click-to-edit + Min/Max/Reset menu
  BrushRibbon.axaml / .cs       Brush size / color / pressure target / draw-zero / Clear toolbar
  StrokeCanvasView.axaml / .cs  One stroke surface: header + Save button + Image host

Curves/
  CurveMath.cs                  Pure math: curve evaluation + bezier solver
  PressureCurveParams.cs        Immutable record holding the full curve configuration
  BezierPoint.cs                Bezier anchor/handle record
  BezierPresets.cs              Built-in bezier preset definitions
  Enums.cs                      CurveType, MinApproach, HandleMode, SmoothingType, SmoothingOrder, ColorMode, PressureControl
  EmaConstants.cs               EMA smoothing constants

Drawing/
  DrawSurface.cs                SKBitmap + SKCanvas + WriteableBitmap, shareable across Image hosts

Persistence/
  PresetStore.cs                Load/save user presets to %LOCALAPPDATA%\PenDynamicsLab\presets.json
  PressureResponseData.cs       Response data record + JSON loader (bundled + file picker)
  SampleResponses/              Embedded WACOM KP-504E sample JSONs

PenDynamicsLab.Tests/           xUnit project pinning curve math behavior
.github/workflows/release.yml   Tag-triggered win-x64 release build
docs/                           Documentation (you are here)
```
