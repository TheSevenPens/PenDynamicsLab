using Avalonia;
using SkiaSharp;

namespace PenDynamicsLab.Drawing;

/// <summary>
/// A fixed geometric figure stamped at a tap, for judging whether the canvas rasterizes and
/// scales correctly — independently of anything to do with strokes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> When a stroke does not look as good as another application's, two very
/// different causes produce that impression: the mark could be drawn onto a surface whose scaling
/// or presentation is wrong, or the surface could be perfect and the stroke itself — its spacing,
/// smoothing, width response — could be the weak part. Those need separating before either can be
/// worked on, and a stroke cannot separate them, because it exercises both at once.
/// </para>
/// <para>
/// A figure made entirely of circles can. Circles are the shape that makes a scaling fault
/// impossible to miss, since anything anisotropic turns them into ellipses, and the eye reads a
/// bad ellipse far more reliably than it reads a slightly wrong curve.
/// </para>
/// <para>
/// <b>What to look for.</b> The twelve dots sit between two concentric circles, tangent to both.
/// That tangency is the measurement:
/// </para>
/// <list type="bullet">
///   <item><b>Dots break tangency</b> at some angles but not others — the x and y scales differ,
///   so the figure is being drawn or presented anisotropically.</item>
///   <item><b>Dots look like ellipses</b> — the same fault, visible in each dot as well as in
///   their arrangement.</item>
///   <item><b>Spacing is uneven</b> around the ring — positions are being rounded to whole
///   pixels somewhere they should not be.</item>
///   <item><b>Edges are soft, doubled, or stair-stepped</b> — the bitmap is not landing 1:1 on
///   physical pixels, and is being resampled on its way to the screen.</item>
///   <item><b>Everything is crisp, round and even</b> — the surface is fine, and a stroke that
///   still looks wrong is telling you about the stroke.</item>
/// </list>
/// <para>
/// Every element is antialiased, so the figure is judged on shape rather than on the display's
/// pixel grid — which is the thing the test is trying to see past.
/// </para>
/// </remarks>
public static class TestPattern
{
    /// <summary>Distance from the tap to each dot's centre, in DIPs.</summary>
    public const double RingRadius = 70;

    /// <summary>Radius of each dot on the ring, in DIPs.</summary>
    public const double DotRadius = 14;

    /// <summary>How many dots go round the ring.</summary>
    /// <remarks>
    /// Twelve puts a dot on both axes and both diagonals — the directions an anisotropic scale
    /// separates most — and still leaves clear space between neighbours at these radii.
    /// </remarks>
    public const int DotCount = 12;

    /// <summary>Line width for every outline, in DIPs.</summary>
    /// <remarks>
    /// Thin on purpose. A heavy outline hides exactly the softness and stair-stepping this is
    /// meant to reveal.
    /// </remarks>
    public const float LineWidth = 1.25f;

    /// <summary>Half-width and half-height of the figure, in DIPs.</summary>
    public const double Extent = RingRadius + DotRadius;

    /// <summary>Stamp the figure centred on <paramref name="center"/>, in DIPs.</summary>
    public static void Draw(SKCanvas canvas, Point center, SKColor color)
    {
        using var outline = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = LineWidth,
        };
        using var fill = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        float cx = (float)center.X;
        float cy = (float)center.Y;

        // The two circles the dots are tangent to. Drawn first so the dots sit on top of them and
        // any break in tangency shows as a gap or an overlap rather than as a hidden crossing.
        canvas.DrawCircle(cx, cy, (float)(RingRadius - DotRadius), outline);
        canvas.DrawCircle(cx, cy, (float)(RingRadius + DotRadius), outline);

        for (int i = 0; i < DotCount; i++)
        {
            // Starting at -90 degrees puts the first dot at twelve o'clock, so the figure is
            // symmetric about the vertical axis and a vertical scale error reads directly.
            double angle = -Math.PI / 2 + i * 2 * Math.PI / DotCount;
            float dx = cx + (float)(RingRadius * Math.Cos(angle));
            float dy = cy + (float)(RingRadius * Math.Sin(angle));
            canvas.DrawCircle(dx, dy, (float)DotRadius, outline);
        }

        // Marks the tap itself, so it is visible whether the figure is centred where the pen
        // actually came down — a coordinate offset, rather than a scaling one, shows up here.
        canvas.DrawCircle(cx, cy, 2f, fill);
    }
}
