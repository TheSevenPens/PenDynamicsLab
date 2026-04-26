using System.Collections.Immutable;

namespace PenDynamicsLab.Curves;

public sealed record PressureCurveParams
{
    public double EmaSmoothing { get; init; } = 0;
    public double PositionEmaSmoothing { get; init; } = 0;
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
