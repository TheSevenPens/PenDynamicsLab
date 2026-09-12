using Avalonia;
using Avalonia.Controls;
using SkiaSharp;

namespace PenDynamicsLab.Drawing;

/// <summary>Which of the two surfaces a canvas shows.</summary>
public enum CanvasRole
{
    /// <summary>The pipeline's output — the mark the configured brush actually makes.</summary>
    Processed,

    /// <summary>Unprocessed pen pressure, for comparison.</summary>
    Raw,
}

/// <summary>One on-screen canvas: the border that defines its bounds, and the image that shows it.</summary>
/// <param name="Host">Used for hit-testing and sizing. Its <c>Bounds</c> are DIPs.</param>
/// <param name="Image">Registered with the surface for this role.</param>
public readonly record struct CanvasTarget(Border Host, Image Image, CanvasRole Role);

/// <summary>
/// Owns the stroke surfaces and puts marks on them.
/// </summary>
/// <remarks>
/// <para>
/// The window used to do all of this inside its 16 ms timer: sizing the bitmaps, hit-testing the
/// visible tab, picking the stroke colour, constructing an <c>SKPaint</c>, and presenting. It now
/// drains pen points, runs telemetry and the charts, and hands positions plus pipeline output
/// plus <see cref="BrushSettings"/> to this type.
/// </para>
/// <para>
/// <b>The processed surface is one bitmap with two hosts</b> — the Stroke tab and the Compare
/// tab's upper canvas are the same content, so drawing on either is drawing on both. They are
/// different sizes, which is safe because <c>DrawSurface</c> never shrinks and the hosts clip an
/// oversized bitmap.
/// </para>
/// <para>
/// Deliberately no layers, tools, or undo. Those need a stroke model on top of this — see #15.
/// </para>
/// </remarks>
public sealed class DrawingSession : IDisposable
{
    private static readonly SKColor BlackStroke = new(0x1A, 0x1A, 0x2E);
    private static readonly SKColor RedStroke = new(0xC4, 0x1E, 0x3A);

    private static readonly SKColor[] Palette =
    [
        new(0xE6, 0x19, 0x4B), new(0x3C, 0xB4, 0x4B), new(0x43, 0x63, 0xD8), new(0xF5, 0x82, 0x31),
        new(0x91, 0x1E, 0xB4), new(0x42, 0xD4, 0xF4), new(0xF0, 0x32, 0xE6), new(0xBF, 0xEF, 0x45),
        new(0xFA, 0xBE, 0xD4), new(0x46, 0x99, 0x90), new(0xDC, 0xBE, 0xFF), new(0x9A, 0x63, 0x24),
        new(0x80, 0x00, 0x00), new(0xAA, 0xFF, 0xC3), new(0x80, 0x80, 0x00), new(0x00, 0x00, 0x75),
    ];

    private readonly CanvasTarget[] _targets;
    private readonly IBrushEngine _engine;

    // Per-stroke state. Not BrushSettings' business: the resolved colour changes mid-gesture,
    // and in Random mode it is not recoverable from the settings afterwards.
    private readonly Random _rng = new();
    private SKColor _strokeColor = BlackStroke;
    private int _lastColorIndex = -1;

    private CanvasRole? _active;
    private Point? _lastDrawPos;

    // The pressures the previous sample was drawn at. A segment needs the width at both of its
    // ends to taper, and only one of them belongs to the sample now arriving.
    private double _lastRawPressure, _lastProcessedPressure;
    private bool _processedDirty, _rawDirty;

    /// <summary>
    /// What has been drawn, as strokes rather than only as pixels. Recording is immediate and the
    /// mark is still rasterized as it arrives — this is a parallel record, not a replacement for
    /// drawing. See #15.
    /// </summary>
    public StrokeHistory History { get; } = new();

    // Everything evicted from the history, already rendered. Undo restores these before replaying
    // the strokes that are still recorded, so capping the history cannot erase work from the
    // canvas. Allocated only once the cap is actually reached - a session that never fills the
    // history never pays for them.
    private SKBitmap? _processedBaseline, _rawBaseline;

