using PenDynamicsLab.Curves;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Covers the live pipeline — the composition that used to be a private method on
/// <c>MainWindow</c> and could not be exercised without starting a window.
/// </summary>
/// <remarks>
/// <c>CurveMath</c> was already pure and well tested. What had no coverage was the stateful
/// part: quantization feeding smoothing, the two orders, and the filter reset that decides
/// whether one stroke can bleed into the next.
/// </remarks>
public class DynamicsPipelineTests
{
    private static PressureCurveParams Ema(double amount, int levels = 0) => PressureCurveParams.Default with
    {
        SmoothingType = SmoothingType.Ema,
        EmaSmoothing = amount,
        QuantizationLevels = levels,
    };

    // ── Quantization feeding the filter ──────────────────────────

    [Fact]
    public void QuantizationRunsBeforeSmoothing()
    {
        // At 2 levels, ceiling quantization sends anything in (0, 0.5] to 0.5. The filter must
        // see the coarsened value, not the original — everything downstream, including what the
        // charts call "raw", sees what the pen effectively gave us.
        var pipeline = new DynamicsPipeline();
        var r = pipeline.Process(0.3, Ema(0.5, levels: 2), SmoothingOrder.SmoothThenCurve);

        Assert.Equal(0.5, r.Raw, 9);
        Assert.Equal(0.5, r.PreCurve, 9);   // first sample seeds the filter, so no blending yet
    }

    [Fact]
    public void QuantizationAndEmaCompose()
    {
        // Two samples that quantize to different levels: the second is blended, and the blend
        // is between the *quantized* values.
        var pipeline = new DynamicsPipeline();
        var p = Ema(0.5, levels: 2);

        pipeline.Process(0.1, p, SmoothingOrder.SmoothThenCurve);           // -> 0.5, seeds
        var second = pipeline.Process(0.9, p, SmoothingOrder.SmoothThenCurve); // -> 1.0, blends

        // alpha = 1 - 0.5; 0.5 + 0.5 * (1.0 - 0.5) = 0.75
        Assert.Equal(0.75, second.PreCurve, 9);
    }

    // ── The two orders ───────────────────────────────────────────

    [Fact]
    public void PreCurveMeansTheSmoothedInputWhenSmoothingRunsFirst()
    {
        var pipeline = new DynamicsPipeline();
        var p = Ema(0.5);

        // Seed with contact, not 0.0 — a zero sample is a lift and clears the filter, so
        // seeding with one would leave nothing to blend against.
        pipeline.Process(0.2, p, SmoothingOrder.SmoothThenCurve);
        var r = pipeline.Process(1.0, p, SmoothingOrder.SmoothThenCurve);

        // 0.2 + 0.5 * (1.0 - 0.2) = 0.6.
        Assert.Equal(0.6, r.PreCurve, 9);
        Assert.NotEqual(r.Raw, r.PreCurve);
    }

    [Fact]
    public void PreCurveMeansTheRawInputWhenTheCurveRunsFirst()
    {
        // The asymmetry the original issue did not call out: in this order there is no distinct
        // value between the stages, so PreCurve carries the raw input instead. Anything reading
        // PreCurve has to accept both meanings, which is exactly why it gets its own test.
        var pipeline = new DynamicsPipeline();
        var p = Ema(0.5);

        pipeline.Process(0.2, p, SmoothingOrder.CurveThenSmooth);
        var r = pipeline.Process(1.0, p, SmoothingOrder.CurveThenSmooth);

        Assert.Equal(r.Raw, r.PreCurve);
        Assert.Equal(1.0, r.PreCurve, 9);
    }

    [Fact]
    public void BothOrdersAgreeWhenNothingIsSmoothing()
    {
        // With no smoothing the order cannot matter. A difference here would mean one of the
        // two paths is applying something extra.
        var a = new DynamicsPipeline().Process(0.4, PressureCurveParams.Default, SmoothingOrder.SmoothThenCurve);
        var b = new DynamicsPipeline().Process(0.4, PressureCurveParams.Default, SmoothingOrder.CurveThenSmooth);

        Assert.Equal(a.Output, b.Output, 9);
    }

    // ── Passthrough short-circuit ────────────────────────────────

    [Fact]
    public void PassthroughIgnoresTheSmoothingAmount()
    {
        // Passthrough takes the same path as an amount of 0, however high the slider is.
        var pipeline = new DynamicsPipeline();
        var p = PressureCurveParams.Default with { SmoothingType = SmoothingType.Passthrough, EmaSmoothing = 0.9 };

        pipeline.Process(0.2, p, SmoothingOrder.SmoothThenCurve);
        var r = pipeline.Process(0.8, p, SmoothingOrder.SmoothThenCurve);

        Assert.Equal(0.8, r.Output, 9);
    }

