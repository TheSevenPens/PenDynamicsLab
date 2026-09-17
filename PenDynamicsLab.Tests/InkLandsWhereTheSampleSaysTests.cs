using Avalonia;
using PenDynamicsLab.Drawing;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// That a sample in device-independent units puts ink at the matching physical pixel.
/// </summary>
/// <remarks>
/// <para>
/// Everything in this application draws in DIPs, and the surface it draws into is measured in
/// physical pixels. Something has to bridge that, and when it is missing nothing fails: the
/// mark is correct at the origin and lands short by a factor of the render scaling everywhere
/// else, which reads as ink drifting further from the nib the further out you draw.
/// </para>
/// <para>
/// It cannot be seen from the unit tests that existed, because they check what the engine is
/// asked to draw rather than where the ink ends up. This one reads the surface back.
/// </para>
/// </remarks>
public class InkLandsWhereTheSampleSaysTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.25)]
    public void A_mark_in_dips_lands_at_that_point_times_the_scale(double scale)
    {
        var session = new DrawingSession([]);

        session.EnsureAtLeast(CanvasRole.Processed, 200, 200, scale);

        var brush = BrushSettings.Default;

        // A short stroke well away from the origin, where a missing transform shows.
        for (var each = 0; each < 8; each++)
        {
            session.AddSample(new Point(100 + each, 100 + each), 0.8, 0.8, brush);
        }

        session.EndStroke();

        var art = session.Processed;

        Assert.NotNull(art);

        var ink = art.InkBounds();

        Assert.NotNull(ink);

        // Where the mark should be, in pixels, allowing for the nib's own width around it.
        var expected = 100 * scale;
        var slack = brush.Size * scale;

        Assert.True(
            Math.Abs(ink.Value.Left - expected) < slack,
            $"at {scale}x a sample at 100 DIP should ink near {expected}px; it inked at {ink.Value.Left}px");
    }

    /// <summary>
    /// A mark keeps its place in DIPs when the window moves to a display that scales differently.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what shipped broken.</b> The surface is rebuilt when it needs more pixels, and
    /// a move from a 2.25x display to a 1.75x one needs fewer -- so nothing was rebuilt, and the
    /// canvas went on carrying a transform for a scale it was no longer being shown at. Every
    /// mark then drew long by the ratio: right at the origin, drifting further from the nib the
    /// further out the pen went.
    /// </para>
    /// <para>
    /// The old content is resampled rather than copied pixel for pixel, because it was put down
    /// in DIPs and should stay where those DIPs are.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(2.25, 1.75)]
    [InlineData(1.0, 2.0)]
    public void A_mark_holds_its_place_when_the_scale_changes(double before, double after)
    {
        var session = new DrawingSession([]);
        var brush = BrushSettings.Default;

        session.EnsureAtLeast(CanvasRole.Processed, 200, 200, before);

        for (var each = 0; each < 8; each++)
        {
            session.AddSample(new Point(100 + each, 100 + each), 0.8, 0.8, brush);
        }

        session.EndStroke();

        // The window moves to a display that scales differently. Same size in DIPs.
        session.EnsureAtLeast(CanvasRole.Processed, 200, 200, after);

        var art = session.Processed;

        Assert.NotNull(art);

        var ink = art.InkBounds();

        Assert.NotNull(ink);

        var expected = 100 * after;
        var slack = brush.Size * after;

        Assert.True(
            Math.Abs(ink.Value.Left - expected) < slack,
            $"after {before}x to {after}x a mark at 100 DIP should still ink near {expected}px; "
            + $"it is at {ink.Value.Left}px");
    }
}