    /// <summary>The processed surface. Exposed for PNG export, not for drawing.</summary>
    public DrawSurface Processed { get; } = new();

    /// <summary>The raw surface. Exposed for PNG export, not for drawing.</summary>
    public DrawSurface Raw { get; } = new();

    public DrawingSession(IEnumerable<CanvasTarget> targets, IBrushEngine? engine = null)
    {
        _targets = [.. targets];
        _engine = engine ?? new RoundBrushEngine();

        foreach (var t in _targets)
            SurfaceFor(t.Role).AddHost(t.Image);
    }

    private DrawSurface SurfaceFor(CanvasRole role) => role == CanvasRole.Raw ? Raw : Processed;

    /// <summary>
    /// Size each surface to the visible host for its role.
    /// </summary>
    /// <remarks>
    /// <c>IsEffectivelyVisible</c> rather than a bounds check: inactive tab content keeps stale
    /// layout, and picking a host from a tab that is not showing produces sizes — and, in
    /// <see cref="ResolveActiveCanvas"/>, coordinates — from the wrong place.
    /// </remarks>
    public void EnsureSurfaces(double renderScaling)
    {
        foreach (var role in (ReadOnlySpan<CanvasRole>)[CanvasRole.Processed, CanvasRole.Raw])
        {
            foreach (var t in _targets)
            {
                if (t.Role != role || !t.Host.IsEffectivelyVisible) continue;
                if (t.Host.Bounds.Width <= 0 || t.Host.Bounds.Height <= 0) continue;

                SurfaceFor(role).EnsureSize(t.Host.Bounds.Width, t.Host.Bounds.Height, renderScaling);
                break;
            }
        }
    }

    /// <summary>
    /// Which canvas the pen is over, and where in that canvas, or null if it is over neither.
    /// </summary>
    public CanvasRole? ResolveActiveCanvas(TopLevel topLevel, Point clientPt, out Point local)
    {
        foreach (var t in _targets)
        {
            if (!t.Host.IsEffectivelyVisible) continue;
            if (HitTest(t.Host) is not { } p) continue;
            local = p;
            return t.Role;
        }

        local = default;
        return null;

        Point? HitTest(Border host)
        {
            if (host.Bounds.Width <= 0 || host.Bounds.Height <= 0) return null;
            var origin = host.TranslatePoint(new Point(0, 0), topLevel);
            if (origin is null) return null;
            var p = new Point(clientPt.X - origin.Value.X, clientPt.Y - origin.Value.Y);
            if (p.X < 0 || p.X >= host.Bounds.Width || p.Y < 0 || p.Y >= host.Bounds.Height) return null;
            return p;
        }
    }

    /// <summary>
    /// Record which canvas the pen is over, returning true if that is a change.
    /// </summary>
    /// <remarks>
    /// The caller resets the pipeline's filter on a true, <b>before</b> processing the sample, so
    /// the first point on the new canvas is genuinely unweighted by the old one. Crossing to
    /// "over nothing" is not a change — the pen hovering off the canvas should not end a stroke
    /// that a moment later continues on the same one.
    /// </remarks>
    public bool NoteCanvas(CanvasRole? over)
    {
        if (over is not { } role || role == _active) return false;
        _active = role;
        _lastDrawPos = null;
        return true;
    }

