using System.Collections.Immutable;
using PenDynamicsLab.Curves;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Pins type-scoped reset: it restores the current type's settings and never changes the
/// type, nor the settings belonging to the types you are not looking at.
/// </summary>
public class CurveDefaultsTests
{
    /// <summary>Every curve field moved off its default, so any reset is visible.</summary>
    private static PressureCurveParams Tweaked(CurveType type) => new()
    {
        CurveType = type,
        Softness = 0.6,
        InputMinimum = 0.2,
        InputMaximum = 0.9,
        Minimum = 0.1,
        Maximum = 0.8,
        MinApproach = MinApproach.Cut,
        FlatLevel = 0.25,
        BezierPoints = BezierPresets.All.First(b => b.Name == "Heavy").Points,
    };

    // ── The type survives ────────────────────────────────────────

    [Theory]
    [InlineData(CurveType.Basic)]
    [InlineData(CurveType.Extended)]
    [InlineData(CurveType.Sigmoid)]
    [InlineData(CurveType.Flat)]
    [InlineData(CurveType.Bezier)]
    public void ResetCurve_KeepsTheSelectedType(CurveType type)
        => Assert.Equal(type, CurveDefaults.ResetCurve(Tweaked(type)).CurveType);

    [Fact]
    public void ResetSmoothing_KeepsTheSelectedType()
    {
        var p = new PressureCurveParams { SmoothingType = SmoothingType.Ema, EmaSmoothing = 0.7 };
        Assert.Equal(SmoothingType.Ema, CurveDefaults.ResetSmoothing(p).SmoothingType);
    }

    // ── Each type's own settings come back ───────────────────────

    [Fact]
    public void ResetCurve_Basic_RestoresTheInitialBasicCurve()
    {
        var reset = CurveDefaults.ResetCurve(Tweaked(CurveType.Basic));
        Assert.Equal(0, reset.Softness);
        // Which is to say: the identity mapping you get on first picking Basic.
        Assert.Equal(0.37, CurveMath.ApplyPressureCurve(0.37, reset), 1e-9);
    }

    [Fact]
    public void ResetCurve_Extended_RestoresSoftnessAndTheFullRange()
    {
        var reset = CurveDefaults.ResetCurve(Tweaked(CurveType.Extended));
        Assert.Equal(0, reset.Softness);
        Assert.Equal(0, reset.InputMinimum);
        Assert.Equal(1, reset.InputMaximum);
        Assert.Equal(0, reset.Minimum);
        Assert.Equal(1, reset.Maximum);
        Assert.Equal(MinApproach.Clamp, reset.MinApproach);
    }

    [Fact]
    public void ResetCurve_Flat_RestoresTheLevel()
        => Assert.Equal(PressureCurveParams.Default.FlatLevel,
                        CurveDefaults.ResetCurve(Tweaked(CurveType.Flat)).FlatLevel);

    [Fact]
    public void ResetCurve_Bezier_RestoresTheDefaultPoints()
        => Assert.Equal(PressureCurveParams.DefaultBezierPoints,
                        CurveDefaults.ResetCurve(Tweaked(CurveType.Bezier)).BezierPoints);

    [Fact]
    public void ResetSmoothing_RestoresTheAmount()
    {
        var p = new PressureCurveParams { SmoothingType = SmoothingType.Ema, EmaSmoothing = 0.7 };
        Assert.Equal(PressureCurveParams.Default.EmaSmoothing, CurveDefaults.ResetSmoothing(p).EmaSmoothing);
    }

    // ── Other types' settings are left alone ─────────────────────

    [Theory]
    [InlineData(CurveType.Basic)]
    [InlineData(CurveType.Flat)]
    public void ResetCurve_LeavesBezierPointsAlone_WhenNotEditingTheBezier(CurveType type)
    {
        var p = Tweaked(type);
        // Shaping a Bezier and then resetting from another card state must not throw the
        // Bezier away — none of it is even on screen at the time.
        Assert.Equal(p.BezierPoints, CurveDefaults.ResetCurve(p).BezierPoints);
    }

    [Theory]
    [InlineData(CurveType.Basic)]
    [InlineData(CurveType.Flat)]
    [InlineData(CurveType.Bezier)]
    public void ResetCurve_LeavesTheRangeFieldsAlone_ForTypesThatDoNotUseThem(CurveType type)
    {
        var p = Tweaked(type);
        var reset = CurveDefaults.ResetCurve(p);
        Assert.Equal(p.InputMinimum, reset.InputMinimum);
        Assert.Equal(p.InputMaximum, reset.InputMaximum);
        Assert.Equal(p.Minimum, reset.Minimum);
        Assert.Equal(p.Maximum, reset.Maximum);
        Assert.Equal(p.MinApproach, reset.MinApproach);
    }

    [Fact]
    public void ResetCurve_LeavesTheFlatLevelAlone_WhenNotOnFlat()
    {
        var p = Tweaked(CurveType.Basic);
        Assert.Equal(p.FlatLevel, CurveDefaults.ResetCurve(p).FlatLevel);
    }

    [Fact]
    public void ResetCurve_LeavesSmoothingAlone()
    {
        var p = Tweaked(CurveType.Basic) with { SmoothingType = SmoothingType.Ema, EmaSmoothing = 0.7 };
        var reset = CurveDefaults.ResetCurve(p);
        Assert.Equal(SmoothingType.Ema, reset.SmoothingType);
        Assert.Equal(0.7, reset.EmaSmoothing);
    }

    [Fact]
    public void ResetSmoothing_LeavesTheCurveAlone()
    {
        var p = Tweaked(CurveType.Basic) with { SmoothingType = SmoothingType.Ema, EmaSmoothing = 0.7 };
        var reset = CurveDefaults.ResetSmoothing(p);
        Assert.Equal(CurveType.Basic, reset.CurveType);
        Assert.Equal(0.6, reset.Softness);
    }

    // ── Passthrough has nothing to restore ───────────────────────

    [Fact]
    public void ResetCurve_UnderPassthrough_ChangesNothing()
    {
        var p = Tweaked(CurveType.Passthrough);
        Assert.Equal(p, CurveDefaults.ResetCurve(p));
    }

    [Fact]
    public void ResetSmoothing_UnderPassthrough_ChangesNothing()
    {
        var p = new PressureCurveParams { SmoothingType = SmoothingType.Passthrough, EmaSmoothing = 0.7 };
        Assert.Equal(p, CurveDefaults.ResetSmoothing(p));
    }

    // ── Reset lands where the pills say it should ────────────────

    [Theory]
    [InlineData(CurveType.Basic)]
    [InlineData(CurveType.Extended)]
    [InlineData(CurveType.Sigmoid)]
    public void ResetCurve_LeavesTheParametricTypes_ReadingOnWithNoEffect(CurveType type)
        // Their defaults are the identity mapping, so the amber pill is the honest answer
        // right after a reset: the stage is running, and configured to change nothing.
        => Assert.Equal(StageState.NoEffect, StageStatus.Curve(CurveDefaults.ResetCurve(Tweaked(type))));

    [Fact]
    public void ResetCurve_LeavesBezier_ReadingOn()
        // Unlike the others, Bezier's default is a real ease-in-out S.
        => Assert.Equal(StageState.On, StageStatus.Curve(CurveDefaults.ResetCurve(Tweaked(CurveType.Bezier))));
}
