# Futures

Ideas, known issues, and potential directions for PenDynamicsLab.

## Known issues

- **Response JSON format drift** — The upload-JSON code path is wired but the upstream pen-measurement tooling has changed format since the bundled WACOM samples were captured. `PressureResponseLoader.RawJson` will likely need to be revised against fresh data; the embedded samples may need refreshing too.
- **`Tmds.DBus.Protocol` security advisory** — Transitive Avalonia dependency flagged with NU1903. Awaiting an Avalonia upstream bump.

## Feature suggestions (carryover from WebPressureExplorer)

- **Undo/redo** — Track param changes and allow stepping back through history, especially useful during bezier editing.
- **Bezier import/export** — Copy/paste bezier point data as JSON for sharing or backup.
- **Pressure response overlay on the main chart** — Show the response data curve overlaid directly on the pressure curve chart (currently they're separate plots).
- **Multiple response datasets** — Load and compare several pens' response data side by side.
- **Application-specific profiles** — Model pressure curves used by Photoshop, Clip Studio Paint, Krita, etc., to help users understand how their app's built-in curve interacts with the tablet driver.
- **Curve comparison mode** — Two curves overlaid for visual A/B comparison of settings.
- **Pressure recording and playback** — Record a stroke's pressure data over time and replay it through different curve settings to compare feel without re-drawing.
- **Expanded hardware data** — Larger library of bundled pen response datasets covering more brands and models.
- **Chart format toggles** — Show/hide grid, axis labels, control nodes, node guides, raw indicator, effective indicator (the web app has these in `PressureChartFormat.svelte`).

## Directions specific to PenDynamicsLab

The desktop port unlocks input the web app can't reach. The "Pen Dynamics" in the project name signals that strokes will eventually use more than just pressure.

- **Tilt-driven brush shape** — Use azimuth + altitude to make stroke ends elliptical or directional, mimicking a chisel or flat brush.
- **Velocity / acceleration modulation** — Map stroke speed to width, opacity, or texture density.
- **Twist support** — Use the pen's barrel rotation to drive an angle parameter (for square brushes, calligraphy nibs).
- **Stamp-based stroke rendering** — Replace `DrawLine` with stamp interpolation (constant-spacing dabs) so brush textures and per-stamp pressure work properly.
- **Bitmap brush textures** — Image-based brush tips loaded from disk.
- **Multi-channel curve routing** — Each pen channel (pressure, tilt, velocity, twist) gets its own pressure-curve-style mapping into a separate stroke parameter, all configurable in the same UI.
- **Stroke smoothing comparison** — Side-by-side EMA vs Catmull-Rom vs predictive smoothing on the same input stream.
- **Replay from session capture** — Capture a pen stream to disk, replay it through different settings (related to web-side "pressure recording and playback").

## Open questions

- **Smoothing between the curves** — Processing offers smooth-then-curves and curves-then-smooth. With a pair, `C1 → S → C2` is also meaningful and is not offered; it would make the order a three-way choice rather than a toggle.

## Technical improvements

- **Test the bezier solver edge cases** — The suite is 167 tests, and `EvaluateCustomCurve` is currently exercised mostly on the linear preset and endpoints. Add tests for very-narrow segments (`span ≤ 1e-6` early return) and Step-preset shapes.
- **Decompose `PressureChartControl`** — At ~825 lines, hit-testing, drag dispatch, and the bezier context menu could split out into a separate interaction layer.
- **Tab-aware surface sizing is subtle** — Both `EnsureSurfaces` and `ResolveActiveCanvas` depend on `IsEffectivelyVisible` to avoid stale inactive-tab layout, and the raw surface only gets allocated once the compare tab has been shown. A small abstraction owning "the surfaces of the active tab" would make this less easy to break.
- **Avalonia source generator quirk** — `Controls/LabeledSlider.axaml.cs` discovered the hard way that defining your own `InitializeComponent()` shadows the generator's, leaving named fields null. Worth a comment-block or a doc note for future controls.
