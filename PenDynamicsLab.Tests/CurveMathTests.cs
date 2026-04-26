using PenDynamicsLab.Curves;
using Xunit;

namespace PenDynamicsLab.Tests;

public class CurveMathTests
{
    private const double Eps = 1e-9;

    // ── RawCurveOutput: basic / extended (power-law) ─────────────

    [Theory]
    [InlineData(0.0, 0.5, 0.5)]   // softness 0 → exponent 1 → identity
    [InlineData(0.5, 0.25, 0.5)]  // softness +0.5 → exp 0.5 → sqrt(0.25)
    [InlineData(-0.5, 0.5, 0.25)] // softness -0.5 → exp 2 → 0.5^2
    public void RawCurveOutput_Basic_PowerLaw(double softness, double x, double expected)
    {
        var p = PressureCurveParams.Default with { CurveType = CurveType.Basic, Softness = softness };
        Assert.Equal(expected, CurveMath.RawCurveOutput(x, p), Eps);
    }

    [Fact]
    public void RawCurveOutput_RemappedOutputRange()
    {
        // softness 0, x=0.5, output range [0.2, 0.8] → 0.2 + 0.5*0.6 = 0.5
        var p = PressureCurveParams.Default with
        {
            CurveType = CurveType.Basic,
            Softness = 0,
            Minimum = 0.2,
            Maximum = 0.8,
        };
        Assert.Equal(0.5, CurveMath.RawCurveOutput(0.5, p), Eps);
    }

    // ── RawCurveOutput: sigmoid ──────────────────────────────────

    [Fact]
    public void RawCurveOutput_Sigmoid_NearZeroSoftness_IsIdentity()
    {
        // |k| = 0 < 0.01 → curved = xNorm
        var p = PressureCurveParams.Default with { CurveType = CurveType.Sigmoid, Softness = 0 };
        Assert.Equal(0.7, CurveMath.RawCurveOutput(0.7, p), Eps);
    }

    [Fact]
    public void RawCurveOutput_Sigmoid_SymmetricAtMidpoint()
    {
        // Sigmoid is point-symmetric about (0.5, 0.5) → output at 0.5 is 0.5
        var p = PressureCurveParams.Default with { CurveType = CurveType.Sigmoid, Softness = 0.5 };
        Assert.Equal(0.5, CurveMath.RawCurveOutput(0.5, p), 1e-12);
    }