    /// <summary>Add one pen sample to the stroke in progress, starting one if needed.</summary>
    /// <param name="rawPressure">Pressure before the pipeline, which drives the raw surface.</param>
    /// <param name="processedPressure">The pipeline's output, which drives the processed surface.</param>
    public void AddSample(Point pos, double rawPressure, double processedPressure, BrushSettings brush, PenOrientation orientation = default)
    {
        if (rawPressure <= 0)
        {
            EndStroke();
            return;
        }

        // A new stroke begins whenever pressure arrives with no segment in progress: at pen-down,
        // and again after the pen crosses to the other canvas. Keying off the canvas change alone
        // missed the common case — hovering over the canvas consumes the change at zero pressure,
        // so pressing down afterwards never picked a colour and every stroke stayed the initial
        // black.
        if (_lastDrawPos is null)
        {
            PickStrokeColor(brush.ColorMode);
            History.BeginStroke(brush, _strokeColor);

            // No predecessor to ramp from. Seeding with this sample's own pressure makes the
            // first segment start at the width it ends at, rather than opening from the width
            // the previous stroke happened to finish on.
            _lastRawPressure = rawPressure;
            _lastProcessedPressure = processedPressure;
        }

        History.AddSample(pos, rawPressure, orientation, processedPressure);

        if (_lastDrawPos is { } from)
        {
            // Both surfaces are drawn on every segment. The processed one takes the pipeline
            // output; the raw one takes unprocessed pressure, which is the comparison.
            if (Processed.Canvas is { } pc && (brush.DrawAtZeroPressure || processedPressure > 0))
            {
                _engine.DrawSegment(pc, from, pos, _strokeColor,
                    brush.StrokeWidthFor(_lastProcessedPressure), brush.StrokeWidthFor(processedPressure),
                    brush.OpacityFor(processedPressure));
                _processedDirty = true;
            }
            if (Raw.Canvas is { } rc)
            {
                _engine.DrawSegment(rc, from, pos, _strokeColor,
                    brush.StrokeWidthFor(_lastRawPressure), brush.StrokeWidthFor(rawPressure),
                    brush.OpacityFor(rawPressure));
                _rawDirty = true;
            }
        }

        _lastDrawPos = pos;
        _lastRawPressure = rawPressure;
        _lastProcessedPressure = processedPressure;
    }

    /// <summary>End the segment in progress without clearing anything that was drawn.</summary>
    public void EndStroke()
    {
        _lastDrawPos = null;
        History.EndStroke();

        // Bake anything the cap pushed out into the baseline before losing the samples.
        while (History.EvictOldestIfOverCap() is { } evicted) BakeIntoBaseline(evicted);
    }

