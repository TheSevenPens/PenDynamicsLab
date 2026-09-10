using PenDynamicsLab.Curves;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Pins the three-state card pills, and the rule underneath them: Basic is the power
/// curve across the full [0, 1] range and ignores the range fields entirely.
/// </summary>
public class StageStatusTests
{
    private const double Eps = 1e-9;

    /// <summary>Range values a user could leave behind by using Extended, or load in a preset.</summary>
    private static CurveSettings WithStaleRanges(CurveType type) => new()
    {
        CurveType = type,
        Softness = 0,
        InputMinimum = 0.2,
        InputMaximum = 0.9,
        Minimum = 0.1,
        Maximum = 0.8,
        MinApproach = MinApproach.Cut,
    };

    // ── Basic ignores the range fields ───────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void Basic_IgnoresRangeFields_SoStaysIdentity(double x)
    {
        var p = WithStaleRanges(CurveType.Basic);
        Assert.Equal(x, CurveMath.ApplyCurve(x, p), Eps);
    }

    [Fact]
    public void Extended_HonoursTheSameRangeFields()
    {
        var p = WithStaleRanges(CurveType.Extended);
        // Cut below the input minimum, and remapped above it — i.e. the fields Basic ignores
        // are doing real work here.
        Assert.Equal(0.0, CurveMath.ApplyCurve(0.1, p), Eps);
        Assert.NotEqual(0.5, CurveMath.ApplyCurve(0.5, p), 3);
    }

    // ── Curve state ──────────────────────────────────────────────

    [Fact]
    public void Curve_Passthrough_IsOff()
        => Assert.Equal(StageState.Off,
            StageStatus.Curve(new CurveSettings { CurveType = CurveType.Passthrough }));

    [Fact]
    public void Curve_BasicAtDefaultAmount_IsOnWithNoEffect()
        => Assert.Equal(StageState.NoEffect,
            StageStatus.Curve(new CurveSettings { CurveType = CurveType.Basic, Softness = 0 }));

    [Fact]
    public void Curve_BasicWithStaleRanges_IsStillOnWithNoEffect()
        => Assert.Equal(StageState.NoEffect, StageStatus.Curve(WithStaleRanges(CurveType.Basic)));

    [Fact]
    public void Curve_BasicWithAmount_IsOn()
        => Assert.Equal(StageState.On,
            StageStatus.Curve(new CurveSettings { CurveType = CurveType.Basic, Softness = 0.5 }));

    [Fact]
    public void Curve_ExtendedAcrossFullRange_IsOnWithNoEffect()
        => Assert.Equal(StageState.NoEffect, StageStatus.Curve(new CurveSettings
        {
            CurveType = CurveType.Extended,
            Softness = 0,
            InputMinimum = 0,
            InputMaximum = 1,
            Minimum = 0,
            Maximum = 1,
        }));

    [Fact]
    public void Curve_ExtendedWithNarrowedRange_IsOn()
        => Assert.Equal(StageState.On, StageStatus.Curve(WithStaleRanges(CurveType.Extended)));

    [Fact]
    public void Curve_SigmoidBelowItsLinearThreshold_IsOnWithNoEffect()
    {
        // The evaluator falls back to a straight line below |Softness * 14| < 0.01, so the
        // pill mirrors that threshold rather than testing for exactly zero.
        double justUnder = 0.0005;
        Assert.True(System.Math.Abs(justUnder * CurveMath.SigmoidSteepness) < CurveMath.SigmoidLinearThreshold);
        Assert.Equal(StageState.NoEffect, StageStatus.Curve(new CurveSettings
        {
            CurveType = CurveType.Sigmoid,
            Softness = justUnder,
            InputMinimum = 0,
            InputMaximum = 1,
            Minimum = 0,
            Maximum = 1,
        }));
    }

