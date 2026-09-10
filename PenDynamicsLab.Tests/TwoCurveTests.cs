using PenDynamicsLab.Curves;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Two curves in series: curve 1 shapes the pen, curve 2 shapes curve 1's output, and
/// <see cref="CurveMath.ApplyPressureCurve"/> is the pair collapsed into one mapping.
/// </summary>
public class TwoCurveTests
{
    private const double Eps = 1e-9;

    private static CurveSettings Basic(double softness)
        => new() { CurveType = CurveType.Basic, Softness = softness };

    private static CurveSettings Of(CurveType type)
        => new() { CurveType = type };

    // ── Composition ──────────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.6)]
    [InlineData(1.0)]
    public void TheOrderIsCurve1ThenCurve2(double x)
    {
        var p = new PressureCurveParams { Curve1 = Basic(0.5), Curve2 = Of(CurveType.Inverted) };

        double byHand = CurveMath.ApplyCurve(CurveMath.ApplyCurve(x, p.Curve1), p.Curve2);
        Assert.Equal(byHand, CurveMath.ApplyPressureCurve(x, p), Eps);
    }

    [Fact]
    public void TheOrderMatters()
    {
        // Basic then Inverted is not Inverted then Basic, so the numbering is load-bearing
        // rather than cosmetic.
        var oneWay = new PressureCurveParams { Curve1 = Basic(0.5), Curve2 = Of(CurveType.Inverted) };
        var other = new PressureCurveParams { Curve1 = Of(CurveType.Inverted), Curve2 = Basic(0.5) };

        Assert.NotEqual(
            CurveMath.ApplyPressureCurve(0.25, oneWay),
            CurveMath.ApplyPressureCurve(0.25, other),
            3);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    [InlineData(1.0)]
    public void BothPassthrough_IsTheIdentity(double x)
        => Assert.Equal(x, CurveMath.ApplyPressureCurve(x, PressureCurveParams.Default), Eps);

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    [InlineData(1.0)]
    public void ASingleCurveWithCurve2Bypassed_MatchesThatCurveAlone(double x)
    {
        // This is the property the preset migration leans on: an old preset holds one
        // curve, and loading it as curve 1 with curve 2 at Passthrough must not change
        // what it does.
        var only = Basic(0.4);
        var p = new PressureCurveParams { Curve1 = only };

        Assert.Equal(CurveMath.ApplyCurve(x, only), CurveMath.ApplyPressureCurve(x, p), Eps);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void InvertedTwice_CancelsBackToTheIdentity(double x)
    {
        var p = new PressureCurveParams { Curve1 = Of(CurveType.Inverted), Curve2 = Of(CurveType.Inverted) };
        Assert.Equal(x, CurveMath.ApplyPressureCurve(x, p), Eps);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void FlatInPositionOne_MakesCurve2Irrelevant(double x)
    {
        // Curve 2 only ever sees the constant, so its shape cannot show through.
        var p = new PressureCurveParams
        {
            Curve1 = new CurveSettings { CurveType = CurveType.Flat, FlatLevel = 0.5 },
            Curve2 = Basic(0.6),
        };

        double atZero = CurveMath.ApplyPressureCurve(0, p);
        Assert.Equal(atZero, CurveMath.ApplyPressureCurve(x, p), Eps);
    }

    [Fact]
    public void EachCurveKeepsItsOwnSettings()
    {
        // The two used to share one set of fields. Curve 1 being Extended with a narrowed
        // range must not reach into curve 2, which is Basic and ignores ranges entirely.
        var p = new PressureCurveParams
        {
            Curve1 = new CurveSettings
            {
                CurveType = CurveType.Extended,
                InputMinimum = 0.2,
                MinApproach = MinApproach.Cut,
            },
            Curve2 = Basic(0),
        };

        // Curve 1 cuts below its input minimum, so the pair gives zero...
        Assert.Equal(0.0, CurveMath.ApplyPressureCurve(0.1, p), Eps);

        // ...while curve 2 on its own is the identity there. It never saw that range,
        // and Basic ignores the range fields anyway.
        Assert.Equal(0.1, CurveMath.ApplyCurve(0.1, p.Curve2), Eps);
    }

    // ── The effective pill ───────────────────────────────────────

    [Fact]
    public void Effective_IsOff_OnlyWhenBothCurvesAreBypassed()
        => Assert.Equal(StageState.Off, StageStatus.Effective(PressureCurveParams.Default));

    [Fact]
    public void Effective_IsOn_WhenOnlyCurve1Shapes()
        => Assert.Equal(StageState.On, StageStatus.Effective(
            new PressureCurveParams { Curve1 = Basic(0.5) }));

    [Fact]
    public void Effective_IsOn_WhenOnlyCurve2Shapes()
        => Assert.Equal(StageState.On, StageStatus.Effective(
            new PressureCurveParams { Curve2 = Basic(0.5) }));

    [Fact]
    public void Effective_IsNoEffect_WhenOneIsBypassedAndTheOtherIsNeutral()
        => Assert.Equal(StageState.NoEffect, StageStatus.Effective(
            new PressureCurveParams { Curve2 = Basic(0) }));

    [Fact]
    public void Effective_IsNoEffect_WhenBothAreRunningAndNeutral()
        => Assert.Equal(StageState.NoEffect, StageStatus.Effective(
            new PressureCurveParams { Curve1 = Basic(0), Curve2 = Basic(0) }));

    [Fact]
    public void Effective_ReportsOn_ForTwoInvertsThatCancel()
    {
        // Known and deliberate: the pill answers from the settings, and two shaped stages
        // are two shaped stages even when they happen to undo one another. The chart is
        // what shows you they cancelled.
        var p = new PressureCurveParams { Curve1 = Of(CurveType.Inverted), Curve2 = Of(CurveType.Inverted) };

        Assert.Equal(StageState.On, StageStatus.Effective(p));
        for (double x = 0; x <= 1; x += 0.25)
            Assert.Equal(x, CurveMath.ApplyPressureCurve(x, p), Eps);
    }
}
