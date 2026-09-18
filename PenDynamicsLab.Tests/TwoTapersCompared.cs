using Avalonia;
using PenDynamicsLab.Curves;
using PenDynamicsLab.Drawing;
using SkiaSharp;
using StrokeKit.Brushes;
using StrokeKit.Strokes;
using StrokeKit.Surfaces;
using Xunit;

using KitBrush = StrokeKit.Brushes.Brush;
using KitStroke = StrokeKit.Strokes.Stroke;

namespace PenDynamicsLab.Tests;

/// <summary>
/// This application's taper against the kit's <c>Engine.SampleTaper</c>, in pixels.
/// </summary>
/// <remarks>
/// <para>
/// The gate on <c>#78</c>. Both draw one filled taper between consecutive samples, so the
/// question is whether adopting the kit's engine would change the mark this application makes
/// — and that is answered by rendering the same input through both and counting.
/// </para>
/// <para>
/// Everything that could differ for an uninteresting reason is held still: the same endpoints,
/// pressures chosen to give the same radii through both width mappings, no flow so the colour
/// is constant, and one segment so nothing about spacing or accumulation is involved.
/// </para>
/// </remarks>
public class TwoTapersCompared
{
    private const int Size = 240;
    private const double Nib = 40;

    /// <summary>Pressure as a fraction, which is what this application's brush takes.</summary>
    private static double Fraction(uint raw) => raw / 1024.0;

    private static BrushSettings Lab() => BrushSettings.Default with
    {
        Size = Nib,
        PressureDrives = PressureControl.Size,
    };

    /// <summary>
    /// A kit brush whose width is the same function of pressure as this application's.
    /// </summary>
    /// <remarks>
    /// The lab's is <c>max(MinStrokeWidth, pressure * Size)</c> with pressure a fraction. A
    /// linear response from a floor of nearly nothing up to the same size is the same line, so
    /// at any pressure well above the floor the two ask for the same diameter.
    /// </remarks>
    private static KitBrush Kit() => new(
        Nib, SKColors.Black, 1,
        Buildup: Buildup.PerStamp,
        Width: new Width(0.01, Nib, 1024, new Response(0, 1, 1)),
        SpacedBy: SpacedBy.Distance, Flow: null, Engine: Engine.SampleTaper);

    private static Surface FromTheLab(SKPoint a, SKPoint b, uint pressureA, uint pressureB)
    {
        var art = Surface.CreateExactly(Size, Size, Size, Size);

        art.Canvas.Clear(SKColors.Transparent);

        using var engine = new RoundBrushEngine();

        engine.BeginStroke();

        var from = new StrokeSample(new Point(a.X, a.Y), Fraction(pressureA), default,
                                    Fraction(pressureA), 0);
        var to = new StrokeSample(new Point(b.X, b.Y), Fraction(pressureB), default,
                                  Fraction(pressureB), 0);

        engine.DrawSegment(art.Canvas, from, to, Lab(), SKColors.Black,
                           PressureChannel.Processed);

        engine.EndStroke();

        return art;
    }

    private static Surface FromTheKit(SKPoint a, SKPoint b, uint pressureA, uint pressureB)
    {
        var art = Surface.CreateExactly(Size, Size, Size, Size);

        art.Canvas.Clear(SKColors.Transparent);

        var stroke = new KitStroke([
            Synthetic.Reading(a.X, a.Y, pressureA),
            Synthetic.Reading(b.X, b.Y, pressureB),
        ]);

        Kit().Draw(art, InkTransform.For(art), stroke);

        return art;
    }

    /// <summary>How many rows of a column carry any ink, which is the span across it.</summary>
    private static int Across(Surface art, int column)
    {
        int top = -1, bottom = -1;

        for (var row = 0; row < Size; row++)
        {
            if (art.ReadStored(column, row).Alpha == 0) continue;

            if (top < 0) top = row;

            bottom = row;
        }

        return top < 0 ? 0 : bottom - top + 1;
    }

    /// <summary>Pixels where the two disagree at all, and the worst alpha gap.</summary>
    private static (int Differing, int Worst) Compared(Surface left, Surface right)
    {
        var a = left.ReadAll();
        var b = right.ReadAll();

        int differing = 0, worst = 0;

        for (var each = 0; each < a.Length; each++)
        {
            if (a[each] == b[each]) continue;

            differing++;
            worst = Math.Max(worst, Math.Abs(a[each].Alpha - b[each].Alpha));
        }

        return (differing, worst);
    }

