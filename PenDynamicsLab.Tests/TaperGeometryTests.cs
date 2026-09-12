using PenDynamicsLab.Drawing;
using SkiaSharp;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Pins the shape of one stroke segment.
/// </summary>
/// <remarks>
/// A segment used to be a straight line at one width, so there was no geometry to get wrong. It
/// is now the outline of two circles and the region swept between them, built from the circles'
/// external tangents — which is exactly the kind of construction that looks right in the common
/// case and falls apart at the edges, so the edges are what these cover.
/// </remarks>
public class TaperGeometryTests
{
    private const float Tol = 0.05f;

    private static SKRect TaperBounds(float ax, float ay, float ra, float bx, float by, float rb)
    {
        using var path = new SKPath();
        RoundBrushEngine.BuildTaper(path, new SKPoint(ax, ay), ra, new SKPoint(bx, by), rb);
        return path.TightBounds;
    }

    [Fact]
    public void EqualRadiiGiveAStadium()
    {
        // Parallel sides, a half-circle at each end: the bounds are the centre line grown by r.
        var b = TaperBounds(0, 0, 10, 100, 0, 10);

        Assert.Equal(-10f, b.Left, Tol);
        Assert.Equal(110f, b.Right, Tol);
        Assert.Equal(-10f, b.Top, Tol);
        Assert.Equal(10f, b.Bottom, Tol);
    }

    [Fact]
    public void UnequalRadiiTaper()
    {
        // Each end reaches its own radius along the axis: -20 at the wide end, +105 at the narrow
        // one. A segment that used a single width for both would be symmetric, which is the bug
        // this replaced.
        var b = TaperBounds(0, 0, 20, 100, 0, 5);

        Assert.Equal(-20f, b.Left, Tol);
        Assert.Equal(105f, b.Right, Tol);

        // Across the axis it reaches 20*cos(asin((20-5)/100)), not 20. The tangent line to two
        // unequal circles is not perpendicular to the centre line - it is rotated by that angle -
        // so the widest point of a taper sits slightly inboard of the radius. Only when the radii
        // are equal does the angle vanish and the extreme land on the perpendicular.
        const float widest = 19.7737f;   // 20 * cos(asin(0.15))
        Assert.Equal(-widest, b.Top, Tol);
        Assert.Equal(widest, b.Bottom, Tol);
    }

    [Fact]
    public void TheTaperIsSymmetricAboutItsAxis()
    {
        var b = TaperBounds(0, 0, 20, 100, 0, 5);
        Assert.Equal(-b.Top, b.Bottom, Tol);
    }

    [Fact]
    public void DirectionDoesNotChangeTheShape()
    {
        var forward = TaperBounds(0, 0, 20, 100, 0, 5);
        var backward = TaperBounds(100, 0, 5, 0, 0, 20);

        Assert.Equal(forward.Left, backward.Left, Tol);
        Assert.Equal(forward.Right, backward.Right, Tol);
        Assert.Equal(forward.Top, backward.Top, Tol);
        Assert.Equal(forward.Bottom, backward.Bottom, Tol);
    }

    [Fact]
    public void DiagonalSegmentsAreNotSpecialCased()
    {
        // The construction runs on atan2, so nothing about it should prefer the axes. A stadium
        // at 45 degrees is symmetric about its own axis, so its bounding box comes out square,
        // and each side spans the centre line plus a radius at each end.
        //
        // Note what this does NOT assert: that a rotated segment keeps the bounding box of an
        // axis-aligned one. It does not, and cannot - an axis-aligned bound is not invariant
        // under rotation.
        var diagonal = TaperBounds(0, 0, 10, 70.71f, 70.71f, 10);

        Assert.Equal(diagonal.Width, diagonal.Height, 0.5f);
        Assert.Equal(90.71f, diagonal.Width, 0.5f);   // 70.71 + 10 + 10
    }

    // ── Degenerate cases: no tangents exist, and the naive formula divides by zero ──

    [Fact]
    public void OneCircleSwallowingTheOtherIsJustTheLargerCircle()
    {
        // A slow pen reporting a much heavier sample almost on top of the last one.
        var b = TaperBounds(0, 0, 30, 2, 0, 5);

        Assert.Equal(-30f, b.Left, Tol);
        Assert.Equal(30f, b.Right, Tol);
    }

    [Fact]
    public void CoincidentPointsDrawADot()
    {
        // Two samples at the same position, which a stationary pen produces constantly.
        var b = TaperBounds(50, 50, 8, 50, 50, 8);

        Assert.Equal(42f, b.Left, Tol);
        Assert.Equal(58f, b.Right, Tol);
    }

    [Fact]
    public void ZeroWidthStillLeavesSomethingToDraw()
    {
        // Pressure can reach zero while DrawAtZeroPressure is on. A zero-radius circle has no
        // outline and would silently drop the segment.
        var b = TaperBounds(0, 0, 0, 50, 0, 10);

        Assert.True(b.Width > 49f);
        Assert.True(b.Height > 0f);
    }

    [Fact]
    public void TheContourIsClosed()
    {
        // An open contour fills unpredictably, and with a translucent brush the failure shows as
        // a wedge of wrong alpha rather than as a missing shape.
        using var path = new SKPath();
        RoundBrushEngine.BuildTaper(path, new SKPoint(0, 0), 12, new SKPoint(60, 20), 4);

        Assert.True(path.PointCount > 0);
        Assert.Equal(SKPathFillType.Winding, path.FillType);
    }
}
