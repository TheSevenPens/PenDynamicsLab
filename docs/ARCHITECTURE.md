# Architecture

## Window layout

```
MainWindow
├── DriverWarningBanner (dismissible)
├── Top ribbon (API selector + pen telemetry)
└── Body Grid
    ├── Left panel (360 px)
    │   ├── PressureChartControl + "Save chart..." button
    │   └── ScrollViewer
    │       ├── Curve type combo
    │       ├── Bezier toolbar (Add / Remove / count / preset combo, visible only for Bezier)
    │       ├── LabeledSlider × N  (softness, in/out range, flat level)
    │       ├── Min approach radios
    │       ├── SMOOTHING section (pressure EMA, position EMA, smoothing order)
    │       └── USER PRESETS section (name input + Save + dynamic list)
    ├── 1px splitter
    └── CanvasArea (DockPanel)
        └── RightTabs (TabControl)
            ├── "Stroke"
            │   ├── StrokeBrushSlot (ContentControl — hosts the shared BrushRibbon)
            │   └── StrokeView : StrokeCanvasView
            ├── "Stroke compare"
            │   ├── CompareBrushSlot (ContentControl — hosts the shared BrushRibbon)
            │   └── Grid
            │       ├── CompareProcessedView : StrokeCanvasView  ("Pressure processing: ON")
            │       ├── 1px divider
            │       └── CompareRawView : StrokeCanvasView        ("Pressure processing: OFF")
            └── "Pressure response"
                ├── Data combo + Clear button
                ├── "Show effect of curve" checkbox + info label
                └── PressureResponseChartControl
```

`MainWindow.axaml.cs` owns essentially all state and behavior; controls communicate via events and StyledProperties.

`CanvasArea` (the whole right-hand DockPanel) is also the Avalonia element passed to `AvaloniaPointerSession`, so the Avalonia-pointer input path receives events across every tab.

## Component roles

### `MainWindow`
Single source of truth. Owns:
- `_curveParams` — immutable `PressureCurveParams` record
- Two `DrawSurface` instances (`_processed`, `_raw`) for the stroke bitmaps
- The single shared `BrushRibbon` instance, reparented between tab slots
- Stroke-local smoothing state (`_smoothedPressure`, `_smoothedPos`, `_lastDrawPos`, `_activeCanvas`)
- The `PresetStore` and the live `IPenSession`

The render timer (16 ms tick) drains pen points from the session, runs them through the pressure pipeline, draws line segments to the surfaces, and updates the live indicators on both charts.

Brush state is *not* stored on `MainWindow` — it's read on demand from `BrushRibbon`'s properties (`BrushSize`, `ColorMode`, `PressureControl`, `DrawZeroPressure`) at draw time. Only `_strokeColor` (the currently-picked random palette entry) lives on the window.

### `StrokeCanvasView`
A `UserControl` bundling a header label, a "Save..." button, and an `Image`. It does **not** own pixel data — it exposes `Image` (register with a `DrawSurface`), `Host` (the `Border` whose bounds drive surface size), a `Header` styled property, and a `SaveRequested` event. The `Image` sits inside a `Canvas` pinned at (0, 0) so an oversized shared bitmap doesn't get re-laid-out when it's larger than the current host.

Three instances exist: `StrokeView`, `CompareProcessedView`, `CompareRawView`.

### `BrushRibbon`
A `UserControl` toolbar: brush size slider, color mode radios, pressure-target radios, draw-at-zero checkbox, and Clear. Exposes current values as plain read-only properties plus a `ClearRequested` event.

Exactly **one** instance exists, created in the `MainWindow` field initializer and moved between `StrokeBrushSlot` and `CompareBrushSlot` on tab change (`UpdateBrushRibbonHost`). A control can have only one logical parent in Avalonia, so both slots are cleared before assigning to the active one. On the Pressure response tab the ribbon stays detached. This keeps brush settings identical across the stroke tabs with no state syncing.