    /// <summary>
    /// Even where the shapes agree, the edges do not — because one is drawn twice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A segment whose two ends are nearly the same width has almost no tangent angle, so both
    /// engines build the same outline. They still differ, at the edges only: the kit lays a dot
    /// at the first reading and then the taper, and two antialiased fills of the same colour
    /// composite to a firmer edge than one. Measured at <b>228 pixels</b>, worst alpha gap 112,
    /// with the two ends spanning the same 32 pixels.
    /// </para>
    /// <para>
    /// This is the same effect the evaluation on <c>StrokeKit#7</c> measured at 1,604 pixels
    /// between a grouped fill and the same pieces drawn separately. It is a floor under any
    /// comparison of these two engines, and it is not removable by matching brush settings.
    /// </para>
    /// </remarks>
    [Fact]
    public void Even_where_the_shape_agrees_the_edges_differ()
    {
        var a = new SKPoint(90, 120);
        var b = new SKPoint(150, 120);

        using var lab = FromTheLab(a, b, 800, 790);
        using var kit = FromTheKit(a, b, 800, 790);

        var (differing, worst) = Compared(lab, kit);

        // The shapes agree: the wide end spans its full diameter in both.
        Assert.Equal(Across(lab, (int)a.X), Across(kit, (int)a.X));

        // The edges do not, and never will while one draws a dot and a taper where the other
        // draws a taper.
        Assert.True(differing is > 0 and < 400,
            $"an even segment differed at {differing} pixels, worst alpha gap {worst}");
    }

    /// <summary>
    /// A segment whose width changes sharply, now that both draw the same shape.
    /// </summary>
    /// <remarks>
    /// This differed at 387 and 436 pixels with alpha gaps of 255 — pixels fully inked in one
    /// and empty in the other — while the two shapes disagreed. With issue 81 fixed the shapes
    /// agree and what is left is the edge effect, the same as for an even segment.
    /// </remarks>
    [Fact]
    public void A_segment_that_changes_width_fast_now_differs_only_at_the_edges()
    {
        using var lab = FromTheLab(new SKPoint(90, 120), new SKPoint(150, 120), 1000, 120);
        using var kit = FromTheKit(new SKPoint(90, 120), new SKPoint(150, 120), 1000, 120);

        var (differing, worst) = Compared(lab, kit);

        Assert.True(differing < 400,
            $"a width-changing segment differed at {differing} pixels, worst alpha gap {worst}");

        // No pixel is solid in one and absent from the other any more. That is what the shapes
        // agreeing means, and it was 255 before issue 81.
        Assert.True(worst < 255, $"worst alpha gap was {worst}");
    }

    /// <summary>
    /// Why they differ: one turns the tangent normal towards the wide end and one away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both build the same contour — two straight sides and two arcs — from
    /// <c>alpha = asin((ra - rb) / d)</c>. The kit puts the tangent points at
    /// <c>phi ± pi/2 ∓ alpha</c> and gives the wide end the larger arc; this application puts
    /// them at <c>phi ± pi/2 ± alpha</c> and gives the wide end the smaller one.
    /// </para>
    /// <para>
    /// The kit's is the tangential shape: a tangent meets a radius at a right angle, so the
    /// straight sides leave each cap without crossing it. This application's closes and still
    /// looks like a taper, with sides that cut across the caps — which is the shape the kit's
    /// own documentation describes as the result of turning it the other way.
    /// </para>
    /// <para>
    /// <b>The consequence was that this application drew narrower than it asked for.</b> At a
    /// pressure asking for 39.1 pixels the wide end spanned 36, and 38 where the change was
    /// less steep. Fixed in issue 81; this is the regression against it coming back.
    /// </para>
    /// <para>
    /// <b>The wide end is second on purpose.</b> The kit lays a dot at the <i>first</i> reading,
    /// which would guarantee a full-diameter span there whatever its taper did — measuring the
    /// wide end at the first reading measures the dot. With the dot at the narrow end, the span
    /// at the wide one is the taper alone in both engines.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(120u, 1000u)]
    [InlineData(300u, 1000u)]
    public void Both_draw_the_wide_end_at_the_full_diameter(uint narrow, uint wide)
    {
        var a = new SKPoint(90, 120);
        var b = new SKPoint(150, 120);

        using var lab = FromTheLab(a, b, narrow, wide);
        using var kit = FromTheKit(a, b, narrow, wide);

        var wanted = Nib * Fraction(wide);

        Assert.True(Across(kit, (int)b.X) >= wanted,
            $"the kit's wide end should span its full {wanted:F1} pixels, and spans "
            + $"{Across(kit, (int)b.X)}");

        Assert.True(Across(lab, (int)b.X) >= wanted,
            $"this application's wide end should span its full {wanted:F1} pixels, and spans "
            + $"{Across(lab, (int)b.X)} — if this is short again, the tangent normals have been "
            + "turned the wrong way once more");

        Assert.Equal(Across(kit, (int)b.X), Across(lab, (int)b.X));
    }
}
