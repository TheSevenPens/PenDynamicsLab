# Architecture

## Window layout

```
MainWindow
├── Top ribbon (92 px, collapsible to 36) — Pen API combo + pen telemetry (fixed-width readout columns), DriverTipChip, the collapse chevron, then the Options gear at the right edge
└── Body Grid
    ├── Left panel (472 px) — two equal-width columns, settings before curves
    │       (one curve by default; a second card and two more charts appear with UseTwoCurves)
    │   ├── Settings column
    │   │   ├── ScrollViewer — SectionCard × 4 or 5
    │   │   │   ├── Quantization  [Off | On] — level combo (Passthrough, 8192 … 2)
    │   │   │   ├── Curve 1  [Off | On · no effect | On] — CurveEditorView
    │   │   │   │   ├── Curve type combo + type-scoped reset
    │   │   │   │   ├── Bezier toolbar (Add / Remove / count / preset combo, Bezier only)
    │   │   │   │   ├── LabeledSlider × N (Curve Amount, in/out range, flat level)
    │   │   │   │   └── Min approach radios
    │   │   │   ├── Curve 2  [same] — CurveEditorView          (only when UseTwoCurves)
    │   │   │   ├── Smoothing  [Off | On · no effect | On] — algorithm combo (Passthrough / EMA) + type-scoped reset, Smoothing Amount
    │   │   │   └── Processing  [S → C | S → C1 → C2] — smooth-first / curve-first dropdown
    │   │   └── Presets (pinned to the bottom row) — empty-state text, saved list, "Save current settings"
    │   └── Curve column
    │       ├── "Pressure curve 1" card → PressureChartControl (export on its right-click menu)
    │       ├── "Pressure curve 2" card → PressureChartControl        (only when UseTwoCurves)
    │       ├── "Effective pressure curve" card [pill] → EffectiveCurveChartControl (read-only, ditto)
    │       └── transient status label ("Copied")
    ├── 1px splitter
    └── CanvasArea (DockPanel)
        └── RightTabs (TabControl)
            ├── "Stroke"
            │   ├── StrokeBrushSlot (ContentControl — hosts the shared BrushRibbon)
            │   └── StrokeView : StrokeCanvasView
            ├── "Stroke compare"
            │   ├── CompareBrushSlot (ContentControl — hosts the shared BrushRibbon)
            │   └── Grid
            │       ├── CompareProcessedView : StrokeCanvasView  ("Use processed pressure data")
            │       ├── 1px divider
            │       └── CompareRawView : StrokeCanvasView        ("Use raw pressure data")
            └── "Pressure response"
                ├── Data combo + Clear button
                ├── "Show effect of curve" checkbox + info label
                └── PressureResponseChartControl
```

The columns sit in this order deliberately: the row reads **configure → mapping → stroke**, and it puts the curve directly against the canvas it drives, which is the pair you compare while tuning. Presets is pinned to the bottom row of its column so the slack at default settings falls between groups rather than trailing off the end.

`MainWindow.axaml.cs` owns essentially all state and behavior; controls communicate via events and StyledProperties.

`CanvasArea` (the whole right-hand DockPanel) is also the Avalonia element passed to `AvaloniaPointerSession`, so the Avalonia-pointer input path receives events across every tab.

## Visual language

One vocabulary, defined in `Window.Styles` plus a set of theme-brush overrides in `App.axaml`. The rules that keep it coherent:

| Rule | Why |
|---|---|
| No uppercase labels | Sentence case throughout; hierarchy comes from colour and size, never caps. The bold uppercase micro-labels were the app's strongest dated signal. |
| No separator rules between controls | A 1px vertical line is what you reach for when spacing has failed. 20px gaps instead; horizontal rules only where two *regions* meet. |
| One control height | Every combo, button and text field is 32 px, vertically centred. A slider occupies a 32 px box though its track is 4. |
| State in a pill, not the label | `Curve` plus an `Off` chip, so the name stays stable and only the state moves. Three states, three tones: grey `Off`, amber `On · no effect`, accent `On`. |
| Telemetry never reflows | Each readout column reserves the width of the widest value it can hold, so the ribbon groups keep fixed widths and positions while the numbers change. See "Ribbon width sizers" below. |
| No literal colours outside the palette | Every colour is a `Pdl.*` token in `Theming/Palette.axaml`, referenced with `{DynamicResource}`. A literal hex in a control is a colour that cannot follow the theme. |