### `PressureChartControl`
Custom `Control` rendering with Avalonia's `DrawingContext`. Handles:
- Curve trace for all curve types (passthrough / flat / power / sigmoid / bezier)
- Standard min/max control nodes (pink/cyan) with optional dashed projection guides — shown for Sigmoid and Extended (Basic intentionally hides them)
- Bezier anchors + handles with selection highlight
- Hit-tested left-button drag of standard nodes, bezier anchors, and bezier handles
- Right-click context menu in the plot for `Add point at (x, y)` / `Remove point #i` / `Handles: Broken | Mirrored`
- Toolbar-callable `AddBezierPointAtLargestGap()` and `RemoveSelectedBezierPoint()`
- Live raw (purple) and effective (green) pressure indicators with dashed crosshair guides

When the user manipulates the chart, it writes a new `PressureCurveParams` back to its own `Params` StyledProperty. `MainWindow` subscribes to the change notification and mirrors the new values into the slider/combo UI (with a suppression flag to avoid feedback loops).

### `PressureResponseChartControl`
Renders the loaded `PressureResponseData` as gf-vs-percent. When `ShowCurveEffect` is true and `Params` is set, it transforms the Y axis through the active curve and relabels it. Live raw/effective pressure indicators are projected onto the response trace by interpolating the grams-force value at the indicator's Y.

Lives on its own tab. `MainWindow` auto-selects the first bundled sample at startup (combo index 1; index 0 is "(none)") so the chart isn't blank on first open.

### `LabeledSlider`
UserControl wrapping a label, click-to-edit value display (Enter commits, Esc cancels), and a `Slider` whose context menu offers `Min (x.xx)` / `Max (x.xx)` / `Reset (x.xx)`. Exposes a `ValueChanged` event for direct subscription, and a `ShowSlider` flag — the four input/output range sliders set it to `false`, showing label + value only, because those values are meant to be driven by dragging the chart's pink/cyan nodes.

### `DrawSurface`
Bundles an `SKBitmap`, `SKCanvas`, and Avalonia `WriteableBitmap` for **one or more** `Image` hosts. `AddHost(image)` registers an additional host; `EnsureSize(w, h)` (re)allocates on resize and preserves existing pixels; `Present()` blits the SkiaSharp pixels into the WriteableBitmap and invalidates every host; `SavePng(stream)` encodes the current bitmap to PNG.

Multi-host support is what lets the processed surface appear in both the Stroke tab and the Stroke compare tab as literally the same pixels:

```
_processed ──► StrokeView.Image
           └─► CompareProcessedView.Image
_raw       ──► CompareRawView.Image
```

`EnsureSurfaces()` sizes a surface from whichever host is *effectively visible* — that is, the active tab's. Checking `IsEffectivelyVisible` rather than `Bounds` avoids picking up stale cached layout from an inactive tab, which otherwise shows up as an X/Y displacement on the wrong canvas.

### `CurveMath` (static)
Pure math: `ApplyPressureCurve`, `RawCurveOutput`, `RawCurveSlope`, `CubicHermite`, `EvaluateCustomCurve`, `NormalizeBezierPoints`. No Avalonia dependencies — covered directly by the xUnit project.

### `PresetStore`
Loads/saves the user's named curve presets from `%LOCALAPPDATA%\PenDynamicsLab\presets.json`. JSON via `System.Text.Json` with `JsonStringEnumConverter` so enums are readable in the file.

### `PressureResponseLoader`
Reads pen hardware response JSON. Includes a custom `JsonConverter<ResponseRecord>` so each record can be a 2-element `[gf, logPct]` array. Bundles three WACOM KP-504E sample files as embedded resources.

## State flow