    [Fact]
    public void Curve_SigmoidWithSteepness_IsOn()
        => Assert.Equal(StageState.On, StageStatus.Curve(new CurveSettings
        {
            CurveType = CurveType.Sigmoid,
            Softness = 0.5,
        }));

    [Fact]
    public void Curve_Flat_IsAlwaysOn()
    {
        // A constant always changes something, including a constant of zero.
        Assert.Equal(StageState.On, StageStatus.Curve(new CurveSettings
        { CurveType = CurveType.Flat, FlatLevel = 0.5 }));
        Assert.Equal(StageState.On, StageStatus.Curve(new CurveSettings
        { CurveType = CurveType.Flat, FlatLevel = 0 }));
    }

    [Fact]
    public void Curve_LinearBezierPreset_IsOnWithNoEffect()
        => Assert.Equal(StageState.NoEffect, StageStatus.Curve(new CurveSettings
        {
            CurveType = CurveType.Bezier,
            BezierPoints = BezierPresets.All[0].Points,
        }));

    [Fact]
    public void Curve_DefaultBezierPoints_IsOn()
        // The default points are an ease-in-out S: handles at (0.33, 0) and (0.67, 1) are
        // off the diagonal, so the curve genuinely shapes the signal.
        => Assert.Equal(StageState.On, StageStatus.Curve(new CurveSettings
        {
            CurveType = CurveType.Bezier,
            BezierPoints = PressureCurveParams.DefaultBezierPoints,
        }));

    // ── Smoothing state ──────────────────────────────────────────

    [Fact]
    public void Smoothing_Passthrough_IsOff()
        => Assert.Equal(StageState.Off, StageStatus.Smoothing(new PressureCurveParams
        { SmoothingType = SmoothingType.Passthrough, EmaSmoothing = 0.5 }));

    [Fact]
    public void Smoothing_EmaAtZero_IsOnWithNoEffect()
        => Assert.Equal(StageState.NoEffect, StageStatus.Smoothing(new PressureCurveParams
        { SmoothingType = SmoothingType.Ema, EmaSmoothing = 0 }));

    [Fact]
    public void Smoothing_EmaWithAmount_IsOn()
        => Assert.Equal(StageState.On, StageStatus.Smoothing(new PressureCurveParams
        { SmoothingType = SmoothingType.Ema, EmaSmoothing = 0.5 }));

    // ── Processing state ─────────────────────────────────────────

    private static readonly CurveSettings ShapedBasic =
        new() { CurveType = CurveType.Basic, Softness = 0.5 };

    [Fact]
    public void Processing_IsOn_OnlyWhenSmoothingAndTheCurvesBothAlterTheSignal()
        => Assert.Equal(StageState.On, StageStatus.Processing(new PressureCurveParams
        {
            Curve1 = ShapedBasic,
            SmoothingType = SmoothingType.Ema,
            EmaSmoothing = 0.5,
        }));

    [Fact]
    public void Processing_IsMoot_WhenBothCurvesAreOff()
        => Assert.Equal(StageState.NoEffect, StageStatus.Processing(new PressureCurveParams
        {
            SmoothingType = SmoothingType.Ema,
            EmaSmoothing = 0.5,
        }));

    [Fact]
    public void Processing_IsOn_WhenOnlyCurve2Shapes()
        // The order still matters with curve 1 bypassed: smoothing before or after the
        // pair is a real difference.
        => Assert.Equal(StageState.On, StageStatus.Processing(new PressureCurveParams
        {
            Curve2 = ShapedBasic,
            SmoothingType = SmoothingType.Ema,
            EmaSmoothing = 0.5,
        }));

    [Fact]
    public void Processing_IsMoot_WhenSmoothingHasNoEffect()
        => Assert.Equal(StageState.NoEffect, StageStatus.Processing(new PressureCurveParams
        {
            Curve1 = ShapedBasic,
            SmoothingType = SmoothingType.Ema,
            EmaSmoothing = 0,
        }));
}