Tokens: surface `#FFFFFF`, pane `#F9F9F9`, canvas paper `#F7F7F4`, plot field `#F7F7FB`, divider `#E5E5E5`, control edge `#D1D1D1`, text `#242424`, muted `#616161`, accent `#0F6CBD`. Body type is 13, labels 12, tabs 14; control radius 4, card radius 8.

The four data colours — raw `#8833CC`, effective `#14A050`, min node `#FF0088`, max node `#00D0FF` — are deliberately *not* part of this system. They carry meaning, match WebPressureExplorer, and are louder than the chrome on purpose: chrome should recede, data should not.

> **Button fills come from `App.axaml`, not a restyled template.** Fluent's stock `ButtonBackground` is a mid grey that reads as a dark slab against white cards, so the `Button*` brushes are overridden there. Doing it with theme brushes rather than a control template is what keeps the deliberate exceptions working without special-casing: the card headers and tip chip set `Background="Transparent"` locally and a local value still wins, while the accent button draws from the separate `AccentButton*` resources.

## Component roles

### `MainWindow`
Single source of truth. Owns:
- `_curveParams` — immutable `PressureCurveParams` record
- Two `DrawSurface` instances (`_processed`, `_raw`) for the stroke bitmaps
- The single shared `BrushRibbon` instance, reparented between tab slots
- Stroke-local pressure-smoothing state (`_smoothedPressure`, `_lastDrawPos`, `_activeCanvas`)
- The `PresetStore`, the `UiSettings`, and the live `IPenSession`

The render timer (16 ms tick) drains pen points from the session, runs them through the pressure pipeline, draws line segments to the surfaces, and updates the live indicators on both charts.

Brush state is *not* stored on `MainWindow` — it's read on demand from `BrushRibbon`'s properties (`BrushSize`, `ColorMode`, `PressureControl`, `DrawZeroPressure`) at draw time. Only `_strokeColor` (the colour in force for the current stroke) lives on the window.

### `StrokeCanvasView`
A `UserControl` bundling a header pill and an `Image`. It does **not** own pixel data — it exposes `Image` (register with a `DrawSurface`), `Host` (the `Border` whose bounds drive surface size), a `Header` styled property, and `SaveRequested` / `CopyRequested` / `ClearRequested` events. The `Image` sits inside a `Canvas` pinned at (0, 0) so an oversized shared bitmap doesn't get re-laid-out when it's larger than the current host.

Copy, save and clear live on the canvas's own **right-click menu**, matching the curve chart, so no chrome competes with the drawing surface. `MainWindow` wires all three views' `ClearRequested` to `ClearCanvases`: the processed and raw surfaces are two views of one stroke, so clearing only the half under the cursor would leave the comparison mismatched.

The `Image` uses `Stretch="Fill"` with no size set in the markup: `DrawSurface` assigns its `Width`/`Height` at allocation time. See the HiDPI section below for why.

Three instances exist: `StrokeView`, `CompareProcessedView`, `CompareRawView`.

### `SectionCard`
A collapsible titled panel; the left-hand column is four of them. Exposes `Title`,
`Status`, `StatusKind`, `IsExpanded`, and `CardContent`. Clicking anywhere in the header
row toggles the body.

`Status` renders as a **pill after the title** (`Curve` · `Off`), not as a suffix inside
it, so the name stays stable while only the state moves. The header is a `DockPanel`,
which fills in child order — the title element must therefore be declared *before* the
pill, or the state leads the row.