```
MainWindow._curveParams (PressureCurveParams)
   │
   ├──► PressureChart.Params           (re-render on change)
   ├──► ResponseChart.Params           (re-render on change)
   │
   ◄── slider ValueChanged / combo SelectionChanged / radio IsCheckedChanged
        UpdateParams(p => p with { ... })
   │
   ◄── PressureChart writes Params (drag node / handle / context menu)
        SyncCurveControlsFromParams()  (with _suppressCurveControlEvents = true)
```

Every control change funnels through `UpdateParams(Func<PressureCurveParams, PressureCurveParams>)` which rebuilds the immutable record with `with { ... }` and pushes it to both charts. Chart-driven changes round-trip through the same property and are mirrored back into the controls.

`UpdateBezierToolbar()` runs on every params change and drives per-curve-type control visibility. It also clamps `Softness` into the active range — Sigmoid restricts the slider to `[0, 0.95]` (steepness is `softness * 14`, and the top of the range is numerically unstable), everything else uses `[-0.9, 0.9]`.

## Pressure processing pipeline

```
PenSession point (pt.Pressure / pt.MaxPressure → 0..1 raw)
  │
  ▼
ProcessPressure:
   if SmoothThenCurve:                   if CurveThenSmooth:
     smoothed = ema(raw)                   curved   = curve(raw)
     curved   = curve(smoothed)            smoothed = ema(curved)
     output   = curved                     output   = smoothed
     preCurve = smoothed                   preCurve = raw
  │
  ├──► Telemetry labels
  ├──► PressureChart.LiveRawPressure = raw,  LivePressure = preCurve
  └──► ResponseChart same
  │
  ▼
ResolveActiveCanvas → which canvas the pen is over (Processed / Raw / None)
  │  (None → skip drawing; canvas switch resets _smoothedPos, _lastDrawPos)
  ▼
SmoothPosition (EMA on canvas-local x,y)
  │
  ▼
Draw segment on _processed using Output pressure
Draw segment on _raw       using Raw pressure
```

Two ordering details matter here:

1. **The pressure pipeline and the live indicators run for every pen point, whether or not the pen is over a canvas.** That's what keeps the charts live on the Pressure response tab, which has no drawing surface. Only the drawing steps are gated on canvas hit-testing.
2. **`ResolveActiveCanvas` only probes canvases in the visible tab**, for the same stale-layout reason as `EnsureSurfaces`. It translates the desktop point into each host's local frame and returns the first host containing it.

Both surfaces are drawn on every segment when their canvases exist — the processed one with the pipeline output, the raw one with unprocessed pressure. `_raw`'s canvas is only allocated once the compare tab has been visible, so on a fresh launch into the Stroke tab the raw draw is a no-op until the user visits Stroke compare.

Pressure → stroke parameters (`SizeFor` / `OpacityFor`, both reading `BrushRibbon` live):
- `PressureControl.Size`: stroke width = `max(1, pressure * brushSize)`, opacity = 1
- `PressureControl.Opacity`: stroke width = `brushSize`, opacity = `max(0.02, pressure)`

