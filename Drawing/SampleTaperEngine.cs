using SkiaSharp;
using StrokeKit.Brushes;
using StrokeKit.Strokes;
using StrokeKit.Surfaces;

using KitBrush = StrokeKit.Brushes.Brush;
using KitStroke = StrokeKit.Strokes.Stroke;

namespace PenDynamicsLab.Drawing;

/// <summary>
/// Draws through StrokeKit's <see cref="Engine.SampleTaper"/> rather than laying the taper
/// here.
/// </summary>
/// <remarks>
/// <para>
/// <b>The kit renders; this application still decides.</b> Everything about what a mark should
/// be — quantization, smoothing, the two curves, which pressure channel is in play — happens
/// before a reading reaches here and is untouched by this. What the kit is asked for is the
/// shape, which is the part both implementations had.
/// </para>
/// <para>
/// The mapping is exact rather than approximate, because the kit's controls are linear when
/// told to be. A <see cref="Response"/> of <c>(0, 1, 1)</c> runs straight from one endpoint to
/// the other, so a <see cref="Width"/> from zero to the nib size over a range of 1024 answers
/// <c>size * pressure</c> — which is what <see cref="BrushSettings.StrokeWidthFor"/> answers.
/// The pipeline's output is handed over as a raw count against that range.
/// </para>
/// <para>
/// Two differences are known and accepted, both from <c>#78</c>:
/// </para>
/// <para>
/// <b>A dot at the start.</b> The kit lays one at a stroke's first reading and this application
/// never did. It sits inside the first taper's own cap, so on an opaque brush nothing shows;
/// on a translucent one the start is a shade firmer.
/// </para>
/// <para>
/// <b>The width floor.</b> <c>StrokeWidthFor</c> floors at
/// <see cref="BrushSettings.MinStrokeWidth"/> by taking a maximum; a linear width reaches the
/// same two endpoints but cannot floor in the middle. They differ below a pressure of about
/// 0.006, where the mark is a quarter of a pixel wide either way.
/// </para>
/// </remarks>
public sealed class SampleTaperEngine : IBrushEngine
{
    /// <summary>The range the pipeline's output is scaled onto before the kit sees it.</summary>
    /// <remarks>
    /// A number rather than the device's, and deliberately: what arrives here has already been
    /// through quantization, smoothing and two curves, so it is no longer the device's reading
    /// and giving it the device's range would say it was. 1024 is enough that rounding to a
    /// whole count costs less than a thousandth of the nib.
    /// </remarks>
    private const uint Range = 1024;

    /// <summary>One in-progress stroke per channel, because one gesture draws both.</summary>
    private readonly Laying?[] _laying = new Laying?[2];

    private sealed class Laying(ILive live, List<Reading> readings) : IDisposable
    {
        public ILive Live { get; } = live;

        public List<Reading> Readings { get; } = readings;

        public void Dispose() => Live.Dispose();
    }

    public void BeginStroke() => Finish();

    public void EndStroke() => Finish();

    public void DrawSegment(Surface surface, in StrokeSample from, in StrokeSample to,
        BrushSettings brush, SKColor colour, PressureChannel channel)
    {
        var at = (int)channel;
        var start = Taken(from, channel);
        var end = Taken(to, channel);

        // A stroke the kit is given has to begin with what it was given before. The session
        // skips a segment whose pressure is zero, so the next one it asks for may not continue
        // the last -- and that is a new stroke as far as the kit is concerned, not a gap in
        // this one.
        if (_laying[at] is { } laying && !Continues(laying.Readings, start))
        {
            laying.Dispose();
            _laying[at] = null;
        }

        if (_laying[at] is null)
        {
            // Identity, and this is load bearing. A surface handed to an engine here already
            // carries the transform from this application's DIPs to its pixels on its canvas,
            // so a transform here as well would scale every mark twice. The kit's own callers
            // do it the other way -- a plain canvas and an InkTransform -- and both are
            // consistent; what is not allowed is one of each.
            var live = Live.For(Kit(brush, colour), surface, new InkTransform(1, 1, 0, 0));

            _laying[at] = new Laying(live, [start]);
        }

        _laying[at]!.Readings.Add(end);
        _laying[at]!.Live.Extend(new KitStroke(_laying[at]!.Readings));
    }

    /// <summary>Whether this segment carries on from where the last one left off.</summary>
    private static bool Continues(List<Reading> sofar, Reading start) =>
        sofar.Count > 0
        && sofar[^1].X == start.X
        && sofar[^1].Y == start.Y
        && sofar[^1].Pressure == start.Pressure;

    /// <summary>A sample as the kit's reading, carrying the pipeline's output as its pressure.</summary>
    private static Reading Taken(in StrokeSample sample, PressureChannel channel) =>
        new(sample.Position.X, sample.Position.Y,
            (uint)Math.Clamp(Math.Round(sample.PressureFor(channel) * Range), 0, Range));

    /// <summary>
    /// This application's brush, said in the kit's terms.
    /// </summary>
    /// <remarks>
    /// The two controls are exclusive here exactly as they are in <see cref="BrushSettings"/>:
    /// pressure drives the width or the ink and never both, so whichever it does not drive is
    /// given no curve at all rather than a flat one. A null control is the kit's way of saying
    /// "this does not vary", which is the same thing said once instead of twice.
    /// </remarks>
    private static KitBrush Kit(BrushSettings brush, SKColor colour) =>
        brush.PressureDrives == PressureControl.Opacity
            ? new KitBrush(
                brush.Size, colour, 1,
                Buildup: Buildup.PerStamp,
                Width: null,
                SpacedBy: SpacedBy.Distance,
                Flow: new Flow(0, 1, Range, new Response(0, 1, 1)),
                Engine: Engine.SampleTaper)
            : new KitBrush(
                brush.Size, colour, 1,
                Buildup: Buildup.PerStamp,
                Width: new Width(0, brush.Size, Range, new Response(0, 1, 1)),
                SpacedBy: SpacedBy.Distance,
                Flow: null,
                Engine: Engine.SampleTaper);

    private void Finish()
    {
        for (var each = 0; each < _laying.Length; each++)
        {
            _laying[each]?.Live.Finish();
            _laying[each]?.Dispose();
            _laying[each] = null;
        }
    }

    public void Dispose() => Finish();
}