`StatusKind` is a `StatusTone` — `Neutral` (grey `#F0F0F0`/`#616161`), `Active` (accent
`#EFF6FC`/`#115EA3`) or `Advisory` (amber `#FFF9F0`/`#7A5A16`). Colour carries the
distinction so the label can stay short, and Advisory borrows the driver tip's amber,
which already means "worth a look" everywhere else in the app. `MainWindow` maps
`StageState` onto these in `ApplyStageStatus`.

The body must be set with the property-element form:

```xml
<controls:SectionCard Title="Curve">
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
A `UserControl` toolbar: brush size slider, colour mode and pressure-target dropdowns, draw-at-zero checkbox, and Clear. Exposes current values as plain read-only properties plus a `ClearRequested` event.

Exactly **one** instance exists, created in the `MainWindow` field initializer and moved between `StrokeBrushSlot` and `CompareBrushSlot` on tab change (`UpdateBrushRibbonHost`). A control can have only one logical parent in Avalonia, so both slots are cleared before assigning to the active one. On the Pressure response tab the ribbon stays detached. This keeps brush settings identical across the stroke tabs with no state syncing.

### `PressureChartControl`
Editor for **one** `CurveSettings`; two instances are curve 1 and curve 2. Custom `Control` rendering with Avalonia's `DrawingContext`. Handles:
- Curve trace for all curve types (passthrough / flat / power / inverted / sigmoid / bezier)
- Standard min/max control nodes (pink/cyan) with optional dashed projection guides — shown only for the types `CurveMath.UsesRangeControls` names, so a node can never appear on a curve that would ignore it
- Bezier anchors + handles with selection highlight
- Hit-tested left-button drag of standard nodes, bezier anchors, and bezier handles
- Right-click context menu in the plot: the bezier entries (`Add point at (x, y)` / `Remove point #i` / `Handles: Broken | Mirrored`) when they apply, then the owner-supplied export entries
- Toolbar-callable `AddBezierPointAtLargestGap()` and `RemoveSelectedBezierPoint()`
- Live raw (purple) and effective (green) pressure indicators with dashed crosshair guides

The chart draws **no text at all** — no axis titles, numeric labels or tick marks. The grid marks the quarter points, and both axes run 0-1 by definition, so the titles were labelling the obvious; the card's own heading already says what the chart is. That leaves a single uniform `Pad` (16 px) on all four sides, sized only to keep a node centred on the plot boundary from being clipped — `NodeDrawRadius` is 6, so a node at (0, 0) or (1, 1) clears the card edge comfortably.

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
Pure math, no Avalonia dependencies — covered directly by the xUnit project. The public surface is `ApplyCurve` (one curve), `ApplyPressureCurve` (the pair composed: `ApplyCurve(ApplyCurve(x, Curve1), Curve2)`), `RawCurveOutput`, `EvaluateCustomCurve`, `NormalizeBezierPoints`, and `UsesRangeControls`, which is the single source of truth for which curve types honour the input/output range fields — the evaluator, the chart's min/max nodes and `StageStatus` all defer to it rather than repeating the list.

The bezier solver (`BuildCustomSegments`, `CubicAt`, `SolveBezierTForX`) is private: callers go through `EvaluateCustomCurve`. `SigmoidSteepness` (14) and `SigmoidLinearThreshold` (0.01) are exposed as constants so the "no effect" pill can mirror the exact threshold below which the evaluator degenerates to a straight line, instead of guessing at one.

### Ribbon width sizers

The telemetry readouts sit in `Auto` grid columns, which means the column — and therefore the group, and therefore every group to its right — resized every time a value changed. `Raw` going from `--` to `44704, 27940` shoved Pressure and Orientation sideways, and the ribbon shuffled continuously while the pen moved.

Each value column now carries an extra `TextBlock` with `Classes="sizer"` holding the widest string that column can ever hold (`888888, 888888` for a position pair, `888888` for a pressure count, `-888.8°` for an angle), in the same grid cell as the live label. `Auto` then resolves to the sizer's width and stays there. The Pen group gets the same treatment for `Out of range`, which is wider than the `In range` it toggles to.