    /// <summary>Render an evicted stroke into the baseline so undo can still restore it.</summary>
    private void BakeIntoBaseline(Stroke stroke)
    {
        Bake(Processed, ref _processedBaseline, s => s.ProcessedPressure);
        Bake(Raw, ref _rawBaseline, s => s.RawPressure);

        void Bake(DrawSurface surface, ref SKBitmap? baseline, Func<StrokeSample, double> pressure)
        {
            if (surface.Width <= 0 || surface.Height <= 0) return;

            // The surface only ever grows, so a baseline from an earlier size is still valid at
            // the origin - copy it into a larger one rather than starting over.
            if (baseline is null || baseline.Width < surface.Width || baseline.Height < surface.Height)
            {
                var grown = new SKBitmap(surface.Width, surface.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                if (baseline is not null)
                {
                    using var copy = new SKCanvas(grown);
                    copy.DrawBitmap(baseline, 0, 0);
                    baseline.Dispose();
                }
                baseline = grown;
            }

            using var canvas = new SKCanvas(baseline);
            // Samples are in DIPs; the baseline is physical pixels, same as the surface it mirrors.
            canvas.Scale((float)surface.Scale);

            var samples = stroke.Samples;
            for (int i = 1; i < samples.Count; i++)
            {
                double p = pressure(samples[i]);
                if (!stroke.Brush.DrawAtZeroPressure && p <= 0) continue;
                _engine.DrawSegment(canvas, samples[i - 1].Position, samples[i].Position, stroke.Color,
                    stroke.Brush.StrokeWidthFor(pressure(samples[i - 1])),
                    stroke.Brush.StrokeWidthFor(p),
                    stroke.Brush.OpacityFor(p));
            }
        }
    }

    /// <summary>Push whichever surfaces were drawn on to their hosts.</summary>
    public void PresentDirty()
    {
        if (_processedDirty) { Processed.Present(); _processedDirty = false; }
        if (_rawDirty) { Raw.Present(); _rawDirty = false; }
    }

    /// <summary>
    /// Wipe both surfaces. They are two views of one stroke, so clearing only the half that was
    /// right-clicked would leave the comparison mismatched.
    /// </summary>
    public void Clear()
    {
        Processed.Clear();
        Raw.Clear();
        History.Clear();
        _processedBaseline?.Dispose(); _processedBaseline = null;
        _rawBaseline?.Dispose(); _rawBaseline = null;
        ResetStroke();
    }

    /// <summary>Forget the stroke in progress and which canvas it was on.</summary>
    /// <remarks>
    /// The session's half of what <c>ResetStrokeState</c> used to do alone. The pipeline owns the
    /// filter state and the window owns the charts' live indicators.
    /// </remarks>
    public void ResetStroke()
    {
        _active = null;
        _lastDrawPos = null;
        History.EndStroke();
    }

    /// <summary>
    /// Remove the most recent stroke and redraw what is left. Returns false if there was nothing
    /// to undo.
    /// </summary>
    /// <param name="recompute">
    /// Re-runs a stroke's raw pressures through the current pipeline, for strokes recorded under
    /// an older parameter generation. Supplied by the window, which owns the pipeline. When null,
    /// stale strokes are replayed from their cached outputs — visibly the old curve, which is
    /// wrong but better than not drawing them.
    /// </param>
    /// <remarks>
    /// Undo clears both surfaces and replays every remaining stroke: O(strokes) per undo. That is
    /// fine at lab scale and deliberately not optimised into a damage-rect scheme — see #15.
    /// </remarks>
    public bool UndoLastStroke(Func<Stroke, IReadOnlyList<double>>? recompute = null)
    {
        EndStroke();
        if (!History.RemoveLast()) return false;

        Processed.Clear();
        Raw.Clear();
        // Evicted strokes first: they are no longer replayable, but they are still on screen and
        // must stay there. Without this the cap would quietly delete work on the next undo.
        if (_processedBaseline is not null) Processed.DrawSnapshot(_processedBaseline);
        if (_rawBaseline is not null) Raw.DrawSnapshot(_rawBaseline);
        foreach (var stroke in History.Strokes) Replay(stroke, recompute);

        _processedDirty = _rawDirty = true;
        PresentDirty();
        return true;
    }

    private void Replay(Stroke stroke, Func<Stroke, IReadOnlyList<double>>? recompute)
    {
        // A stroke drawn under older params has a stale cache. Re-running it from its raw
        // pressures is what keeps the canvas from showing two curve generations at once.
        if (stroke.ParamsVersion != History.ParamsVersion && recompute is not null)
            stroke.RecacheOutputs(recompute(stroke), History.ParamsVersion);

        var samples = stroke.Samples;
        for (int i = 1; i < samples.Count; i++)
        {
            var from = samples[i - 1].Position;
            var to = samples[i].Position;
            var s = samples[i];
            var prev = samples[i - 1];

            if (Processed.Canvas is { } pc && (stroke.Brush.DrawAtZeroPressure || s.ProcessedPressure > 0))
            {
                _engine.DrawSegment(pc, from, to, stroke.Color,
                    stroke.Brush.StrokeWidthFor(prev.ProcessedPressure),
                    stroke.Brush.StrokeWidthFor(s.ProcessedPressure),
                    stroke.Brush.OpacityFor(s.ProcessedPressure));
            }
            if (Raw.Canvas is { } rc)
            {
                _engine.DrawSegment(rc, from, to, stroke.Color,
                    stroke.Brush.StrokeWidthFor(prev.RawPressure),
                    stroke.Brush.StrokeWidthFor(s.RawPressure),
                    stroke.Brush.OpacityFor(s.RawPressure));
            }
        }
    }

    private void PickStrokeColor(ColorMode mode)
    {
        if (mode == ColorMode.Black) { _strokeColor = BlackStroke; return; }
        if (mode == ColorMode.Red) { _strokeColor = RedStroke; return; }

        int idx;
        do { idx = _rng.Next(Palette.Length); } while (idx == _lastColorIndex && Palette.Length > 1);
        _lastColorIndex = idx;
        _strokeColor = Palette[idx];
    }

    public void Dispose()
    {
        _engine.Dispose();
        Processed.Dispose();
        Raw.Dispose();
        _processedBaseline?.Dispose();
        _rawBaseline?.Dispose();
    }
}