    [Fact]
    public void PassthroughStillTracksTheInput()
    {
        // The filter keeps following the input while Passthrough is selected, so switching to
        // Ema mid-stroke blends from the current value rather than jumping from a stale one.
        var pipeline = new DynamicsPipeline();
        var off = PressureCurveParams.Default with { SmoothingType = SmoothingType.Passthrough, EmaSmoothing = 0.5 };

        pipeline.Process(1.0, off, SmoothingOrder.SmoothThenCurve);
        var after = pipeline.Process(0.0, Ema(0.5), SmoothingOrder.SmoothThenCurve);

        // Blends from 1.0, not from nothing: 1.0 + 0.5 * (0 - 1.0) = 0.5.
        Assert.Equal(0.5, after.PreCurve, 9);
    }

    // ── Reset ────────────────────────────────────────────────────

    [Fact]
    public void ResetMakesTheNextSampleStartFresh()
    {
        var pipeline = new DynamicsPipeline();
        var p = Ema(0.9);

        pipeline.Process(1.0, p, SmoothingOrder.SmoothThenCurve);
        pipeline.Reset();
        var after = pipeline.Process(0.2, p, SmoothingOrder.SmoothThenCurve);

        // Without the reset a heavily-smoothed filter sitting at 1.0 would drag this up to 0.92.
        Assert.Equal(0.2, after.PreCurve, 9);
    }

    [Fact]
    public void ZeroPressureResetsTheFilterSoTheNextStrokeStartsUnsmoothed()
    {
        // The behaviour change this extraction carries. There is no pen-up event — the drain
        // loop only has the pressure value — so any zero sample ends the stroke.
        var pipeline = new DynamicsPipeline();
        var p = Ema(0.9);

        pipeline.Process(1.0, p, SmoothingOrder.SmoothThenCurve);   // heavy press
        pipeline.Process(0.0, p, SmoothingOrder.SmoothThenCurve);   // lift
        var next = pipeline.Process(0.2, p, SmoothingOrder.SmoothThenCurve); // new stroke

        Assert.Equal(0.2, next.PreCurve, 9);
    }

    [Fact]
    public void WithoutTheLiftTheFilterStillCarriesWithinAStroke()
    {
        // The counterpart: resetting on zero must not mean resetting on every sample. Inside a
        // stroke the filter still does its job.
        var pipeline = new DynamicsPipeline();
        var p = Ema(0.5);

        pipeline.Process(1.0, p, SmoothingOrder.SmoothThenCurve);
        var next = pipeline.Process(0.0 + 0.2, p, SmoothingOrder.SmoothThenCurve);

        // 1.0 + 0.5 * (0.2 - 1.0) = 0.6 — blended, not taken as-is.
        Assert.Equal(0.6, next.PreCurve, 9);
    }

    [Fact]
    public void ZeroPressureIsJudgedOnTheInputNotTheOutput()
    {
        // A Flat curve turns zero pressure into a nonzero output. Contact is what decides
        // whether a stroke is in progress, so the reset keys off the input.
        var pipeline = new DynamicsPipeline();
        var flat = Ema(0.9) with { Curve1 = CurveSettings.Default with { CurveType = CurveType.Flat, FlatLevel = 0.7 } };

        pipeline.Process(1.0, flat, SmoothingOrder.SmoothThenCurve);
        var lift = pipeline.Process(0.0, flat, SmoothingOrder.SmoothThenCurve);
        var next = pipeline.Process(0.2, flat, SmoothingOrder.SmoothThenCurve);

        Assert.True(lift.Output > 0);            // the curve still produces a mark value
        Assert.Equal(0.2, next.PreCurve, 9);     // but the filter was reset anyway
    }

    // ── Channel shape ────────────────────────────────────────────

    [Fact]
    public void TwistIsMarkedAngularSoNobodyGivesItTheLinearBlend()
    {
        // Nothing smooths twist yet. This pins the distinction rather than the feature: a
        // linear EMA between 359 and 1 degrees takes the long way round, so the shape has to
        // know the difference before anything wires it up.
        Assert.True(DynamicsPipeline.IsAngular(PenChannel.Twist));
        Assert.False(DynamicsPipeline.IsAngular(PenChannel.Pressure));
        Assert.False(DynamicsPipeline.IsAngular(PenChannel.Tilt));
    }
}
