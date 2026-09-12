using Avalonia;
using SkiaSharp;

namespace PenDynamicsLab.Drawing;

/// <summary>
/// Puts one segment of a stroke onto a canvas.
/// </summary>
/// <remarks>
/// <para>
/// One implementation today — <see cref="RoundBrushEngine"/>, an antialiased taper between two
/// round ends. The interface exists so a stamp engine can be added without touching the window or
/// the drawing session: it would interpolate dabs between <c>from</c> and <c>to</c> rather than
/// filling a taper, and nothing above it needs to know.
/// </para>
/// <para>
/// Coordinates are DIPs. <c>DrawSurface</c>'s canvas transform converts to physical pixels, and
/// is the only place that knows the display scaling.
/// </para>
/// </remarks>
public interface IBrushEngine : IDisposable
{
    /// <summary>Draw from <paramref name="from"/> to <paramref name="to"/> on the canvas.</summary>
    /// <param name="widthFrom">Width in DIPs at the start of the segment.</param>
    /// <param name="widthTo">Width in DIPs at the end of the segment.</param>
    /// <param name="opacity">0-1; applied to <paramref name="color"/>'s alpha.</param>
    void DrawSegment(SKCanvas canvas, Point from, Point to, SKColor color,
        float widthFrom, float widthTo, float opacity);
}

/// <summary>An antialiased taper between two round ends.</summary>
/// <remarks>
/// <para>
/// This used to stroke a straight line at a single width, taken from the sample at the end of the
/// segment. Width therefore <b>stepped</b> at every sample boundary rather than changing along
/// the segment, and the silhouette of a stroke was a staircase wherever pressure moved — which at
/// tablet report rates is everywhere. Tapering between the two samples' widths is what turns that
/// staircase into a ramp.
/// </para>
/// <para>
/// Consecutive segments share an endpoint <i>and</i> a width — segment i ends at the width segment
/// i+1 starts at — so the ramp is continuous across the whole stroke rather than only within each
/// piece of it.
/// </para>
/// </remarks>
public sealed class RoundBrushEngine : IBrushEngine
{
    // One paint and one path for the life of the engine. These used to be allocated per segment —
    // on every point, for both surfaces, at 16 ms — which is a lot of garbage for a few writes.
    private readonly SKPaint _paint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
    };

    private readonly SKPath _path = new();

    public void DrawSegment(SKCanvas canvas, Point from, Point to, SKColor color,
        float widthFrom, float widthTo, float opacity)
    {
        byte alpha = (byte)Math.Clamp(opacity * 255, 0, 255);
        _paint.Color = color.WithAlpha(alpha);

        BuildTaper(_path,
            new SKPoint((float)from.X, (float)from.Y), widthFrom / 2f,
            new SKPoint((float)to.X, (float)to.Y), widthTo / 2f);

        canvas.DrawPath(_path, _paint);
    }

    /// <summary>
    /// Build the outline of two circles and the region swept between them, into
    /// <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One closed contour, not three overlapping shapes.</b> The obvious construction — a
    /// quad between the tangent points plus a circle at each end — is wrong as soon as the brush
    /// is translucent, because the overlaps get painted twice and show as darker lozenges at
    /// every sample. A single filled path has no overlaps to double.
    /// </para>
    /// <para>
    /// The straight sides are the circles' external tangents. For centres <c>d</c> apart with
    /// radii <c>ra</c> and <c>rb</c>, both tangent points lie along the same normal, offset from
    /// the centre line by <c>asin((ra - rb) / d)</c> — which is what makes the sides meet the
    /// caps smoothly instead of cutting across them.
    /// </para>
    /// </remarks>
    internal static void BuildTaper(SKPath path, SKPoint a, float ra, SKPoint b, float rb)
    {
        path.Reset();

        ra = Math.Max(ra, 0.01f);
        rb = Math.Max(rb, 0.01f);

        float dx = b.X - a.X, dy = b.Y - a.Y;
        float d = MathF.Sqrt(dx * dx + dy * dy);

        // Degenerate cases, both of which have no tangents to compute: the centres coincide, or
        // one circle swallows the other. The union is then just the larger circle.
        if (d <= MathF.Abs(ra - rb) + 1e-4f)
        {
            if (ra >= rb) path.AddCircle(a.X, a.Y, ra);
            else path.AddCircle(b.X, b.Y, rb);
            return;
        }

        float phi = MathF.Atan2(dy, dx);
        float alpha = MathF.Asin(Math.Clamp((ra - rb) / d, -1f, 1f));

        // The shared normal of the two external tangent lines, one on each side of the axis.
        float up = phi + MathF.PI / 2 + alpha;
        float down = phi - MathF.PI / 2 - alpha;

        float alphaDeg = alpha * 180f / MathF.PI;

        path.MoveTo(a.X + ra * MathF.Cos(up), a.Y + ra * MathF.Sin(up));
        path.LineTo(b.X + rb * MathF.Cos(up), b.Y + rb * MathF.Sin(up));

        // Round the far end, then the near one. Both sweeps run the same way round so the contour
        // stays simple; together they account for the full 360 degrees the two caps share.
        path.ArcTo(Bounds(b, rb), Deg(up), -(180f + 2f * alphaDeg), false);
        path.LineTo(a.X + ra * MathF.Cos(down), a.Y + ra * MathF.Sin(down));
        path.ArcTo(Bounds(a, ra), Deg(down), -(180f - 2f * alphaDeg), false);

        path.Close();

        static SKRect Bounds(SKPoint c, float r) => new(c.X - r, c.Y - r, c.X + r, c.Y + r);
        static float Deg(float radians) => radians * 180f / MathF.PI;
    }

    public void Dispose()
    {
        _paint.Dispose();
        _path.Dispose();
    }
}
