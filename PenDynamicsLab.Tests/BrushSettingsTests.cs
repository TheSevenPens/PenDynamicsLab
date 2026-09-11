using PenDynamicsLab.Drawing;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Pins <see cref="BrushSettings"/> — the brush configuration that used to be read on demand
/// off the ribbon's controls.
/// </summary>
/// <remarks>
/// The point of these is the second half of the refactor: a consumer that is not a
/// <c>UserControl</c> has to get the same answers. The size clamp especially — the range used
/// to be enforced only by the slider, and once the record is the source of truth nothing else
/// guards it.
/// </remarks>
public class BrushSettingsTests
{
    [Theory]
    [InlineData(0, BrushSettings.MinSize)]
    [InlineData(-5, BrushSettings.MinSize)]
    [InlineData(1000, BrushSettings.MaxSize)]
    [InlineData(40, 40)]
    public void SizeIsClampedToTheUsableRange(double given, double expected)
    {
        // The slider's Minimum/Maximum no longer guards this: a preset, a replay, or any other
        // headless caller can construct the record directly.
        Assert.Equal(expected, new BrushSettings { Size = given }.Size);
    }

    [Fact]
    public void WithExpressionsAreGuardedToo()
    {
        // Most callers will build one by copying, not by constructing, so a constructor-only
        // check would miss the common path.
        Assert.Equal(BrushSettings.MinSize, (BrushSettings.Default with { Size = -1 }).Size);
        Assert.Equal(BrushSettings.MaxSize, (BrushSettings.Default with { Size = 9999 }).Size);
    }

    [Fact]
    public void NaNFallsBackToTheDefaultRatherThanPropagating()
    {
        // Math.Clamp would hand NaN straight back, and a NaN stroke width draws nothing at all.
        Assert.Equal(BrushSettings.DefaultSize, (BrushSettings.Default with { Size = double.NaN }).Size);
    }

    [Fact]
    public void DefaultsMatchTheRibbonAsShipped()
    {
        var d = BrushSettings.Default;
        Assert.Equal(40, d.Size);
        Assert.Equal(ColorMode.Black, d.ColorMode);
        Assert.Equal(PressureControl.Size, d.PressureDrives);
        Assert.False(d.DrawAtZeroPressure);
    }

    // ── Pressure mapping ─────────────────────────────────────────

    [Fact]
    public void PressureDrivingSizeScalesWidthAndLeavesOpacityAlone()
    {
        var b = BrushSettings.Default with { Size = 100, PressureDrives = PressureControl.Size };

        Assert.Equal(50f, b.StrokeWidthFor(0.5), 4);
        Assert.Equal(1f, b.OpacityFor(0.5), 4);
    }

    [Fact]
    public void PressureDrivingOpacityHoldsWidthAtTheBrushSize()
    {
        var b = BrushSettings.Default with { Size = 100, PressureDrives = PressureControl.Opacity };

        Assert.Equal(100f, b.StrokeWidthFor(0.5), 4);
        Assert.Equal(0.5f, b.OpacityFor(0.5), 4);
    }

    [Fact]
    public void WidthNeverFallsBelowOneDip()
    {
        // A zero-width stroke draws nothing, so the lightest touch would silently skip.
        var b = BrushSettings.Default with { Size = 100, PressureDrives = PressureControl.Size };
        Assert.Equal(1f, b.StrokeWidthFor(0.0), 4);
        Assert.Equal(1f, b.StrokeWidthFor(0.001), 4);
    }

    [Fact]
    public void OpacityNeverFallsToFullyTransparent()
    {
        // Same reasoning: invisible is indistinguishable from not drawing.
        var b = BrushSettings.Default with { PressureDrives = PressureControl.Opacity };
        Assert.Equal(0.02f, b.OpacityFor(0.0), 4);
    }

    [Fact]
    public void TheRecordAloneIsEnoughToDrawWith()
    {
        // The done-when in #12: a second consumer takes the record, not a control. Nothing here
        // touches Avalonia, which is the whole point.
        var b = new BrushSettings { Size = 20, PressureDrives = PressureControl.Size };
        double[] samples = [0.25, 0.5, 1.0];
        float[] widths = [.. samples.Select(b.StrokeWidthFor)];

        Assert.Equal([5f, 10f, 20f], widths);
    }
}
