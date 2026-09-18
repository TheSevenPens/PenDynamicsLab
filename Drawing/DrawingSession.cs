using Avalonia;
using Avalonia.Controls;
using SkiaSharp;

// Aliased rather than imported whole: StrokeKit.Strokes has a Stroke and so does this
// namespace, and they are different things.
using Surface = StrokeKit.Surfaces.Surface;
using SurfaceView = StrokeKit.Avalonia.SurfaceView;

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
public readonly record struct CanvasTarget(Border Host, SurfaceView View, CanvasRole Role);

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
    /// <summary>
    /// The previous sample of the segment in progress, and the flag for whether there is one.
    /// Null between strokes and after the pen crosses to the other canvas.
    /// </summary>
    /// <remarks>
    /// This used to be the previous position alone, with the two pressures beside it in their own
    /// fields. Keeping the whole sample means the engine can be handed both endpoints, which is
    /// what lets it decide the mark for itself rather than being told a width.
    /// </remarks>
    private StrokeSample? _lastSample;

    // The pressures the previous sample was drawn at. A segment needs the width at both of its
    // ends to taper, and only one of them belongs to the sample now arriving.
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
    /// <summary>
    /// The processed document, shown by every view whose role is Processed.
    /// </summary>
    /// <remarks>
    /// Replaced rather than resized, because a <see cref="Surface"/> has a fixed size. Growth
    /// keeps the old content at the origin; see <see cref="EnsureSurfaces"/>.
    /// </remarks>
    public Surface? Processed { get; private set; }

    /// <summary>The raw surface. Exposed for PNG export, not for drawing.</summary>
    /// <summary>The raw document, shown by every view whose role is Raw.</summary>
    public Surface? Raw { get; private set; }

    public DrawingSession(IEnumerable<CanvasTarget> targets, IBrushEngine? engine = null)
    {
        _targets = [.. targets];

        // StrokeKit's taper by default, rather than this application's own. The two lay the
        // same shape -- checked in TwoTapersCompared, which is what found that this one's
        // sides were turned the wrong way -- so what changes is that there is one
        // implementation of it instead of two, and it is the kit's.
        //
        // RoundBrushEngine stays, and stays tested. It is the thing the kit's is compared
        // against, and a comparison needs both halves.
        _engine = engine ?? new SampleTaperEngine();
    }

    private Surface? SurfaceFor(CanvasRole role) => role == CanvasRole.Raw ? Raw : Processed;

    /// <summary>The paper a cleared canvas shows.</summary>
    /// <remarks>
    /// Held here rather than by the surface. A StrokeKit surface is a raster and has no opinion
    /// about what empty looks like, which is right: the guide's own pads clear to transparent
    /// and this bench clears to paper, and neither is the kit's business.
    /// </remarks>
    private static readonly SKColor Paper = new(0xF7, 0xF7, 0xF4);

    private static void Wipe(Surface? art) => art?.Canvas.Clear(Paper);

    /// <summary>
    /// Puts a surface's canvas into this application's units.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A StrokeKit surface draws in pixels.</b> Its logical size is metadata for the view
    /// that presents it, not a transform on its canvas -- which is right for a kit that has
    /// consumers in both units, and is the opposite of what DrawSurface did. Everything here
    /// is in DIPs, from the samples to the brush engine, so the transform has to be applied
    /// once per surface, here, where the convention is.
    /// </para>
    /// <para>
    /// Missing it does not fail: it draws everything at 1/scale of the distance from the
    /// origin, so a mark is correct in the top-left corner and drifts further from the pen the
    /// further out it goes. That is what it did.
    /// </para>
    /// </remarks>
    private static void InDips(Surface art, double scale) => art.Canvas.Scale((float)scale);

    /// <summary>Hands a role's surface to every view that shows it.</summary>
    /// <remarks>
    /// Two views can show one surface: the Stroke tab and the Compare tab are both Processed,
    /// and each sizes its own presentation bitmap to its own viewport. The surface is read
    /// during a frame and never written by a view, so sharing one is safe.
    /// </remarks>
    private void Showing(CanvasRole role, Surface art)
    {
        foreach (var t in _targets)
        {
            if (t.Role == role) t.View.Show(art);
        }
    }

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

                EnsureAtLeast(role, t.Host.Bounds.Width, t.Host.Bounds.Height, renderScaling);
                break;
            }
        }
    }

    /// <summary>
    /// Makes a role's surface at least large enough for the host asking, keeping what is on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never smaller.</b> Growth copies the old content in at the origin, so allocating
    /// smaller would discard whatever fell outside -- permanently, since growing back cannot
    /// recover it. Shrinking the window and restoring it used to truncate the mark at the
    /// smaller height, and a resize drag does that on every step. <see cref="Surface.Grown"/>
    /// takes the maximum of each dimension for exactly that reason.
    /// </para>
    /// <para>
    /// Growth is monotonic within a session and bounded by screen size, which is a stated
    /// consequence rather than a leak.
    /// </para>
    /// </remarks>
    public void EnsureAtLeast(CanvasRole role, double dipWidth, double dipHeight, double scale)
    {
        if (dipWidth <= 0 || dipHeight <= 0) return;
        if (scale <= 0 || double.IsNaN(scale)) scale = 1;

        var wide = (int)Math.Round(dipWidth * scale);
        var high = (int)Math.Round(dipHeight * scale);

        if (wide <= 0 || high <= 0) return;

        var art = SurfaceFor(role);
        var was = ScaleOf(role);

        // A change of scale reallocates whatever the pixel count says, and it may say fewer.
        // The surface's canvas carries the transform that puts this application's DIPs into
        // its pixels, so a surface made at one scale and presented at another draws every mark
        // short or long by the ratio -- correct at the origin and drifting further out the
        // further it goes. Moving the window to a display with different scaling does it.
        if (art is not null && scale != was)
        {
            var moved = Surface.CreateExactly(wide, high, wide / scale, high / scale);

            InDips(moved, scale);

            // The old content at the size it looked, not the pixels it occupied: it was drawn
            // in DIPs and should stay where those DIPs are.
            using (var image = art.Snapshot())
            {
                moved.Canvas.Save();
                moved.Canvas.ResetMatrix();
                moved.Canvas.DrawImage(image,
                    new SKRect(0, 0, (float)(art.PixelWidth * scale / was),
                                     (float)(art.PixelHeight * scale / was)));
                moved.Canvas.Restore();
            }

            Remember(role, moved, scale);
            Showing(role, moved);
            art.Dispose();

            return;
        }

        if (art is null)
        {
            var made = Surface.CreateExactly(wide, high, wide / scale, high / scale);

            InDips(made, scale);
            Remember(role, made, scale);
            Showing(role, made);

            return;
        }

        if (wide <= art.PixelWidth && high <= art.PixelHeight) return;

        var grown = art.Grown(wide, high, wide / scale, high / scale);

        // After the blit, which Grown does in pixels.
        InDips(grown, scale);

        // Shown before the old one goes: a view holding a disposed surface would render from
        // freed memory on its next frame.
        Remember(role, grown, scale);
        Showing(role, grown);
        art.Dispose();
    }

    private double ScaleOf(CanvasRole role) => role == CanvasRole.Raw ? _rawScale : _processedScale;

    private void Remember(CanvasRole role, Surface art, double scale)
    {
        if (role == CanvasRole.Raw) { Raw = art; _rawScale = scale; }
        else { Processed = art; _processedScale = scale; }
    }

    /// <summary>The scale each surface's canvas transform was built for.</summary>
    /// <remarks>
    /// Kept because a StrokeKit surface does not know: its canvas is in pixels and the
    /// transform on it is this application's doing, so this application is what has to notice
    /// when the display it is being shown on stops matching.
    /// </remarks>
    private double _processedScale = 1;

    private double _rawScale = 1;

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
    /// The visible canvas's offset within <paramref name="topLevel"/> and its size, both in DIPs.
    /// </summary>
    /// <remarks>
    /// For recording: a replay needs to know where the canvas was in order to reconstruct
    /// canvas-local positions from the desktop coordinates the device reported.
    /// </remarks>
    public bool TryGetCanvasGeometry(TopLevel topLevel, out Point originDip, out Size sizeDip)
    {
        foreach (var t in _targets)
        {
            if (t.Role != CanvasRole.Processed || !t.Host.IsEffectivelyVisible) continue;
            if (t.Host.Bounds.Width <= 0 || t.Host.Bounds.Height <= 0) continue;
            if (t.Host.TranslatePoint(new Point(0, 0), topLevel) is not { } o) continue;

            originDip = o;
            sizeDip = new Size(t.Host.Bounds.Width, t.Host.Bounds.Height);
            return true;
        }

        originDip = default;
        sizeDip = default;
        return false;
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

        // Crossing to the other canvas ends the segment, and the next sample starts a new stroke.
        // The engine has to be told, or it would be given a second BeginStroke with no end between
        // them and carry the first stroke's state into the second.
        CloseEngineStroke();
        return true;
    }

    /// <summary>Add one pen sample to the stroke in progress, starting one if needed.</summary>
    /// <param name="rawPressure">Pressure before the pipeline, which drives the raw surface.</param>
    /// <param name="processedPressure">The pipeline's output, which drives the processed surface.</param>
    public void AddSample(Point pos, double rawPressure, double processedPressure, BrushSettings brush,
                          PenOrientation orientation = default, long timestampMicroseconds = 0)
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
        if (_lastSample is null)
        {
            PickStrokeColor(brush.ColorMode);
            History.BeginStroke(brush, _strokeColor);
            _engine.BeginStroke();
        }

        // Built once, and used for both the history and the engine. The two used to be assembled
        // separately from the same arguments, which is two objects that have to agree.
        var sample = new StrokeSample(pos, rawPressure, orientation, processedPressure,
                                      timestampMicroseconds);
        History.AddSample(sample);

        // No predecessor to ramp from on the first sample of a stroke, so the segment is skipped
        // entirely -- there is nothing to draw from. The width the previous stroke finished on
        // never enters this one, because the previous sample is cleared when a stroke ends.
        if (_lastSample is { } from)
        {
            // Both surfaces are drawn on every segment. The processed one takes the pipeline
            // output; the raw one takes unprocessed pressure, which is the comparison.
            if (Processed?.Canvas is { } pc && (brush.DrawAtZeroPressure || processedPressure > 0))
            {
                _engine.DrawSegment(Processed!, from, sample, brush, _strokeColor, PressureChannel.Processed);
                _processedDirty = true;
            }
            if (Raw?.Canvas is { } rc)
            {
                _engine.DrawSegment(Raw!, from, sample, brush, _strokeColor, PressureChannel.Raw);
                _rawDirty = true;
            }
        }

        _lastSample = sample;
    }

    /// <summary>
    /// Stamp the alignment test figure at <paramref name="pos"/> on the canvas given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Diagnostic, not drawing: this is how a tap answers "is the surface itself sound?" without
    /// a stroke in the way. See <see cref="TestPattern"/> for what the figure is measuring.
    /// </para>
    /// <para>
    /// Deliberately <b>not recorded in <see cref="History"/></b>. A stamp is not a stroke, and
    /// letting undo peel these off one at a time would mean replay had to know how to redraw
    /// them. Clear removes them, which is all a diagnostic mark needs.
    /// </para>
    /// <para>
    /// Ends any stroke in progress first. A tap that lands mid-stroke should not be joined to it
    /// by a line from wherever the pen last was.
    /// </para>
    /// </remarks>
    public void DrawTestPattern(Point pos, CanvasRole role)
    {
        EndStroke();

        var surface = SurfaceFor(role);
        if (surface?.Canvas is not { } canvas) return;

        TestPattern.Draw(canvas, pos, BlackStroke);

        if (role == CanvasRole.Raw) _rawDirty = true;
        else _processedDirty = true;
    }

    /// <summary>
    /// Close the engine's stroke, if one is open, and forget the previous sample.
    /// </summary>
    /// <remarks>
    /// Guarded, so <see cref="IBrushEngine.BeginStroke"/> and <see cref="IBrushEngine.EndStroke"/>
    /// arrive strictly in pairs. Without the guard a hovering pen would emit an unmatched end on
    /// every sample -- <see cref="AddSample"/> ends the stroke whenever pressure is zero, and a
    /// pen resting above the tablet reports that at the device's full rate.
    /// </remarks>
    private void CloseEngineStroke()
    {
        if (_lastSample is null) return;
        _lastSample = null;
        _engine.EndStroke();
    }

    /// <summary>End the segment in progress without clearing anything that was drawn.</summary>
    public void EndStroke()
    {
        CloseEngineStroke();
        History.EndStroke();

        // Bake anything the cap pushed out into the baseline before losing the samples.
        while (History.EvictOldestIfOverCap() is { } evicted) BakeIntoBaseline(evicted);
    }

    /// <summary>Render an evicted stroke into the baseline so undo can still restore it.</summary>
    private void BakeIntoBaseline(Stroke stroke)
    {
        Bake(Processed, ref _processedBaseline, PressureChannel.Processed);
        Bake(Raw, ref _rawBaseline, PressureChannel.Raw);

        void Bake(Surface? surface, ref SKBitmap? baseline, PressureChannel channel)
        {
            if (surface is null || surface.PixelWidth <= 0 || surface.PixelHeight <= 0) return;

            // The surface only ever grows, so a baseline from an earlier size is still valid at
            // the origin - copy it into a larger one rather than starting over.
            if (baseline is null || baseline.Width < surface.PixelWidth || baseline.Height < surface.PixelHeight)
            {
                var grown = new SKBitmap(surface.PixelWidth, surface.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                if (baseline is not null)
                {
                    using var copy = new SKCanvas(grown);
                    copy.DrawBitmap(baseline, 0, 0);
                    baseline.Dispose();
                }
                baseline = grown;
            }

            // A surface of its own rather than a canvas over the baseline, because an engine
            // draws onto surfaces. Its logical size carries the scaling that the canvas
            // transform used to: samples are in DIPs and the baseline is physical pixels.
            //
            // One extra composite per eviction, which happens when the undo cap pushes a
            // stroke out and not otherwise. Source-over composes the same either way, so a
            // stroke laid here and then drawn on lands where it would have landed directly.
            using var replay = Surface.CreateExactly(
                baseline.Width, baseline.Height,
                baseline.Width / surface.ScaleX, baseline.Height / surface.ScaleY);

            replay.Canvas.Clear(SKColors.Transparent);

            // The same DIP-to-pixel transform the live surfaces carry, because an engine here
            // expects to be handed a canvas that already has it. See SampleTaperEngine.
            replay.Canvas.Scale((float)surface.ScaleX, (float)surface.ScaleY);

            // Bracketed like any other stroke. A stateful engine replaying one must start from
            // nothing, or the baseline would be drawn with whatever the live canvas left behind.
            _engine.BeginStroke();
            var samples = stroke.Samples;
            for (int i = 1; i < samples.Count; i++)
            {
                if (!stroke.Brush.DrawAtZeroPressure && samples[i].PressureFor(channel) <= 0) continue;
                _engine.DrawSegment(replay, samples[i - 1], samples[i],
                                    stroke.Brush, stroke.Color, channel);
            }
            _engine.EndStroke();

            using var laid = replay.Snapshot();
            using var onto = new SKCanvas(baseline);

            onto.DrawImage(laid, 0, 0);
        }
    }

    /// <summary>Push whichever surfaces were drawn on to their hosts.</summary>
    public void PresentDirty()
    {
        if (_processedDirty) { Redraw(CanvasRole.Processed); _processedDirty = false; }
        if (_rawDirty) { Redraw(CanvasRole.Raw); _rawDirty = false; }

        void Redraw(CanvasRole role)
        {
            foreach (var t in _targets)
            {
                if (t.Role == role) t.View.InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Wipe both surfaces. They are two views of one stroke, so clearing only the half that was
    /// right-clicked would leave the comparison mismatched.
    /// </summary>
    public void Clear()
    {
        Wipe(Processed);
        Wipe(Raw);
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
        CloseEngineStroke();
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

        Wipe(Processed);
        Wipe(Raw);
        // Evicted strokes first: they are no longer replayable, but they are still on screen and
        // must stay there. Without this the cap would quietly delete work on the next undo.
        if (_processedBaseline is not null) Processed?.Canvas.DrawBitmap(_processedBaseline, 0, 0);
        if (_rawBaseline is not null) Raw?.Canvas.DrawBitmap(_rawBaseline, 0, 0);
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

        // One pair of brackets for the stroke, not one per surface. Both channels are drawn from
        // the same gesture and begin together; the per-mark channel argument is what keeps their
        // state apart inside the engine.
        _engine.BeginStroke();

        var samples = stroke.Samples;
        for (int i = 1; i < samples.Count; i++)
        {
            var prev = samples[i - 1];
            var s = samples[i];

            if (Processed?.Canvas is { } pc && (stroke.Brush.DrawAtZeroPressure || s.ProcessedPressure > 0))
                _engine.DrawSegment(Processed!, prev, s, stroke.Brush, stroke.Color, PressureChannel.Processed);

            if (Raw?.Canvas is { } rc)
                _engine.DrawSegment(Raw!, prev, s, stroke.Brush, stroke.Color, PressureChannel.Raw);
        }

        _engine.EndStroke();
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

        // Null where a role's host was never visible: EnsureSurfaces only sizes a surface for
        // a host that is showing, so a session that never opened the compare tab has no raw
        // surface at all. That is ordinary, and was a crash on close until it was written down.
        Processed?.Dispose();
        Raw?.Dispose();
        _processedBaseline?.Dispose();
        _rawBaseline?.Dispose();
    }
}
