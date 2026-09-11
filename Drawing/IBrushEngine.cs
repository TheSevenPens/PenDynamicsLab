using Avalonia;
using SkiaSharp;

namespace PenDynamicsLab.Drawing;

/// <summary>
/// Puts one segment of a stroke onto a canvas.
/// </summary>
/// <remarks>
/// <para>
/// One implementation today — <see cref="RoundBrushEngine"/>, a round antialiased line, which is
/// what the app has always drawn. The interface exists so a stamp engine can be added without
/// touching the window or the drawing session: it would interpolate dabs between
/// <c>from</c> and <c>to</c> rather than stroking a line, and nothing above it needs to know.
/// </para>
/// <para>
/// Coordinates are DIPs. <c>DrawSurface</c>'s canvas transform converts to physical pixels, and
/// is the only place that knows the display scaling.
/// </para>
/// </remarks>
public interface IBrushEngine : IDisposable
{
    /// <summary>Stroke from <paramref name="from"/> to <paramref name="to"/> on the canvas.</summary>
    /// <param name="strokeWidth">Width in DIPs.</param>
    /// <param name="opacity">0-1; applied to <paramref name="color"/>'s alpha.</param>
    void DrawSegment(SKCanvas canvas, Point from, Point to, SKColor color, float strokeWidth, float opacity);
}

/// <summary>The round antialiased line the app has always drawn.</summary>
public sealed class RoundBrushEngine : IBrushEngine
{
    // One paint for the life of the engine. This used to be allocated per segment — on every
    // point, for both surfaces, at 16 ms — which is a lot of garbage for four property writes.
    private readonly SKPaint _paint = new()
    {
        StrokeCap = SKStrokeCap.Round,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
    };

    public void DrawSegment(SKCanvas canvas, Point from, Point to, SKColor color, float strokeWidth, float opacity)
    {
        byte alpha = (byte)Math.Clamp(opacity * 255, 0, 255);
        _paint.Color = color.WithAlpha(alpha);
        _paint.StrokeWidth = strokeWidth;
        canvas.DrawLine((float)from.X, (float)from.Y, (float)to.X, (float)to.Y, _paint);
    }

    public void Dispose() => _paint.Dispose();
}
