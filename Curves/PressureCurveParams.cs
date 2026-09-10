using System.Collections.Immutable;

namespace PenDynamicsLab.Curves;

public sealed record PressureCurveParams
{
    // Both pipeline stages default to Passthrough, so a fresh session applies nothing and
    // what you draw is the pen's raw behaviour until you opt into a curve or smoothing.
    //
    // Note for any future default change: a preset whose JSON predates a field takes that
    // field's initializer here, so flipping a default silently rewrites how already-saved
    // presets behave.
    public SmoothingType SmoothingType { get; init; } = SmoothingType.Passthrough;
    public double EmaSmoothing { get; init; } = 0;
    public SmoothingOrder SmoothingOrder { get; init; } = SmoothingOrder.SmoothThenCurve;
    public double Softness { get; init; } = 0.0;
    public double InputMinimum { get; init; } = 0;
    public double InputMaximum { get; init; } = 1;
    public double Minimum { get; init; } = 0;
    public double Maximum { get; init; } = 1;
    public CurveType CurveType { get; init; } = CurveType.Passthrough;
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
