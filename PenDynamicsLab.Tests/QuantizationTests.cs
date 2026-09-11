using PenDynamicsLab.Curves;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Quantization coarsens pressure to N levels by <b>ceiling</b>: zero only when the pen
/// reports zero, and above that exactly N equal-width buckets.
/// </summary>
public class QuantizationTests
{
    private const double Eps = 1e-9;

    // ── The agreed buckets, boundary by boundary ─────────────────

    [Theory]
    // Zero is the only input that gives zero.
    [InlineData(0.0, 0.0)]
    // (0, 0.5] → 0.5, including the edge exactly.
    [InlineData(0.0001, 0.5)]
    [InlineData(0.25, 0.5)]
    [InlineData(0.5, 0.5)]
    // (0.5, 1] → 1, starting immediately above the edge.
    [InlineData(0.5001, 1.0)]
    [InlineData(0.75, 1.0)]
    [InlineData(1.0, 1.0)]
    public void TwoLevels(double x, double expected)
        => Assert.Equal(expected, Quantization.Apply(x, 2), Eps);

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.0001, 0.25)]
    [InlineData(0.25, 0.25)]
    [InlineData(0.2501, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.5001, 0.75)]
    [InlineData(0.75, 0.75)]
    [InlineData(0.7501, 1.0)]
    [InlineData(1.0, 1.0)]
    public void FourLevels(double x, double expected)
        => Assert.Equal(expected, Quantization.Apply(x, 4), Eps);

    // ── The properties that make it "N levels" ───────────────────

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(8192)]
    public void ProducesExactlyNPlusOneDistinctValues(int levels)
    {
        var seen = new HashSet<double>();
        for (int i = 0; i <= 100_000; i++)
            seen.Add(Quantization.Apply(i / 100_000.0, levels));

        Assert.Equal(levels + 1, seen.Count);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(16)]
    [InlineData(1024)]
    public void EveryBucketEdgeMapsToItself(int levels)
    {
        // k/N is the top of bucket k, so ceiling must leave it alone rather than push it
        // to the next step. This is the case floating point threatens: x * levels can
        // land a hair above an integer.
        for (int k = 1; k <= levels; k++)
        {
            double edge = k / (double)levels;
            Assert.Equal(edge, Quantization.Apply(edge, levels), Eps);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(64)]
    public void AnyContactAtAllRegistersAtTheLowestLevel(int levels)
    {
        // The defining property of ceiling, and the reason it was chosen: there is no
        // band of light pressure that reads as no pressure.
        Assert.Equal(1.0 / levels, Quantization.Apply(1e-12, levels), Eps);
        Assert.Equal(0.0, Quantization.Apply(0, levels), Eps);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8192)]
    public void NeverLeavesTheUnitRange(int levels)
    {
        Assert.Equal(0.0, Quantization.Apply(-0.5, levels), Eps);
        Assert.Equal(1.0, Quantization.Apply(1.5, levels), Eps);
    }

    // ── Passthrough ──────────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    [InlineData(1.0)]
    public void LevelZero_PassesTheValueThrough(double x)
        => Assert.Equal(x, Quantization.Apply(x, 0), Eps);

    [Fact]
    public void PassthroughIsTheFirstLevelOffered_AndIsNotActive()
    {
        Assert.Equal(0, Quantization.Levels[0]);
        Assert.False(Quantization.IsActive(0));
        Assert.Equal("Passthrough", Quantization.Format(0));
    }

    [Fact]
    public void TheOfferedLevelsRunFrom8192DownToTwo()
    {
        Assert.Equal(
            new[] { 0, 8192, 4096, 2048, 1024, 512, 256, 128, 64, 32, 16, 8, 4, 2 },
            Quantization.Levels);

        // Every non-zero level is a power of two, which is what makes them read as tablet
        // resolutions rather than arbitrary numbers.
        foreach (int level in Quantization.Levels.Skip(1))
            Assert.Equal(0, level & (level - 1));
    }

    [Fact]
    public void ADefaultSessionDoesNotQuantize()
        => Assert.Equal(0, PressureCurveParams.Default.QuantizationLevels);
}