Two details matter. The sizer uses `Opacity="0"`, **not** `IsVisible="False"` — the latter collapses it out of layout, which is the one thing it must not do. And the width is expressed as glyphs rather than a pixel constant, so it stays correct if the font, the font size or the DPI changes; the digit `8` is the widest digit, so a run of them is the safe reservation.

The field labels are static text, so their `Auto` column was never the problem and is left alone.

### Theming

`Theming/Palette.axaml` is a `ResourceDictionary` with `ThemeDictionaries` for `Light` and `Dark`, merged into `App.axaml`. Every colour the app draws is a `Pdl.*` key in it; XAML reads them with **`{DynamicResource}`**, never `{StaticResource}` — a static reference resolves once at load and silently keeps its old colour through a theme change.

The light column is the palette the app already shipped, lifted value for value, so light mode is unchanged by the move.

**`ThemeService`** maps the saved `AppTheme` onto Avalonia's `ThemeVariant`. The mapping is the whole trick: `ThemeVariant.Default` is not a third palette, it means *take the platform's* — so `AppTheme.System` maps to it and Avalonia keeps `ActualThemeVariant` in step with the Windows app-mode setting by itself, including while the app runs. Nothing polls. `MainWindow`'s constructor applies the saved preference **before** `InitializeComponent`, so the window paints correctly on its first frame rather than flashing light and correcting.

**`ThemeInk`** exists for the two chart controls, which paint with `DrawingContext` where `{DynamicResource}` is unavailable. Their colours were `static readonly` fields — exactly the shape a theme switch cannot reach — and are now instance fields seeded with the light values and refreshed on `ActualThemeVariantChanged`. The seed doubles as the fallback, so a missing key degrades to the old appearance rather than to a blank chart.

Two deliberate exclusions:

- **The four data colours** — raw purple, effective green, min pink, max cyan — stay literal in both themes. They carry meaning, they match WebPressureExplorer, and they are louder than the chrome on purpose.
- **`Pdl.CanvasPaper` is identical in both themes.** The drawing surface keeps its paper: strokes default to black, so a dark canvas would swallow them, and a canvas exported from dark would not match one exported from light. Theme is one setting, not two. It needs no border of its own either — the canvas header's divider and the pane splitter already bound it.

### `OptionsWindow`
Global app options, opened by the gear at the right end of the telemetry ribbon. A category rail on the left (Appearance, Curves) and a detail pane on the right, so a further option group is one more row rather than a taller dialog.

> **Rail rows are toned by a style class, not from code.** Fluent paints a button's fill on the template's `ContentPresenter`, and its `:pointerover` setter beats a locally-set `Button.Background` — so a code-set selection colour vanishes under the cursor. Styles also let the tone come from `{DynamicResource}`, which a code-set brush cannot without re-toning on every theme change.

The gear is declared **before** `DriverTipChip` in the ribbon's `DockPanel`, which fills in child order, so it takes the outermost right slot. The tip chip beside it is dismissible; the other order would slide the gear sideways the moment the tip went away.

There is no OK / Cancel. `ThemeService.Set` applies and persists in one call, so the window owns no draft state and has nothing to roll back — and with the change already live behind a modal dialog, the app itself is the preview. Close is the only button.

### `CurveEditorView`
The controls for one curve — type combo, the sliders that type uses, the bezier toolbar, the reset button. Two instances are curve 1 and curve 2.

It exists so a second curve costs one more instance rather than a second copy of ten named controls and their handlers. It never writes to its own `Curve` property from its event handlers: it raises `CurveChanged` and `MainWindow` writes back, so exactly one place decides what the current parameters are.

> **Radio groups are matched by name across the whole window.** Two instances sharing `GroupName="MinApproach"` would let curve 2's *Cut* clear curve 1's *Clamp*. The constructor gives each instance its own group name.

### Quantization

