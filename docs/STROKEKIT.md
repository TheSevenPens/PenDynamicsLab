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

## Done: the batch clock

**A correction first.** An earlier draft of this file said this application had no host clock.
That was wrong. `StrokeRecorder` has always written two columns — `T` from
`DateTime.UtcNow` and `PenTimeUs` from the pen — and its own remarks say the two existing
to be compared against each other is the only reason to keep both.

A second guess was also wrong: that `DateTime.UtcNow` would be too coarse. Measured on this
machine, both it and `Stopwatch` report a median step of **1 microsecond**. Resolution was
never the difference.

What was actually wrong is narrower and worse. `Add` read the wall clock **once per sample,
from inside the loop over a drained batch**. Packets the driver handed over in a single
`DrainPoints()` call were therefore stamped microseconds apart — and what separated them was
how long this application took to convert coordinates and draw the preceding ones. The column
conflated when the pen reported with what the program was doing at the time, in an instrument
whose subject is timing.

The tick now drains through `Draining`, which stamps the batch on the line after taking it and
before anything here runs. `Add` is given that arrival rather than reading a clock, so every
sample from one poll carries the same `T` exactly. It is also monotonic, which a wall clock is
not — that, rather than resolution, is the second reason to prefer it.

`One_batch_is_one_tick_time` pins it, and was watched failing against the old behaviour before
being trusted. An existing test, `Both_columns_open_at_zero_on_the_first_sample`, fails against
it too: it used to pass because two `UtcNow` reads a line apart are nearly equal, and now
passes because the first sample's `T` is exactly zero.

**One thing this turned up for later.** `StrokeKit.Strokes` has a `Stroke` and so does
`PenDynamicsLab.Drawing`, and they are different things. `Draining` is aliased rather than the
namespace imported, to avoid deciding which one this application means. Deciding that is part
of adopting the kit properly.

## Where the kit is pinned

`vendor/StrokeKit` at whatever commit this branch records. The kit is not published as a
package: its API is still moving, and a package boundary on a moving API is a cost with no
benefit. Moving the pin is a commit here.
