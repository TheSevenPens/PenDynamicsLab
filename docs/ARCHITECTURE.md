# Architecture

## Window layout

```
MainWindow
├── DriverWarningBanner (dismissible)
├── Top ribbon (API selector + pen telemetry)
└── Body Grid
    ├── Left panel (592 px) — chart column + card column
    │   ├── Chart column
    │   │   ├── PressureChartControl
    │   │   └── "Copy ▾" / "Save ▾" (each: Full chart | Plot area only) + status label
    │   └── Card column (330 px, scrolls) — SectionCard × 4
    │       ├── CURVE (OFF)
    │       │   ├── Curve type combo + reset
    │       │   ├── Bezier toolbar (Add / Remove / count / preset combo, Bezier only)
    │       │   ├── LabeledSlider × N (Curve Amount, in/out range, flat level)
    │       │   └── Min approach radios
    │       ├── SMOOTHING (OFF) — algorithm combo (Passthrough / EMA) + reset, Smoothing Amount
    │       ├── PROCESSING ORDER — smooth-then-curve / curve-then-smooth radios
    │       └── PRESETS — empty-state text, saved list, "Save settings"
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
- Stroke-local pressure-smoothing state (`_smoothedPressure`, `_lastDrawPos`, `_activeCanvas`)
- The `PresetStore` and the live `IPenSession`

The render timer (16 ms tick) drains pen points from the session, runs them through the pressure pipeline, draws line segments to the surfaces, and updates the live indicators on both charts.

Brush state is *not* stored on `MainWindow` — it's read on demand from `BrushRibbon`'s properties (`BrushSize`, `ColorMode`, `PressureControl`, `DrawZeroPressure`) at draw time. Only `_strokeColor` (the currently-picked random palette entry) lives on the window.

### `StrokeCanvasView`
A `UserControl` bundling a header label, a "Save..." button, and an `Image`. It does **not** own pixel data — it exposes `Image` (register with a `DrawSurface`), `Host` (the `Border` whose bounds drive surface size), a `Header` styled property, and a `SaveRequested` event. The `Image` sits inside a `Canvas` pinned at (0, 0) so an oversized shared bitmap doesn't get re-laid-out when it's larger than the current host.

The `Image` uses `Stretch="Fill"` with no size set in the markup: `DrawSurface` assigns its `Width`/`Height` at allocation time. See the HiDPI section below for why.

Three instances exist: `StrokeView`, `CompareProcessedView`, `CompareRawView`.

### `SectionCard`
A collapsible titled panel; the left-hand card column is four of them. Exposes `Title`, a
`Status` suffix (used for the derived `(OFF)` marker), `IsExpanded`, and `CardContent`.
Clicking anywhere in the header row toggles the body.

The body must be set with the property-element form:

```xml
<controls:SectionCard Title="CURVE">
    <controls:SectionCard.CardContent>
        <StackPanel>…</StackPanel>
    </controls:SectionCard.CardContent>
</controls:SectionCard>
```

> **Do not mark `CardContent` with `[Content]`** to allow the shorter child-element syntax.
> That attribute also applies when `AvaloniaXamlLoader` loads `SectionCard.axaml` itself, so
> the card's own `Border` chrome is assigned to `CardContent` instead of `Content`. The
> UserControl is then empty, measures to zero height, and every card renders as nothing at
> all — with no error to point at it.

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
Bundles an `SKBitmap`, `SKCanvas`, and Avalonia `WriteableBitmap` for **one or more** `Image` hosts. `AddHost(image)` registers an additional host; `EnsureSize(dipWidth, dipHeight, scale)` (re)allocates when the DIP size *or* the render scaling changed, preserving existing pixels; `Present()` blits the SkiaSharp pixels into the WriteableBitmap and invalidates every host; `SavePng(stream)` encodes the current bitmap to PNG at full physical resolution.

Multi-host support is what lets the processed surface appear in both the Stroke tab and the Stroke compare tab as literally the same pixels:

```
_processed ──► StrokeView.Image
           └─► CompareProcessedView.Image