`Quantization.Apply(x, levels)` coarsens pressure to `levels` steps by **ceiling**: zero only when the pen reports zero, and above that exactly `N` equal-width buckets. Level `N` yields `N + 1` possible values — `0, 1/N, 2/N … 1`. At two levels, `(0, 0.5]` gives 0.5 and `(0.5, 1]` gives 1.

Ceiling rather than nearest or floor, deliberately. Nearest turns the bottom `1/(2N)` of the range into zero, so at low levels a light touch makes no mark at all. Floor makes the top bucket a single point — full pressure only at exactly the pen's maximum, which never happens. Ceiling gives the `N` equal nonzero buckets the level number promises, at the cost of no soft entry: the faintest contact registers at `1/N`.

The ceiling carries a `1e-9` epsilon. Without it a value that should sit exactly on a bucket edge but lands a hair above in floating point — `3.0000000000000004` rather than `3` — is pushed a whole bucket up. At 8192 levels a bucket is `0.0001` wide, so an epsilon that small cannot swallow a real one.

**This stage has no order setting and is always first.** It models the resolution the pressure arrived at, and nothing downstream can restore detail it has discarded — so the Processing card stays about smoothing and the curves only, and its pill does not mention quantization.

It is also deliberately **absent from the effective curve chart**, which keeps meaning "the two curves composed".

The offered levels mirror real tablet pressure resolutions, which is what makes the setting legible — picking 1024 on an 8192-level pen shows what that pen would feel like. The bottom three (8, 4, 2) are below any real hardware and exist because that is where the effect becomes unmistakable on screen.

### Curve count

`UiSettings.UseTwoCurves` (off by default) drives `MainWindow.ApplyCurveCount`, which shows or hides the Curve 2 card, its chart, and the effective chart.

Switching to one curve sets `Curve2` to **Passthrough** rather than merely hiding it. A setting that is off screen but still shapes the output is exactly the bug removed from Basic, and the rule the whole pipeline is held to: what you cannot see is not applied. The old settings are parked in `_parkedCurve2` so the way back is lossless.

Three things follow the count: the effective chart disappears (with one curve it *is* curve 1, the same line drawn twice), the remaining card and chart lose their numbers (a number with nothing to distinguish it from is noise), and the Processing pill and order labels go singular — `S → C` rather than `S → C1 → C2`.

Loading a preset whose `Curve2` is not Passthrough switches the count to two. Otherwise half the preset would apply with nothing on screen to show it.

### Chart card sizing

Both chart controls draw a **square** plot sized off the available width, so a card's height follows its width: `plotSide = width - 2*Pad`, `height = plotSide + 2*Pad`, which comes back to the width whatever `Pad` is.

That has a consequence worth knowing before trying to make these cards shorter. Shrinking `Pad` (16 → 8) grows the plot inside the same box — it buys a bigger graph and a thinner margin, but not one pixel of height. The only lever on height is the width, which is why the three charts carry `MaxWidth="170"`: about 25px per card, 75px across the three, paid for with a little space either side of each plot.

The alternative — narrowing the chart column — would buy the same height with no side space, but the two columns are equal-width by construction and were made that way deliberately.

`Pad` itself is now only large enough to keep a node centred on the plot boundary from clipping: `NodeDrawRadius` is 6 and its outline adds ~0.75, so 8 is the floor.

### Ribbon collapse

`RibbonToggle_Click` folds the telemetry down to a 36px strip and back, hiding `RibbonBody` and the driver tip and leaving the gear reachable. It is for demonstrating the curve pipeline, where the telemetry is the least interesting band on screen and the tallest thing between the audience and the charts — 56px straight to the chart column.

Session state on purpose: it is a presenting mode, not a preference, so it does not go in `UiSettings`.

### `EffectiveCurveChartControl`
Draws the mapping the brush actually obeys — curve 1 with curve 2 applied over its output — sampled per pixel across [0, 1], plus the identity diagonal to read it against.

