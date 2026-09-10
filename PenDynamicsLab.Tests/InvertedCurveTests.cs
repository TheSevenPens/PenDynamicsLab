using PenDynamicsLab.Curves;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// The Inverted curve: output = 1 - input, with no settings of its own.
/// </summary>
public class InvertedCurveTests
{
    private const double Eps = 1e-9;

    private static CurveSettings Inverted => new() { CurveType = CurveType.Inverted };

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.25, 0.75)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.75, 0.25)]
    [InlineData(1.0, 0.0)]
    public void Inverts(double x, double expected)
        => Assert.Equal(expected, CurveMath.ApplyCurve(x, Inverted), Eps);

    [Theory]
    [InlineData(-0.5, 1.0)]
    [InlineData(1.5, 0.0)]
    public void ClampsInputBeforeInverting(double x, double expected)
        => Assert.Equal(expected, CurveMath.ApplyCurve(x, Inverted), Eps);

    [Fact]
    public void IgnoresEveryOtherCurveField()
    {
        // Softness, the ranges, min approach and the flat level all belong to other types.
        // Inverted shares the record with them and must not pick any of them up.
        var loaded = new CurveSettings
        {
            CurveType = CurveType.Inverted,
            Softness = 0.7,
            InputMinimum = 0.2,
            InputMaximum = 0.9,
            Minimum = 0.1,
            Maximum = 0.8,
            MinApproach = MinApproach.Cut,
            FlatLevel = 0.25,
        };
        for (double x = 0; x <= 1.0; x += 0.1)
            Assert.Equal(1 - x, CurveMath.ApplyCurve(x, loaded), Eps);
    }

    [Fact]
    public void DoesNotUseRangeControls()
        => Assert.False(CurveMath.UsesRangeControls(CurveType.Inverted));

    [Fact]
    public void IsAlwaysOn_BecauseNoSettingCanFlattenIt()
        => Assert.Equal(StageState.On, StageStatus.Curve(Inverted));

    [Fact]
    public void HasNoSettingsToReset()
        => Assert.False(CurveDefaults.CurveHasSettings(CurveType.Inverted));

    [Fact]
    public void ResetLeavesItUntouched()
    {
        var p = Inverted with { Softness = 0.7, BezierPoints = BezierPresets.All[2].Points };
        Assert.Equal(p, CurveDefaults.ResetCurve(p));
    }

    [Fact]
    public void SitsBetweenExtendedAndSigmoid()
    {
        // The combo is populated by iterating the enum and selected by casting to int, so
        // this ordering is the UI ordering — not merely cosmetic.
        Assert.Equal((int)CurveType.Extended + 1, (int)CurveType.Inverted);
        Assert.Equal((int)CurveType.Inverted + 1, (int)CurveType.Sigmoid);
    }
}