    // ── ApplyPressureCurve: passthrough / flat ───────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(0.37)]
    [InlineData(1)]
    public void Apply_Passthrough_ReturnsInput(double x)
    {
        var p = PressureCurveParams.Default with { CurveType = CurveType.Passthrough };
        Assert.Equal(x, CurveMath.ApplyPressureCurve(x, p), Eps);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.37)]
    [InlineData(1)]
    public void Apply_Flat_ReturnsFlatLevel(double x)
    {
        var p = PressureCurveParams.Default with { CurveType = CurveType.Flat, FlatLevel = 0.42 };
        Assert.Equal(0.42, CurveMath.ApplyPressureCurve(x, p), Eps);
    }

    // ── ApplyPressureCurve: input range remapping ────────────────

    [Fact]
    public void Apply_InputRange_RemapsBeforeCurve()
    {
        // x=0.5 with input range [0.2,0.8] → xNorm = 0.5; basic softness 0 → 0.5
        var p = PressureCurveParams.Default with
        {
            CurveType = CurveType.Basic,
            Softness = 0,
            InputMinimum = 0.2,
            InputMaximum = 0.8,
        };
        Assert.Equal(0.5, CurveMath.ApplyPressureCurve(0.5, p), Eps);
    }

    [Fact]
    public void Apply_MinApproachClamp_BelowInputMin_OutputsMinimum()
    {
        var p = PressureCurveParams.Default with
        {
            CurveType = CurveType.Basic,
            InputMinimum = 0.2,
            Minimum = 0.1,
            MinApproach = MinApproach.Clamp,
        };
        // x < inputMin → xNorm clamped to 0 → softness 0 → 0^1 = 0 → 0.1 + 0*(1-0.1) = 0.1
        Assert.Equal(0.1, CurveMath.ApplyPressureCurve(0.1, p), Eps);
    }

    [Fact]
    public void Apply_MinApproachCut_BelowInputMin_OutputsZero()
    {
        var p = PressureCurveParams.Default with
        {
            CurveType = CurveType.Basic,
            InputMinimum = 0.2,
            Minimum = 0.1,
            MinApproach = MinApproach.Cut,
        };
        Assert.Equal(0.0, CurveMath.ApplyPressureCurve(0.1, p), Eps);
    }

    // ── Bezier ────────────────────────────────────────────────────

    [Fact]
    public void Apply_BezierLinearPreset_IsApproximatelyIdentity()
    {
        var linear = BezierPresets.All.First(p => p.Name == "Linear");
        var p = PressureCurveParams.Default with
        {
            CurveType = CurveType.Bezier,
            BezierPoints = linear.Points,
        };
        // Linear preset has handles colinear with the line → identity (within solver precision)
        for (double x = 0; x <= 1.0; x += 0.05)
            Assert.Equal(x, CurveMath.ApplyPressureCurve(x, p), 1e-7);
    }

    [Fact]
    public void Apply_Bezier_AtEndpoints_ReturnsEndpointY()
    {
        var heavy = BezierPresets.All.First(p => p.Name == "Heavy");
        var p = PressureCurveParams.Default with
        {
            CurveType = CurveType.Bezier,
            BezierPoints = heavy.Points,
        };
        Assert.Equal(0.0, CurveMath.ApplyPressureCurve(0, p), Eps);
        Assert.Equal(1.0, CurveMath.ApplyPressureCurve(1, p), Eps);
    }

    [Fact]
    public void Apply_Bezier_ClampsInputBelowZero()
    {
        var linear = BezierPresets.All.First(p => p.Name == "Linear");
        var p = PressureCurveParams.Default with { CurveType = CurveType.Bezier, BezierPoints = linear.Points };
        Assert.Equal(0.0, CurveMath.ApplyPressureCurve(-0.5, p), Eps);
    }

    [Fact]
    public void Apply_Bezier_ClampsInputAboveOne()
    {
        var linear = BezierPresets.All.First(p => p.Name == "Linear");
        var p = PressureCurveParams.Default with { CurveType = CurveType.Bezier, BezierPoints = linear.Points };
        Assert.Equal(1.0, CurveMath.ApplyPressureCurve(1.5, p), Eps);
    }

    // ── NormalizeBezierPoints ────────────────────────────────────

    [Fact]
    public void NormalizeBezierPoints_Empty_ReturnsDefaultLinearPair()
    {
        var result = CurveMath.NormalizeBezierPoints(Array.Empty<BezierPoint>());
        Assert.Equal(2, result.Length);
        Assert.Equal((0, 0), (result[0].X, result[0].Y));
        Assert.Equal((1, 1), (result[^1].X, result[^1].Y));
    }

    [Fact]
    public void NormalizeBezierPoints_FirstAndLastEndpoints_HaveBrokenHandles()
    {
        var input = new[]
        {
            new BezierPoint(0.2, 0.3, 0.1, 0.3, 0.4, 0.3, HandleMode.Mirrored),
            new BezierPoint(0.8, 0.7, 0.6, 0.7, 0.9, 0.7, HandleMode.Mirrored),
        };
        var result = CurveMath.NormalizeBezierPoints(input);
        // Should prepend (0,...) and append (1,...) since the input doesn't span the full range
        Assert.True(result.Length >= 2);
        Assert.Equal(0, result[0].X);
        Assert.Equal(1, result[^1].X);
        Assert.Equal(HandleMode.Broken, result[0].HandleMode);
        Assert.Equal(HandleMode.Broken, result[^1].HandleMode);
    }

    [Fact]
    public void NormalizeBezierPoints_OutOfOrder_GetsSorted()
    {
        var input = new[]
        {
            new BezierPoint(0.8, 0.9, 0.7, 0.9, 1.0, 0.9, HandleMode.Broken),
            new BezierPoint(0.2, 0.1, 0.0, 0.1, 0.3, 0.1, HandleMode.Broken),
        };
        var result = CurveMath.NormalizeBezierPoints(input);
        for (int i = 1; i < result.Length; i++)
            Assert.True(result[i - 1].X <= result[i].X, $"unsorted at index {i}");
    }

    // ── Bezier presets are well-formed ───────────────────────────

    [Fact]
    public void AllBezierPresets_AreMonotonicOnEvaluation_AtEndpoints()
    {
        foreach (var preset in BezierPresets.All)
        {
            var p = PressureCurveParams.Default with { CurveType = CurveType.Bezier, BezierPoints = preset.Points };
            Assert.Equal(0.0, CurveMath.ApplyPressureCurve(0, p), Eps);
            Assert.Equal(1.0, CurveMath.ApplyPressureCurve(1, p), Eps);
        }
    }
}