Deliberately **not** a mode of `PressureChartControl`. That control is an editor: hit-testing, drag dispatch, a bezier context menu, draggable range nodes. None of it applies to a composition, which cannot be dragged — there is no single answer to which of the two curves a moved point should change. Keeping them apart means the read-only chart carries no interaction code that has to be conditionally switched off, which is how a "read-only" mode ends up editable by accident.

### `StageStatus` (static)
Resolves each pipeline stage to a `StageState` — `Off` when the stage is set to Passthrough, `NoEffect` when it is running but its settings mean output equals input, `On` otherwise — which `MainWindow.UpdateCardStatuses` renders as the header pills.

The test is **structural, not numerical**. An earlier version sampled the mapping and compared it against `y = x` within a tolerance, which made the pill's claim depend on where you sampled and how tight the tolerance was. "No effect" now means exactly one checkable thing: *the settings are configured such that nothing changes*. The consequence is that a curve which happens to be indistinguishable from identity without being configured as identity reports `On` — the honest answer, since the stage is shaped, however slightly.

`Processing` is `On` only while both stages alter the signal, because that is the only time the order decides anything.

### `CurveDefaults` (static)
`ResetCurve` / `ResetSmoothing` back the two reset buttons. They restore the settings of the type the stage is **currently** set to, and never change the type — reaching Passthrough is the dropdown's job alone. Reset used to drag the type back to Passthrough with it, which left no way to undo a tweak without also leaving the curve you were working on.

Only the fields the current type actually uses are touched, because all six curve types share the one `PressureCurveParams`: a blanket reset would silently discard a Bezier you had shaped while you were sitting in Basic, where none of it is even on screen. Under Passthrough there is nothing on screen to restore, so the button is disabled rather than left as a silent no-op.

### `PresetStore`
Loads/saves the user's named curve presets from `%LOCALAPPDATA%\PenDynamicsLab\presets.json`. JSON via `System.Text.Json` with `JsonStringEnumConverter` so enums are readable in the file, and `PressureCurveParamsConverter` so presets saved before curve 2 existed still load.

That converter reads both shapes — the current one with `Curve1`/`Curve2`, and the legacy one with the curve's fields flat on the object — detecting by the presence of `Curve1` rather than a version number, which would have had to exist before it was needed. A legacy preset loads as **curve 1 with curve 2 at Passthrough**, which is the same mapping it always was: a single curve and a curve followed by a bypass are the same function. Writing is always the current shape, so opening a legacy preset and saving migrates it. Beyond `Save` / `Delete` / `Get` it offers `Rename` (in place, keeping list position) and `NextAvailableName`.

Saving takes a generated `Preset N` rather than prompting, so it stays one click; naming moves to Rename, which is when a name is worth thinking about — by then you know what the preset turned out to be. Each row carries a single `···` menu (Load / Rename / Delete) rather than a Load button beside a glyph button, which never lined up and had nowhere to put rename.

### `UiSettings`
A small persisted bag of preferences in `%LOCALAPPDATA%\PenDynamicsLab\ui-settings.json` — `DriverTipDismissed`, the `AppTheme`, and `UseTwoCurves`. Enums serialize by name, so inserting a theme into the middle of the enum cannot silently repoint everyone's saved preference. Deliberately separate from `PresetStore`: presets are user content they name and manage, these are preferences the app remembers on their behalf. Every read and write is best-effort, because a preference failing to persist must never stop the app.

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

`RenderChartPng`, behind the chart's export entries, has the same DIP-vs-pixel problem: a `RenderTargetBitmap` built from `Bounds` at 96 DPI produces an image at `1/scale` of the on-screen resolution. It sizes the target in physical pixels and tags it `96 * scale`.

That is the **opposite** of the canvas rule above, and deliberately so. `RenderTargetBitmap` is a render *target*, not a source bitmap: the tag tells Avalonia how to rasterize the visual into it, so scaling it up is exactly what's wanted. The canvas path passes its bitmap as a *source*, where the same tag instead changes how the source rectangle is derived. Same parameter, two different roles.