_raw       ──► CompareRawView.Image
```

`EnsureSurfaces()` sizes a surface from whichever host is *effectively visible* — that is, the active tab's. Checking `IsEffectivelyVisible` rather than `Bounds` avoids picking up stale cached layout from an inactive tab, which otherwise shows up as an X/Y displacement on the wrong canvas.

`DrawSurface` is also where Avalonia's layout units are reconciled with physical pixels — see [HiDPI](#hidpi-dips-vs-physical-pixels) below, which is required reading before changing anything in this class.

### `CurveMath` (static)
Pure math: `ApplyPressureCurve`, `RawCurveOutput`, `RawCurveSlope`, `CubicHermite`, `EvaluateCustomCurve`, `NormalizeBezierPoints`. No Avalonia dependencies — covered directly by the xUnit project.

### `PresetStore`
Loads/saves the user's named curve presets from `%LOCALAPPDATA%\PenDynamicsLab\presets.json`. JSON via `System.Text.Json` with `JsonStringEnumConverter` so enums are readable in the file.

### `PressureResponseLoader`
Reads pen hardware response JSON. Includes a custom `JsonConverter<ResponseRecord>` so each record can be a 2-element `[gf, logPct]` array. Bundles three WACOM KP-504E sample files as embedded resources.

## HiDPI: DIPs vs physical pixels

Avalonia lays out in **device-independent units** (DIPs); a display at 225% scaling paints each DIP across 2.25 physical pixels. Only two places in the app are aware of that: `DrawSurface` (below) and `SaveControlAsPngAsync` (at the end of this section). Everything else — the pressure pipeline, hit-testing, brush sizing — works in DIPs and stays oblivious.

`DrawSurface` gets three things right at once:

| Concern | How |
|---|---|
| Backing store resolution | `SKBitmap` is allocated at `dip * scale` **physical pixels**, so strokes are stored at the display's true resolution |
| Drawing coordinates | The `SKCanvas` carries a `Scale(scale)` transform, so all caller code — positions, brush widths — stays in **DIP space** and needs no changes |
| On-screen mapping | Each host `Image` gets an explicit `Width`/`Height` in DIPs (`ApplyToHost`), which with `Stretch="Fill"` maps the backing store 1:1 |

`Width` / `Height` on the surface are physical pixels; `DipWidth` / `DipHeight` are the DIP equivalents, derived from the *rounded* pixel count so that `DipWidth * Scale == Width` exactly and the mapping stays 1:1 rather than drifting by a rounding error. Because the DIP size is always `pixels / scale`, `Stretch="Fill"` resolves to an identity transform, not a resample.

> **The `new Vector(96, 96)` DPI tag is deliberate — do not "fix" it.**
>
> It is tempting to tag the `WriteableBitmap` with its true density (`96 * scale`), since that is what the bitmap genuinely is. Doing so breaks rendering. The tag makes `Bitmap.Size` report **DIPs** rather than pixels, and Avalonia derives the **source rectangle** from `Bitmap.Size` while treating it as pixels — so only the top-left `pixels / scale` corner of the bitmap is sampled and then stretched over the whole host. Visually: strokes drift further from the pen tip the further the pen is from the canvas origin, displaced by exactly the scaling factor.
>
> Leaving the tag at 96 keeps `Bitmap.Size == PixelSize`, so the whole bitmap is the source. The DIP size is carried by the host's explicit `Width`/`Height` instead, which avoids depending on those semantics at all.

Two further details:

- **Scaling changes.** Dragging the window to a monitor with different DPI keeps it the same size in DIPs, so no `Bounds` change fires. `MainWindow` subscribes to `ScalingChanged` and re-runs `EnsureSurfaces`, and the allocation cache compares `Scale` as well as size. On such a reallocation the preserve-pixels blit resamples by the scale ratio, so existing content keeps its apparent size instead of jumping.
- **Blit ordering.** That preserve-pixels blit runs *before* the `Scale` transform is applied to the new canvas, so it works in device space and resized content isn't double-scaled.

A calibration pattern (a full-extent border plus ticks every 100 DIP, measured against a screenshot in physical pixels) is the quickest way to check this end to end if it is ever touched again: at 225% the ticks must land exactly 225 physical pixels apart, and both border edges must be visible.

### Image export

`SaveControlAsPngAsync` (used by "Save chart...") has the same DIP-vs-pixel problem: a `RenderTargetBitmap` built from `Bounds` at 96 DPI produces a file at `1/scale` of the on-screen resolution. It sizes the target in physical pixels and tags it `96 * scale`.

That is the **opposite** of the canvas rule above, and deliberately so. `RenderTargetBitmap` is a render *target*, not a source bitmap: the tag tells Avalonia how to rasterize the visual into it, so scaling it up is exactly what's wanted. The canvas path passes its bitmap as a *source*, where the same tag instead changes how the source rectangle is derived. Same parameter, two different roles.

The stroke canvases need no equivalent handling on save — `DrawSurface.SavePng` encodes the `SKBitmap` directly, which is already at physical resolution.

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
  │  (None → skip drawing; canvas switch resets _lastDrawPos)
  ▼
(canvas-local x,y used as-is — position is not smoothed)
  │
  ▼
Draw segment on _processed using Output pressure
Draw segment on _raw       using Raw pressure
```

