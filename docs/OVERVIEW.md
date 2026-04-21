# PenDynamicsLab Overview

PenDynamicsLab is a Windows desktop tool for exploring how drawing tablets and their pens work, and for experimenting with how that input maps to better-looking, more expressive strokes. It targets artists, pen tablet users, and developers who want to understand or fine-tune the pipeline from raw pen pressure to brush output.

The current build re-implements the feature set of [WebPressureExplorer](https://thesevenpens.github.io/WebPressureExplorer/) as a native Avalonia + SkiaSharp app, sitting on top of the [WinPenSession](https://github.com/TheSevenPens/WinPenSession) input library. Future work will extend beyond the web version into more expressive stroke rendering.

## What it does

The app provides two side-by-side panels:

- **Pressure curve editor** (left) — An interactive chart where users select a curve type (passthrough, flat, basic, extended, sigmoid, or bezier), adjust parameters via sliders and draggable control nodes, and see the resulting pressure mapping function in real time. A pressure response sub-chart shows pen hardware grams-force vs logical pressure, optionally with the active curve overlaid.

- **Drawing canvas** (right) — A split drawing surface with two halves. The top half ("Pressure processing: ON") applies the full pressure pipeline (smoothing + curve). The bottom half ("Pressure processing: OFF") uses raw unprocessed pen pressure. Drawing in either half mirrors the stroke to the other for direct visual comparison.

A top ribbon shows the live pen telemetry (proximity, raw/screen/app/canvas position, raw/normalized pressure, azimuth/altitude/twist) and lets the user pick which input API to use (Wintab, Wintab high-res, Avalonia pointer).

## Key features

- **Six curve types** — passthrough (identity), flat (constant), basic (power law), extended (power law with full input/output range controls), sigmoid (S-curve), and bezier (custom cubic bezier with up to 16 points)
- **Bezier presets** — built-in shapes (Linear, Soft, Firm, S-Curve, Light Touch, Heavy, Step) for quick setup
- **Draggable control nodes** — pink/cyan min/max nodes on extended/sigmoid curves; full bezier anchor/handle dragging with broken vs mirrored handle modes; right-click context menu in the plot to add/remove points or change handle mode
- **EMA smoothing** for both pressure and cursor position, with configurable application order (smooth-then-curve or curve-then-smooth)
- **Min approach modes** (clamp vs cut) controlling how the curve behaves below the input minimum
- **Live pressure indicators** on the chart showing raw (purple) and effective (green) pressure positions in real time, plus matching indicators projected onto the response chart
- **Pressure response data** — load pen hardware measurement data (physical grams-force vs logical pressure %) from bundled WACOM samples or uploaded JSON files, with optional curve-effect overlay
- **Image export** — save curve chart, processed canvas, or raw canvas as PNG via file picker
- **Brush controls** — adjustable brush size (1-200 px), stroke color mode (black or random palette), pressure-control target (size or opacity), draw-at-zero-pressure toggle
- **User presets** — save, load, and delete named parameter configurations (curve type + sliders + smoothing + bezier points), persisted to `%LOCALAPPDATA%\PenDynamicsLab\presets.json`
- **Direct value editing** — click any LabeledSlider value to type an exact number; right-click for Min / Max / Reset
- **Driver warning** — dismissible banner reminding users to set their tablet driver's pressure curve to default
- **Multiple input APIs** — Wintab, Wintab high-res digitizer, and Avalonia's pointer pipeline, switchable at runtime

## Tech stack

- **C# 14** on **.NET 10** (`net10.0-windows`)
- **Avalonia 11.2** — UI framework
- **SkiaSharp 3.116** — 2D graphics for the drawing canvases
- **xUnit 2.9** — math parity tests
- **WinPenSession** (sibling repo) — pen input abstraction over Wintab and Avalonia pointer events

The drawing canvases use SkiaSharp with `WriteableBitmap` interop for high-throughput rendering. The chart controls render via Avalonia's `DrawingContext` directly — no Skia interop needed since they're simple line geometry.

## Running the app

```bash
dotnet build PenDynamicsLab.slnx
dotnet run --project PenDynamicsLab.csproj
```

Tests:

```bash
dotnet test PenDynamicsLab.Tests/PenDynamicsLab.Tests.csproj
```

## Project structure

```
PenDynamicsLab.csproj           Main app project (WinExe, net10.0-windows)
PenDynamicsLab.slnx             Solution file
App.axaml / App.axaml.cs        Avalonia application entry
MainWindow.axaml / .cs          Two-panel layout, controls, pressure pipeline
Program.cs                      Avalonia bootstrap

Controls/
  PressureChartControl.cs       Curve chart + draggable nodes / bezier editor / context menus
  PressureResponseChartControl.cs  Pen hardware response data chart
  LabeledSlider.axaml / .cs     Reusable slider with click-to-edit + Min/Max/Reset menu

Curves/
  CurveMath.cs                  Pure math: curve evaluation + bezier solver
  PressureCurveParams.cs        Immutable record holding the full curve configuration
  BezierPoint.cs                Bezier anchor/handle record
  BezierPresets.cs              Built-in bezier preset definitions
  Enums.cs                      CurveType, MinApproach, HandleMode, SmoothingOrder, ColorMode, PressureControl
  EmaConstants.cs               EMA smoothing constants

Drawing/
  DrawSurface.cs                SKBitmap + SKCanvas + WriteableBitmap helper for one canvas

Persistence/
  PresetStore.cs                Load/save user presets to %LOCALAPPDATA%\PenDynamicsLab\presets.json
  PressureResponseData.cs       Response data record + JSON loader (bundled + file picker)
  SampleResponses/              Embedded WACOM KP-504E sample JSONs

PenDynamicsLab.Tests/           xUnit project pinning curve math behavior
docs/                           Documentation (you are here)
```