The stroke canvases need no equivalent handling for either save or copy — `DrawSurface.SavePng` encodes the `SKBitmap` directly, which is already at physical resolution.

Both paths end in `CopyPngToClipboardAsync` when copying. Avalonia has no set-image clipboard API, so the PNG bytes go on under the `"PNG"` format name; apps that read only `CF_DIB` may not see them.

## State flow

```
MainWindow._curveParams (PressureCurveParams)
   │
   ├──► PressureChart.Curve   = Curve1     (re-render on change)
   ├──► PressureChart2.Curve  = Curve2
   ├──► EffectiveChart.Params              (draws the pair composed)
   ├──► ResponseChart.Params
   ├──► Curve1Editor.Curve / Curve2Editor.Curve
   │
   ◄── CurveEditorView.CurveChanged        (its sliders, combos, radios, reset)
   ◄── PressureChartN writes Curve         (drag node / handle / context menu)
   ◄── quantization / smoothing / processing combos, preset load, ApplyCurveCount
        all via UpdateParams(p => p with { ... })
```

Every change funnels through `UpdateParams(Func<PressureCurveParams, PressureCurveParams>)`, which rebuilds the immutable record with `with { ... }` and then does three things, all of which have to happen on **every** change:

- `PushParamsToViews()` — hands the new parameters to both editors and all four charts.
- `UpdateDerivedControlState()` — control state derived from the parameters rather than set by the user, currently the smoothing amount slider's visibility and its reset button. Living only in `SyncCurveControlsFromParams` once meant switching smoothing to EMA left its slider hidden until an unrelated edit happened to trigger a sync.
- `UpdateCardStatuses()` — the header pills.

`SyncCurveControlsFromParams()` is for a **wholesale** change — preset load, curve count, startup. It pushes into the combos this window owns directly, then calls the same three.

That single owner is what keeps two curve editors and four charts from disagreeing: an editor never writes to its own `Curve` from its own handlers — it raises `CurveChanged` and `MainWindow` writes back.

> **Writing a chart's `Curve` raises its `PropertyChanged`, which `WireChart` reads as a user edit.** `PushParamsToViews` therefore guards the chart writes with `_suppressCurveControlEvents`, or the app's own push bounces straight back in as an edit. The **editors** are pushed outside that guard on purpose: switching to Sigmoid from a negative softness makes the editor clamp the value and emit the correction, and that correction has to reach back rather than be swallowed.

`CurveEditorView.UpdateVisibility()` runs whenever its `Curve` changes and drives per-curve-type control visibility, including whether that curve's reset button is enabled — a type with no settings of its own has nothing for a type-scoped reset to restore. It also clamps `Softness` into the active range — Sigmoid restricts the slider to `[0, 0.95]` (steepness is `softness * 14`, and the top of the range is numerically unstable), everything else uses `[-0.9, 0.9]`.

`MainWindow.SyncCurveControlsFromParams()` is the counterpart for what stayed global: the smoothing and processing controls, and pushing the current parameters out to both editors and all three charts.

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

`PressureCurveParams` (immutable record in `Curves/`) holds the pipeline; `CurveSettings` holds one curve, and the pipeline holds two of them:

| Field | Type | Purpose |
|---|---|---|
| `QuantizationLevels` | `int` | Pressure levels to coarsen the input to, or 0 for none. Always applied first |
| `Curve1` | `CurveSettings` | Shapes the pen's pressure |
| `Curve2` | `CurveSettings` | Shapes what curve 1 produced |
| `SmoothingType` | `SmoothingType` enum | Passthrough, Ema; Passthrough skips smoothing entirely |
| `EmaSmoothing` | `double` 0-0.99 | Pressure EMA smoothing amount (ignored when Passthrough) |
| `SmoothingOrder` | `SmoothingOrder` enum | Whether smoothing runs before or after **both** curves |

`CurveSettings`:

| Field | Type | Range | Purpose |
|---|---|---|---|
| `CurveType` | `CurveType` enum | Passthrough, Flat, Basic, Extended, Inverted, Sigmoid, Bezier | Curve algorithm. The declaration order is the dropdown order: the combo is filled by iterating the enum and selected by casting to `int`. |
| `Softness` | `double` | -0.9 to 0.9 (Sigmoid: 0 to 0.95) | Power exponent / sigmoid steepness |
| `InputMinimum` | `double` | 0-1 | Start of input pressure range (Extended / Sigmoid only) |
| `InputMaximum` | `double` | 0-1 | End of input pressure range (Extended / Sigmoid only) |
| `Minimum` | `double` | 0-1 | Start of output pressure range (Extended / Sigmoid only) |
| `Maximum` | `double` | 0-1 | End of output pressure range (Extended / Sigmoid only) |
| `MinApproach` | `MinApproach` enum | Clamp, Cut | Behavior below input minimum (Extended / Sigmoid only) |
| `FlatLevel` | `double` | 0-1 | Constant output for flat curve |
| `BezierPoints` | `ImmutableArray<BezierPoint>` | 2-16 points | Bezier control points |

`BezierPoint`: `(X, Y, InX, InY, OutX, OutY, HandleMode)` — anchor + in handle + out handle + Broken/Mirrored mode.

Every curve type shares this one record, so fields the active type does not use still hold whatever the last type that used them left behind. Two rules follow from that, and both are load-bearing: `CurveMath.UsesRangeControls` gates the five range fields at evaluation time, so Basic cannot silently apply values it never shows; and `CurveDefaults` scopes reset to the active type, so resetting one type cannot discard another's work.

`Default` has both curves and `SmoothingType` at `Passthrough`, so a fresh session applies nothing and what you draw is the pen's raw behaviour. Changing a default is not a local edit: a preset whose JSON predates a field takes that field's initializer here, so flipping one silently rewrites how already-saved presets behave.

> **`CurveSettings` is a record, so `==` looks like value equality — but `ImmutableArray<T>` compares by reference.** Two settings with identical bezier points, one of them just deserialized, are *not* equal. Compare the points with `SequenceEqual` when it matters.

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
2. **Pure math separation** — `Curves/CurveMath.cs` has no Avalonia or SkiaSharp dependencies. The xUnit project pins behavior with 167 tests against analytically-derived values.
3. **Avalonia DrawingContext for charts; SkiaSharp for canvases** — Charts are simple line geometry and benefit from Avalonia's text rendering + transform stack. The drawing canvases need many small antialiased strokes per frame, where SkiaSharp via `SKBitmap`/`WriteableBitmap` interop is faster.
4. **Single owner of state** — `MainWindow` holds the params and the surfaces; everything else is a leaf control receiving values via StyledProperties or queried for its current value. Even the chart's own edits round-trip through this owner.
5. **One surface, many views** — `DrawSurface` supports multiple `Image` hosts so the processed canvas is shared between tabs rather than copied. Sizing and hit-testing both key off `IsEffectivelyVisible` to avoid stale inactive-tab layout.
6. **DIPs in, pixels out** — callers draw entirely in device-independent units; `DrawSurface` alone knows the render scaling, allocating at physical resolution and carrying a matching canvas transform. Keeping that conversion in one class is what lets the pressure pipeline, brush sizing, and hit-testing all ignore DPI. See [HiDPI](#hidpi-dips-vs-physical-pixels).
7. **One ribbon, reparented** — rather than duplicating brush UI per tab and syncing it, a single `BrushRibbon` moves between tab slots.
8. **Stroke-local smoothing reset** — EMA state resets on every pen lift, canvas switch, and tab switch, so smoothing tails don't bleed across strokes or between canvases.
9. **One visual vocabulary, enforced centrally** — the type scale, control heights and radii live in `Window.Styles`, and the button fills in `App.axaml`'s theme-brush overrides. Nothing restyles a control template, so opting out stays a local property rather than a special case. See [Visual language](#visual-language).
