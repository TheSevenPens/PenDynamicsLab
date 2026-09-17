# Standing on StrokeKit

This branch wires PenDynamicsLab to
[StrokeKit](https://github.com/TheSevenPens/StrokeKit) and clears the obstacles to using it.
**It does not use it yet.** Nothing in the application calls a StrokeKit type; the reference is
there, the build is clean, and all 260 tests pass exactly as they did before.

That is the point of stopping here. The wiring turned up three things worth knowing before
anyone spends an afternoon on the migration itself.

## What the wiring cost

### Every default glob reaches into the submodule

`PenDynamicsLab.csproj` sits at the repository root, so the SDK's and Avalonia's default item
globs sweep everything beneath it — including `vendor/`. StrokeKit brings WinPenKit nested
inside itself, and WinPenKit ships sample applications, so the first build after adding the
submodule tried to compile a WinUI app and resolve an Avalonia one's XAML as part of this
program.

Fixed with `Remove` items for `vendor\**` across `Compile`, `AvaloniaResource`,
`AvaloniaXaml`, `EmbeddedResource`, `None`, `Page` and `ApplicationDefinition`.

StrokeFieldGuide never hit this because its projects live in subdirectories. **A flat
repository layout and submodules of source do not mix without saying so explicitly.**

### Two WinPenKit checkouts in one build graph

This project referenced `..\WinPenKit` as a sibling. StrokeKit references its own nested copy.
Adding StrokeKit put both in the graph: both were compiled, and one won the race to be copied
into `bin/`. The evidence was a `WinPenKit.dll` in the output dated a day earlier than the
build that had just produced a fresh one two submodules down.

Nothing failed. It would have failed later, confusingly, as a type from one assembly refusing
to be a type from the other.

So the direct references are gone. WinPenKit arrives transitively through StrokeKit, both
halves, at the commit StrokeKit pins — which is also the same arrangement StrokeFieldGuide
settled on.

**This is the one change with a consequence for how you work.** Editing the sibling
`..\WinPenKit` working tree no longer affects a build on this branch. To try a WinPenKit
change here, edit `vendor/StrokeKit/vendor/WinPenKit` or move the kit's pin.

### The clone has to be recursive

`git clone --recurse-submodules`, or `git submodule update --init --recursive` on an existing
one. Two levels, because WinPenKit is a submodule of StrokeKit.

## What is worth taking, and what is not

StrokeKit is a drawing kit. This application is about *dynamics* — what happens to pressure
between the sensor and the mark — and that part has no counterpart in the kit and should not
acquire one.

### Worth taking

| here | there | note |
|---|---|---|
| `Drawing/DrawSurface.cs` | `Surface`, `SurfaceView` | The same job: an SKBitmap and SKCanvas with a WriteableBitmap mirror, and the DIP-to-physical bridging. Compare the DPI handling carefully before swapping; both are opinionated and they may not agree. |
| `Drawing/Stroke.cs` — `StrokeSample`, `PenOrientation` | `Reading` | `Reading` keeps a lean and an azimuth where this keeps tilt x and tilt y. Not a rename; a conversion. |
| the `RenderTimer_Tick` drain | `PenStream`, `Draining` | See below. This is the one with something to gain rather than just something to share. |
| `Drawing/IBrushEngine.cs` | `Engine`, `Stamp`, `Taper`, `Brush` | Overlapping rather than equivalent. Needs judgement, not a swap. |

### Not worth taking

`Curves/` — eleven files, `CurveMath`, `BezierPresets`, `DynamicsPipeline`,
`PressureCurveParams`, `Quantization` — has no counterpart. StrokeKit has `Response`, which is
a single curve on a brush control, not an editable preset-backed pipeline.

Nor do the controls: `CurveEditorView`, the three chart controls, `BrushRibbon`, the
raw-versus-processed comparison. Nor `PressureChannel` — one gesture drawing two surfaces
through one engine — which the kit has no notion of at all.

## The one real gain

**This application has no host clock.** It records `PenPoint.TimestampMicroseconds` and nothing
else (`Diagnostics/StrokeRecorder.cs`), so every interval it reports is on the pen's own clock.

On the hardware StrokeFieldGuide measured, that clock is a **packet counter rather than a
clock**: it advances a flat 4.166 ms per packet delivered, runs at 0.673 of real time, and
resynchronises at every contact transition. A single clock cannot tell "the device stopped
sending" from "the device stamped late", and five explanations for a gap in recorded data died
on that before a second clock existed.

`Draining` stamps each drained batch on a monotonic host clock, and hands over the packets and
the converted readings together. Adopting it would give this application the distinction it
currently cannot make — which matters more here than anywhere, because this is the one that
exists to measure dynamics.

It is also the smallest self-contained migration of the four, which makes it the obvious first
one.

## Where the kit is pinned

`vendor/StrokeKit` at whatever commit this branch records. The kit is not published as a
package: its API is still moving, and a package boundary on a moving API is a cost with no
benefit. Moving the pin is a commit here.
