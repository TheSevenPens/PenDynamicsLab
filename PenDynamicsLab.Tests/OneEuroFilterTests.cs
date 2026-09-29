using PenDynamicsLab.Curves;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// The 1€ filter on its own, and wired into the pipeline as a smoothing type.
/// </summary>
/// <remarks>
/// What distinguishes it from the EMA is the pair of properties checked first: jitter on a
/// steady signal is smoothed hard, while a fast change is followed closely. The rest pins the
/// time handling — it runs on the pen's clock, not on sample count — and the reset contract the
/// EMA already honours.
/// </remarks>
public class OneEuroFilterTests
{
    private const long Step = 5_000; // 200 Hz, in microseconds

    private static PressureCurveParams OneEuro(double minCutoff = OneEuroFilter.MinCutoffDefault,
                                               double beta = OneEuroFilter.BetaDefault) =>
        PressureCurveParams.Default with
        {
            SmoothingType = SmoothingType.OneEuro,
            OneEuroMinCutoff = minCutoff,
            OneEuroBeta = beta,
        };

    [Fact]
    public void TheFirstSamplePassesThroughUnchanged()
    {
        var f = new OneEuroFilter();
        Assert.Equal(0.42, f.Filter(0.42, 0, 1, 5), 12);
    }

    [Fact]
    public void JitterOnSteadyPressureIsSmoothedHard()
    {
        // Pressure held at 0.5, jittering ±0.02 every sample. Steady means low speed, so the
        // cutoff stays near minCutoff and the output barely moves.
        var f = new OneEuroFilter();
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < 400; i++)
        {
            double noisy = 0.5 + (i % 2 == 0 ? 0.02 : -0.02);
            double y = f.Filter(noisy, i * Step, 1, 5);
            if (i > 200) { lo = Math.Min(lo, y); hi = Math.Max(hi, y); }
        }
        Assert.True(hi - lo < 0.01, $"steady output swung {hi - lo:F4}; the input swung 0.04");
    }

    [Fact]
    public void AFastChangeIsFollowedCloselyWhereAFixedSlowFilterWouldLag()
    {
        // A press from 0.1 to 0.9 over 40 ms. With beta 0 the filter is a fixed 1 Hz low-pass and
        // lags far behind; with the default beta it tracks the change.
        double Track(double beta)
        {
            var f = new OneEuroFilter();
            double y = 0;
            for (int i = 0; i <= 8; i++) y = f.Filter(0.1 + 0.8 * i / 8, i * Step, 1, beta);
            return y;
        }

        double adaptive = Track(OneEuroFilter.BetaDefault);
        double fixedSlow = Track(0);

        Assert.True(adaptive > fixedSlow + 0.2, $"adaptive {adaptive:F3} vs fixed {fixedSlow:F3}");
        Assert.True(adaptive > 0.6, $"adaptive only reached {adaptive:F3} of 0.9");
    }

    [Fact]
    public void ItRunsOnTimeNotOnSampleCount()
    {
        // The same step, sampled at 100 Hz and at 400 Hz, should cover about the same ground in
        // the same 50 ms. A per-sample filter would move four times as far at 400 Hz.
        double After50ms(long stepMicros)
        {
            var f = new OneEuroFilter();
            f.Filter(0.2, 0, 1, 0);
            double y = 0.2;
            for (long t = stepMicros; t <= 50_000; t += stepMicros) y = f.Filter(0.8, t, 1, 0);
            return y;
        }

        double slow = After50ms(10_000), fast = After50ms(2_500);
        Assert.True(Math.Abs(slow - fast) < 0.03, $"100 Hz reached {slow:F3}, 400 Hz reached {fast:F3}");
    }

    [Fact]
    public void AMissingOrRepeatedTimestampFallsBackToANominalInterval()
    {
        // Two samples stamped the same (coalesced input) must not divide by zero or freeze.
        var f = new OneEuroFilter();
        f.Filter(0.2, 1_000, 1, 5);
        double y = f.Filter(0.8, 1_000, 1, 5);
        Assert.True(double.IsFinite(y) && y > 0.2 && y < 0.8);

        var g = new OneEuroFilter();
        g.Filter(0.2, null, 1, 5);
        double z = g.Filter(0.8, null, 1, 5);
        Assert.True(double.IsFinite(z) && z > 0.2 && z < 0.8);
    }

    // ── In the pipeline ──────────────────────────────────────────

    [Fact]
    public void ThePipelineUsesItWhenSelected()
    {
        var pipeline = new DynamicsPipeline();
        var p = OneEuro();
        pipeline.Process(0.2, p, SmoothingOrder.SmoothThenCurve, 0);
        var r = pipeline.Process(0.8, p, SmoothingOrder.SmoothThenCurve, Step);

        Assert.True(r.Output > 0.2 && r.Output < 0.8, $"output {r.Output:F3} should be between the two samples");
    }

    [Fact]
    public void LiftingThePenStartsTheNextStrokeFresh()
    {
        // The EMA's reset contract, kept: a zero-pressure sample clears the filter, so the next
        // stroke's first sample is taken as-is rather than dragged towards the last stroke.
        var pipeline = new DynamicsPipeline();
        var p = OneEuro();
        pipeline.Process(1.0, p, SmoothingOrder.SmoothThenCurve, 0);
        pipeline.Process(0.0, p, SmoothingOrder.SmoothThenCurve, Step);
        var next = pipeline.Process(0.2, p, SmoothingOrder.SmoothThenCurve, 2 * Step);

        Assert.Equal(0.2, next.Output, 9);
    }

    [Fact]
    public void ItsSettingsResetToTheirDefaults()
    {
        var p = OneEuro(minCutoff: 7, beta: 30);
        var reset = CurveDefaults.ResetSmoothing(p);

        Assert.Equal(SmoothingType.OneEuro, reset.SmoothingType);
        Assert.Equal(OneEuroFilter.MinCutoffDefault, reset.OneEuroMinCutoff);
        Assert.Equal(OneEuroFilter.BetaDefault, reset.OneEuroBeta);
    }

    [Fact]
    public void ItsStageReadsAsOn()
    {
        Assert.Equal(StageState.On, StageStatus.Smoothing(OneEuro()));
    }
}
