using System.Collections.Immutable;

namespace PenDynamicsLab.Curves;

public sealed record PressureCurveParams
{
    // Defaults to Ema, not Passthrough: an amount of 0 is already a no-op, and presets
    // saved before this field existed deserialize to the default — picking Passthrough
    // would silently disable smoothing on any of them that had an amount set.
    public SmoothingType SmoothingType { get; init; } = SmoothingType.Ema;
    public double EmaSmoothing { get; init; } = 0;
    public SmoothingOrder SmoothingOrder { get; init; } = SmoothingOrder.SmoothThenCurve;
    public double Softness { get; init; } = 0.0;
    public double InputMinimum { get; init; } = 0;
    public double InputMaximum { get; init; } = 1;
    public double Minimum { get; init; } = 0;
    public double Maximum { get; init; } = 1;
    public CurveType CurveType { get; init; } = CurveType.Basic;
    public MinApproach MinApproach { get; init; } = MinApproach.Clamp;
    public double FlatLevel { get; init; } = 0.5;
    public ImmutableArray<BezierPoint> BezierPoints { get; init; } = DefaultBezierPoints;

    public static readonly ImmutableArray<BezierPoint> DefaultBezierPoints =
    [
        new BezierPoint(0, 0, 0,    0, 0.33, 0, HandleMode.Broken),
        new BezierPoint(1, 1, 0.67, 1, 1,    1, HandleMode.Broken),
    ];

    public static PressureCurveParams Default { get; } = new();
}
