using Avalonia;
using PenDynamicsLab.Curves;
using PenDynamicsLab.Drawing;
using SkiaSharp;
using StrokeKit.Surfaces;

namespace PenDynamicsLab.Diagnostics;

/// <summary>
/// How a replay turns a device position into a canvas position.
/// </summary>
public enum CoordinateMode
{
    /// <summary>
    /// Keep the device's sub-pixel precision.
    /// </summary>
    Exact,

    /// <summary>
    /// Truncate to a whole physical pixel first, reproducing the precision the app used to throw
    /// away before converting. Kept so the cost of that loss can be measured rather than argued
    /// about.
    /// </summary>
    TruncateToPixel,
}

/// <summary>
/// Draws a <see cref="StrokeRecording"/> to a bitmap, with no window and no pen.
/// </summary>
/// <remarks>
/// <para>
/// Runs the same pipeline and the same <see cref="IBrushEngine"/> the live canvas does, at the
/// same scale, so what comes out is the mark the app would have made from that input under those
/// settings — not an approximation of it.
/// </para>
/// <para>
/// Deliberately headless. Investigating stroke quality means rendering the same input a dozen
/// ways and comparing, and anything that needs a window, a tablet, or a human holding a pen
/// cannot be done a dozen times.
/// </para>
/// </remarks>
public static class RecordingRenderer
{
    /// <summary>Background the canvas is cleared to, matching the app's drawing surface.</summary>
    private static readonly SKColor Background = new(0xF7, 0xF7, 0xF2);

    private static readonly SKColor Ink = new(0x1A, 0x1A, 0x2E);

    /// <summary>
    /// Render a recording. The bitmap is in physical pixels, as the real surface is.
    /// </summary>
    /// <param name="scale">Overrides the recording's own scaling, for asking what the same input looks like on a different display.</param>
    public static SKBitmap Render(
        StrokeRecording recording,
        PressureCurveParams? curve = null,
        SmoothingOrder order = SmoothingOrder.SmoothThenCurve,
        BrushSettings? brush = null,
        CoordinateMode coords = CoordinateMode.Exact,
        double? scale = null,
        double positionSmoothing = 0)
    {
        curve ??= PressureCurveParams.Default;
        brush ??= BrushSettings.Default;
        double s = scale ?? (recording.RenderScaling <= 0 ? 1 : recording.RenderScaling);

        int w = Math.Max(1, (int)Math.Ceiling(recording.CanvasWidth * s));
        int h = Math.Max(1, (int)Math.Ceiling(recording.CanvasHeight * s));

        // A surface rather than a canvas over the bitmap, because an engine draws onto
        // surfaces. Its logical size carries the render scaling the canvas transform used to:
        // samples are in DIPs and this is in physical pixels.
        using var art = Surface.CreateExactly(w, h, w / s, h / s);

        art.Canvas.Clear(Background);

        // The DIP-to-pixel transform an engine here expects on the canvas it is handed.
        art.Canvas.Scale((float)s);

        using var engine = new SampleTaperEngine();
        // A recording is one stroke as far as an engine is concerned. Bracketing it matters for
        // any engine carrying state between segments, even though this one carries none.
        engine.BeginStroke();
        var pipeline = new DynamicsPipeline();

        // The whole previous sample rather than its position and pressure separately, because
        // the engine is handed both endpoints and decides the mark itself.
        StrokeSample? last = null;

        // Position smoothing, which the app does not have at all: the pipeline filters pressure
        // and has channels for tilt and twist, but the path is drawn through raw sample positions.
        // An EMA here is the crudest possible stand-in for the spline fitting a paint program
        // does, and exists to answer whether the roughness is the pen's jitter being drawn
        // faithfully - not as a proposal for how to fix it.
        double smooth = Math.Clamp(positionSmoothing, 0, 0.95);
        Point? filtered = null;

        foreach (var pt in recording.Points)
        {
            double raw = recording.MaxPressure > 0 ? (double)pt.Pressure / recording.MaxPressure : 0;
            var result = pipeline.Process(raw, curve, order);

            if (raw <= 0)
            {
                last = null;
                filtered = null;
                continue;
            }

            var pos = ToCanvas(recording, pt, coords, s);

            if (smooth > 0)
            {
                filtered = filtered is { } f
                    ? new Point(f.X + (pos.X - f.X) * (1 - smooth), f.Y + (pos.Y - f.Y) * (1 - smooth))
                    : pos;
                pos = filtered.Value;
            }

            // Everything the recording carries about this point, not only what the taper engine
            // happens to need. The orientation and the pen's own clock are both recorded and both
            // reach an engine that wants them.
            var sample = new StrokeSample(
                pos,
                raw,
                new PenOrientation(pt.Azimuth, pt.Altitude, pt.Twist, pt.TiltX, pt.TiltY),
                result.Output,
                pt.PenTimeUs);

            if (last is null)
            {
                last = sample;
                continue;
            }

            // Only the processed channel is drawn here. This renders what the pipeline produced,
            // which is the question it exists to answer; the raw comparison is the live canvas's job.
            if (brush.DrawAtZeroPressure || result.Output > 0)
                engine.DrawSegment(art, last.Value, sample, brush, Ink, PressureChannel.Processed);

            last = sample;
        }

        engine.EndStroke();

        // Back to a bitmap, which is what every caller of this wants: it is saved, magnified
        // and compared as one.
        var bitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);

        using (var onto = new SKCanvas(bitmap))
        using (var laid = art.Snapshot())
        {
            onto.DrawImage(laid, 0, 0);
        }

        return bitmap;
    }

    /// <summary>Device position to canvas position, in DIPs.</summary>
    internal static Point ToCanvas(StrokeRecording r, RecordedPoint pt, CoordinateMode mode, double scale)
    {
        double dx = pt.DesktopX, dy = pt.DesktopY;

        if (mode == CoordinateMode.TruncateToPixel)
        {
            // What MainWindow did for years: build a PixelPoint, which takes ints, and let the
            // cast discard the fraction. Note it truncates rather than rounds, so the error is
            // one-sided and the centre line drifts as well as wobbles.
            dx = (int)dx;
            dy = (int)dy;
        }

        return new Point((dx - r.CanvasOriginX) / scale, (dy - r.CanvasOriginY) / scale);
    }

    /// <summary>Render straight to a PNG file, optionally magnified for inspection.</summary>
    /// <param name="zoom">Nearest-neighbour magnification. Above 1 this shows pixels, not a smoother picture.</param>
    public static void RenderToFile(
        StrokeRecording recording, string path,
        PressureCurveParams? curve = null,
        SmoothingOrder order = SmoothingOrder.SmoothThenCurve,
        BrushSettings? brush = null,
        CoordinateMode coords = CoordinateMode.Exact,
        double? scale = null,
        int zoom = 1,
        double positionSmoothing = 0)
    {
        using var bitmap = Render(recording, curve, order, brush, coords, scale, positionSmoothing);
        using var shown = zoom <= 1 ? null : Magnify(bitmap, zoom);
        using var image = SKImage.FromBitmap(shown ?? bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    /// <summary>Nearest-neighbour magnification, so magnifying does not invent smoothness.</summary>
    private static SKBitmap Magnify(SKBitmap src, int zoom)
    {
        var dst = new SKBitmap(src.Width * zoom, src.Height * zoom, src.ColorType, src.AlphaType);
        using var canvas = new SKCanvas(dst);
        using var image = SKImage.FromBitmap(src);
        canvas.DrawImage(image, new SKRect(0, 0, dst.Width, dst.Height),
            new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        return dst;
    }
}
