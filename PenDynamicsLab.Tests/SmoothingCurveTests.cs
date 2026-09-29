using PenDynamicsLab.Curves;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// The smoothing curve: an EMA whose amount is read off a curve at the pen's pressure.
/// </summary>
/// <remarks>
/// The properties that matter are that the default smooths light pressure more than firm
/// pressure, that the amount follows the incoming pressure rather than the smoothed or curved
/// value, and that it keeps the EMA's reset-on-lift contract.
/// </remarks>
public class SmoothingCurveTests
{
    private static PressureCurveParams Curve(CurveSettings? curve = null) =>
        PressureCurveParams.Default with
        {
            SmoothingType = SmoothingType.Curve,
            SmoothingCurve = curve ?? SmoothingCurve.Default,
        };

    [Fact]
    public void TheDefaultFallsFromHeavySmoothingAtLightPressureToNoneAtFull()
    {
        var d = SmoothingCurve.Default;
        Assert.Equal(0.9, SmoothingCurve.AmountFor(0, d), 9);
        Assert.Equal(0.45, SmoothingCurve.AmountFor(0.5, d), 9);
        Assert.Equal(0, SmoothingCurve.AmountFor(1, d), 9);
    }

    [Fact]
    public void TheAmountIsHeldFlatOutsideTheInputRange()
    {
        var c = SmoothingCurve.Default with { InputMinimum = 0.2, InputMaximum = 0.8 };
        Assert.Equal(0.9, SmoothingCurve.AmountFor(0.1, c), 9);
        Assert.Equal(0, SmoothingCurve.AmountFor(0.9, c), 9);
        Assert.Equal(0.45, SmoothingCurve.AmountFor(0.5, c), 9);
    }

    [Fact]
    public void TheAmountNeverExceedsWhatTheEmaAccepts()
        => Assert.Equal(EmaConstants.Max, SmoothingCurve.AmountFor(0, SmoothingCurve.Default with { Minimum = 1 }), 9);

    [Fact]
    public void SoftnessBendsTheCurveBetweenTheEnds()
    {
        var bent = SmoothingCurve.Default with { Softness = 0.5 };
        double mid = SmoothingCurve.AmountFor(0.5, bent);
        Assert.NotEqual(0.45, mid, 3);
        Assert.Equal(0.9, SmoothingCurve.AmountFor(0, bent), 9);
        Assert.Equal(0, SmoothingCurve.AmountFor(1, bent), 9);
    }

    [Fact]
    public void AStrayCurveTypeIsReadAsExtended()
    {
        var sigmoid = SmoothingCurve.Default with { CurveType = CurveType.Passthrough };
        Assert.Equal(0.9, SmoothingCurve.AmountFor(0, sigmoid), 9);
    }

    // ── In the pipeline ──────────────────────────────────────────

    /// <summary>How far one step moves from <paramref name="from"/> towards <paramref name="to"/>.</summary>
    private static double StepFraction(double from, double to, PressureCurveParams p)
    {
        var pipeline = new DynamicsPipeline();
        pipeline.Process(from, p, SmoothingOrder.SmoothThenCurve);
        var r = pipeline.Process(to, p, SmoothingOrder.SmoothThenCurve);
        return (r.PreCurve - from) / (to - from);
    }

    [Fact]
    public void LightPressureIsSmoothedMoreThanFirmPressure()
    {
        // The same small step, near the light end and near the firm end. At light pressure the
        // amount is high, so the output moves only a little of the way.
        var p = Curve();
        double light = StepFraction(0.10, 0.12, p);
        double firm = StepFraction(0.88, 0.90, p);

        Assert.Equal(1 - SmoothingCurve.AmountFor(0.12, p.SmoothingCurve), light, 9);
        Assert.Equal(1 - SmoothingCurve.AmountFor(0.90, p.SmoothingCurve), firm, 9);
        Assert.True(light < firm, $"light moved {light:F3} of the step, firm {firm:F3}");
    }

    [Fact]
    public void TheAmountFollowsTheIncomingPressureEvenWhenTheCurveComesFirst()
    {
        // The pen holds 0.1 while curve 1 jumps from a flat 0.5 to a flat 1. If the smoothing
        // curve read the curved value it would see full pressure and not smooth; it must read the
        // pen's 0.1 and smooth heavily.
        Func<double, PressureCurveParams> flat = level =>
            Curve() with { Curve1 = CurveSettings.Default with { CurveType = CurveType.Flat, FlatLevel = level } };
        var p = flat(1);

        var pipeline = new DynamicsPipeline();
        pipeline.Process(0.1, flat(0.5), SmoothingOrder.CurveThenSmooth);
        var r = pipeline.Process(0.1, p, SmoothingOrder.CurveThenSmooth);

        double expectedAmount = SmoothingCurve.AmountFor(0.1, p.SmoothingCurve);
        Assert.Equal(0.5 + (1 - expectedAmount) * (1 - 0.5), r.Output, 9);
    }

    [Fact]
    public void LiftingThePenStartsTheNextStrokeFresh()
    {
        var pipeline = new DynamicsPipeline();
        var p = Curve();
        pipeline.Process(1.0, p, SmoothingOrder.SmoothThenCurve);
        pipeline.Process(0.0, p, SmoothingOrder.SmoothThenCurve);
        var next = pipeline.Process(0.2, p, SmoothingOrder.SmoothThenCurve);

        Assert.Equal(0.2, next.PreCurve, 9);
    }

    [Fact]
    public void ItsSettingsResetToTheirDefaults()
    {
        var p = Curve(SmoothingCurve.Default with { Minimum = 0.2, Maximum = 0.7, Softness = -0.4 });
        var reset = CurveDefaults.ResetSmoothing(p);

        Assert.Equal(SmoothingType.Curve, reset.SmoothingType);
        Assert.Equal(SmoothingCurve.Default.Minimum, reset.SmoothingCurve.Minimum);
        Assert.Equal(SmoothingCurve.Default.Maximum, reset.SmoothingCurve.Maximum);
        Assert.Equal(SmoothingCurve.Default.Softness, reset.SmoothingCurve.Softness);
    }

    [Fact]
    public void ItsStageIsOnUnlessBothEndsAreZero()
    {
        Assert.Equal(StageState.On, StageStatus.Smoothing(Curve()));
        Assert.Equal(StageState.NoEffect,
            StageStatus.Smoothing(Curve(SmoothingCurve.Default with { Minimum = 0, Maximum = 0 })));
    }
}
