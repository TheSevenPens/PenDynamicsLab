using System.Collections.Immutable;

namespace PenDynamicsLab.Curves;

/// <summary>
/// The whole pressure pipeline's configuration: two curves in series, plus smoothing and
/// the order the two stages run in.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Curve1"/> shapes the pen's pressure and <see cref="Curve2"/> shapes what
/// curve 1 produced — which is why they are numbered. The mapping the brush actually
/// obeys is the composition of the two, and that is what the effective chart draws.
/// </para>
/// <para>
/// A fresh session has quantization off and both curves and smoothing at Passthrough, so
/// nothing is applied and what you draw is the pen's raw behaviour until you opt in.
/// </para>
/// </remarks>
public sealed record PressureCurveParams
{
    /// <summary>
    /// Pressure levels to coarsen the input to before anything else, or 0 for none.
    /// See <see cref="Quantization"/> — this stage is always first and has no order
    /// setting, because nothing downstream can restore detail it has discarded.
    /// </summary>
    public int QuantizationLevels { get; init; }

    public CurveSettings Curve1 { get; init; } = CurveSettings.Default;
    public CurveSettings Curve2 { get; init; } = CurveSettings.Default;

    public SmoothingType SmoothingType { get; init; } = SmoothingType.Passthrough;
    public double EmaSmoothing { get; init; } = 0;
    public SmoothingOrder SmoothingOrder { get; init; } = SmoothingOrder.SmoothThenCurve;

    /// <summary>The default bezier shape, kept here as well because callers reach for it.</summary>
    public static ImmutableArray<BezierPoint> DefaultBezierPoints => CurveSettings.DefaultBezierPoints;

    public static PressureCurveParams Default { get; } = new();
}
