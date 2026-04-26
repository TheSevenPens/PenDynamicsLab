# Architecture

## Window layout

```
MainWindow
├── DriverWarningBanner (dismissible)
├── Top ribbon (API selector + pen telemetry)
└── Body Grid
    ├── Left panel (480 px)
    │   ├── PressureChartControl (320 px tall)  + "Save chart..." button
    │   └── ScrollViewer
    │       ├── Curve type combo
    │       ├── Bezier toolbar (Add / Remove / preset combo, visible only for Bezier)
    │       ├── LabeledSlider × N  (softness, in/out range, transition width, flat level)
    │       ├── Min approach radios
    │       ├── SMOOTHING section (pressure EMA, position EMA, smoothing order)
    │       ├── PRESSURE RESPONSE section
    │       │   ├── Data combo (none / 3 samples / Upload JSON...)
    │       │   ├── "Show effect of curve" checkbox + info label
    │       │   └── PressureResponseChartControl
    │       └── USER PRESETS section (name input + Save + dynamic list)
    ├── 1px splitter
    └── Right panel
        ├── Brush ribbon (size, color, pressure target, draw-zero, clear)
        └── Split canvas Grid (header + ProcessedImage + divider + header + RawImage)
```

`MainWindow.axaml.cs` owns essentially all state and behavior; controls communicate via events and StyledProperties.

## Component roles

### `MainWindow`
Single source of truth. Owns:
- `_curveParams` — immutable `PressureCurveParams` record
- Two `DrawSurface` instances (`_processed`, `_raw`) for the split canvas bitmaps
- Brush state (`_brushSize`, `_colorMode`, `_pressureControl`, `_drawZeroPressure`, `_strokeColor`)
- Stroke-local smoothing state (`_smoothedPressure`, `_smoothedPos`, `_lastDrawPos`, `_activeCanvas`)
- The `PresetStore` and the live `IPenSession`

The render timer (16 ms tick) drains pen points from the session, runs them through the pressure pipeline, draws line segments to both canvases, and updates the live indicators on both charts.

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

### `LabeledSlider`
UserControl wrapping a label, click-to-edit value display (Enter commits, Esc cancels), and a `Slider` whose context menu offers `Min (x.xx)` / `Max (x.xx)` / `Reset (x.xx)`. Exposes a `ValueChanged` event for direct subscription.

### `DrawSurface`
Bundles an `SKBitmap`, `SKCanvas`, and Avalonia `WriteableBitmap` for one image host. `EnsureSize(w, h)` (re)allocates on resize and preserves existing pixels. `Present()` blits the SkiaSharp pixels into the WriteableBitmap and invalidates the host. `SavePng(stream)` encodes the current bitmap to PNG.

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

## Pressure processing pipeline

```
PenSession point (pt.Pressure / pt.MaxPressure → 0..1 raw)
  │
  ▼
ResolveActiveCanvas → which sub-canvas the pen is over (Processed / Raw / None)
  │  (canvas switch resets _smoothedPressure, _smoothedPos, _lastDrawPos)
  ▼
SmoothPosition (EMA on canvas-local x,y)
  │
  ▼
ProcessPressure:
   if SmoothThenCurve:                   if CurveThenSmooth:
     smoothed = ema(raw)                   curved   = curve(raw)
     curved   = curve(smoothed)            smoothed = ema(curved)
     output   = curved                     output   = smoothed
     preCurve = smoothed                   preCurve = raw
  │
  ▼
Draw segment on _processed using Output pressure
Draw segment on _raw       using Raw pressure
  │
  ▼
PressureChart.LiveRawPressure = raw,  LivePressure = preCurve
ResponseChart same
```

Pressure → stroke parameters (`SizeFor` / `OpacityFor`):
- `PressureControl.Size`: stroke width = `max(1, pressure * brushSize)`, opacity = 1
- `PressureControl.Opacity`: stroke width = `brushSize`, opacity = `max(0.02, pressure)`

Stroke state (last position, smoothed position, smoothed pressure) resets when:
- The pen lifts (no pressure for >200 ms or no points drained)
- The pen crosses between the processed and raw sub-canvases (so a stroke doesn't "snap" across the divider)
- The user clicks Clear

## Data model

`PressureCurveParams` (immutable record in `Curves/`):

| Field | Type | Range | Purpose |
|---|---|---|---|
| `CurveType` | `CurveType` enum | Passthrough, Flat, Basic, Extended, Sigmoid, Bezier | Active curve algorithm |
| `Softness` | `double` | -0.9 to 0.9 | Power exponent / sigmoid steepness |
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
    [82.0, 51.41],
    ...
  ]
}
```

Each record is `[gramForce, logicalPressurePercent]`. The custom `ResponseRecordConverter` reads each 2-element array and produces a `ResponseRecord(Gf, LogicalPercent)`.

## Pen input

Pen events come from `IPenSession` (WinPenSession). `MainWindow` lets the user pick between:
- Wintab system-cursor session
- Wintab high-resolution digitizer session
- Avalonia pointer events (subclasses `CanvasArea` to receive Avalonia's pen pointer stream)

WM_POINTER subclassing is excluded because Avalonia consumes those messages itself and the subclass never sees them. Each session exposes `DrainPoints()` returning a batch of `PenPoint` records with desktop coords, raw coords, pressure, azimuth, altitude, twist, and cursor type.

## Key design points

1. **Immutable params record** — `PressureCurveParams` is a `record` with `init` properties. Every change is a `with { ... }` rebuild, which makes change detection and parity-test reasoning straightforward.
2. **Pure math separation** — `Curves/CurveMath.cs` has no Avalonia or SkiaSharp dependencies. The xUnit project pins behavior with 27 tests against analytically-derived values.
3. **Avalonia DrawingContext for charts; SkiaSharp for canvases** — Charts are simple line geometry and benefit from Avalonia's text rendering + transform stack. The drawing canvases need many small antialiased strokes per frame, where SkiaSharp via `SKBitmap`/`WriteableBitmap` interop is faster.
4. **Single owner of state** — `MainWindow` holds the params and the surfaces; everything else is a leaf control receiving values via StyledProperties. Even the chart's own edits round-trip through this owner.
5. **Stroke-local smoothing reset** — EMA state resets on every pen lift and on canvas switch, so smoothing tails don't bleed across strokes or between the processed/raw sub-canvases.