Stroke state (last position, smoothed position, smoothed pressure, live indicators) resets when:
- The pen lifts (no pressure for >200 ms or no points drained)
- The pen crosses between canvases (so a stroke doesn't "snap" across the divider)
- The user switches tabs
- The user clicks Clear, or presses Delete / Backspace with no `TextBox` focused

## Data model

`PressureCurveParams` (immutable record in `Curves/`):

| Field | Type | Range | Purpose |
|---|---|---|---|
| `CurveType` | `CurveType` enum | Passthrough, Flat, Basic, Extended, Sigmoid, Bezier | Active curve algorithm |
| `Softness` | `double` | -0.9 to 0.9 (Sigmoid: 0 to 0.95) | Power exponent / sigmoid steepness |
| `InputMinimum` | `double` | 0-1 | Start of input pressure range |
| `InputMaximum` | `double` | 0-1 | End of input pressure range |
| `Minimum` | `double` | 0-1 | Start of output pressure range |
| `Maximum` | `double` | 0-1 | End of output pressure range |
| `MinApproach` | `MinApproach` enum | Clamp, Cut | Behavior below input minimum |
| `FlatLevel` | `double` | 0-1 | Constant output for flat curve |
| `BezierPoints` | `ImmutableArray<BezierPoint>` | 2-16 points | Bezier control points |
| `EmaSmoothing` | `double` | 0-0.99 | Pressure EMA smoothing amount |
| `PositionEmaSmoothing` | `double` | 0-0.99 | Cursor position EMA smoothing |
| `SmoothingOrder` | `SmoothingOrder` enum | SmoothThenCurve, CurveThenSmooth | Pipeline order |

`BezierPoint`: `(X, Y, InX, InY, OutX, OutY, HandleMode)` — anchor + in handle + out handle + Broken/Mirrored mode.

Brush settings (`ColorMode`, `PressureControl`, brush size, draw-at-zero) are deliberately **not** part of this record — they're view state on `BrushRibbon` and aren't saved with user presets.

## Pressure response data schema

Bundled samples and uploaded JSON files match WebPressureExplorer's format:

```json
{
  "brand": "WACOM",
  "pen": "KP-504E",
  "inventoryid": "WAP.0038",
  "date": "2025-11-10",
  "tablet": "PTH-870",
  "records": [
    [82.0, 51.41]
  ]
}
```

Each record is `[gramForce, logicalPressurePercent]`. The custom `ResponseRecordConverter` reads each 2-element array and produces a `ResponseRecord(Gf, LogicalPercent)`.

## Pen input

Pen events come from `IPenSession` (WinPenKit, referenced as a sibling project — see OVERVIEW). `MainWindow` lets the user pick between:
- `InputApi.WintabSystem` — Wintab system context, screen-pixel output, pen drives the cursor
- `InputApi.WintabDigitizer` — Wintab digitizer context, tablet-native output scaled to desktop coords, preserving sub-pixel precision
- `InputApi.AvaloniaPointer` — Avalonia pointer events (`AvaloniaPointerSession` wrapping `CanvasArea`)

`InputApi.WmPointer` is filtered out of the list: Avalonia consumes those messages itself, so the window subclass never sees them. Each session exposes `DrainPoints()` returning a batch of `PenPoint` records with desktop coords, raw coords, pressure, azimuth, altitude, twist, and cursor type. Changing the combo tears down the old session and starts a new one; a start failure is surfaced in the window title.

## Key design points

1. **Immutable params record** — `PressureCurveParams` is a `record` with `init` properties. Every change is a `with { ... }` rebuild, which makes change detection and parity-test reasoning straightforward.
2. **Pure math separation** — `Curves/CurveMath.cs` has no Avalonia or SkiaSharp dependencies. The xUnit project pins behavior with 23 tests against analytically-derived values.
3. **Avalonia DrawingContext for charts; SkiaSharp for canvases** — Charts are simple line geometry and benefit from Avalonia's text rendering + transform stack. The drawing canvases need many small antialiased strokes per frame, where SkiaSharp via `SKBitmap`/`WriteableBitmap` interop is faster.
4. **Single owner of state** — `MainWindow` holds the params and the surfaces; everything else is a leaf control receiving values via StyledProperties or queried for its current value. Even the chart's own edits round-trip through this owner.
5. **One surface, many views** — `DrawSurface` supports multiple `Image` hosts so the processed canvas is shared between tabs rather than copied. Sizing and hit-testing both key off `IsEffectivelyVisible` to avoid stale inactive-tab layout.
6. **One ribbon, reparented** — rather than duplicating brush UI per tab and syncing it, a single `BrushRibbon` moves between tab slots.
7. **Stroke-local smoothing reset** — EMA state resets on every pen lift, canvas switch, and tab switch, so smoothing tails don't bleed across strokes or between canvases.