Two ordering details matter here:

1. **The pressure pipeline and the live indicators run for every pen point, whether or not the pen is over a canvas.** That's what keeps the charts live on the Pressure response tab, which has no drawing surface. Only the drawing steps are gated on canvas hit-testing.
2. **`ResolveActiveCanvas` only probes canvases in the visible tab**, for the same stale-layout reason as `EnsureSurfaces`. It translates the desktop point into each host's local frame and returns the first host containing it.

Both surfaces are drawn on every segment when their canvases exist — the processed one with the pipeline output, the raw one with unprocessed pressure. `_raw`'s canvas is only allocated once the compare tab has been visible, so on a fresh launch into the Stroke tab the raw draw is a no-op until the user visits Stroke compare.

Every coordinate in this pipeline — `clientPt`, the host-local point, and the stroke widths from `SizeFor` — is in **DIPs**. Nothing here is aware of the display scaling; `DrawSurface`'s canvas transform converts to physical pixels at the point of drawing. See [HiDPI](#hidpi-dips-vs-physical-pixels).

Pressure → stroke parameters (`SizeFor` / `OpacityFor`, both reading `BrushRibbon` live):
- `PressureControl.Size`: stroke width = `max(1, pressure * brushSize)`, opacity = 1
- `PressureControl.Opacity`: stroke width = `brushSize`, opacity = `max(0.02, pressure)`

Stroke state (last position, smoothed pressure, live indicators) resets when:
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
| `SmoothingType` | `SmoothingType` enum | Passthrough, Ema | Smoothing algorithm; Passthrough skips smoothing entirely |
| `EmaSmoothing` | `double` | 0-0.99 | Pressure EMA smoothing amount (ignored when Passthrough) |
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
2. **Pure math separation** — `Curves/CurveMath.cs` has no Avalonia or SkiaSharp dependencies. The xUnit project pins behavior with 30 tests against analytically-derived values.
3. **Avalonia DrawingContext for charts; SkiaSharp for canvases** — Charts are simple line geometry and benefit from Avalonia's text rendering + transform stack. The drawing canvases need many small antialiased strokes per frame, where SkiaSharp via `SKBitmap`/`WriteableBitmap` interop is faster.
4. **Single owner of state** — `MainWindow` holds the params and the surfaces; everything else is a leaf control receiving values via StyledProperties or queried for its current value. Even the chart's own edits round-trip through this owner.
5. **One surface, many views** — `DrawSurface` supports multiple `Image` hosts so the processed canvas is shared between tabs rather than copied. Sizing and hit-testing both key off `IsEffectivelyVisible` to avoid stale inactive-tab layout.
6. **DIPs in, pixels out** — callers draw entirely in device-independent units; `DrawSurface` alone knows the render scaling, allocating at physical resolution and carrying a matching canvas transform. Keeping that conversion in one class is what lets the pressure pipeline, brush sizing, and hit-testing all ignore DPI. See [HiDPI](#hidpi-dips-vs-physical-pixels).
7. **One ribbon, reparented** — rather than duplicating brush UI per tab and syncing it, a single `BrushRibbon` moves between tab slots.
8. **Stroke-local smoothing reset** — EMA state resets on every pen lift, canvas switch, and tab switch, so smoothing tails don't bleed across strokes or between canvases.
