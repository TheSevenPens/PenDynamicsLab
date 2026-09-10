using System.Collections.Immutable;

namespace PenDynamicsLab.Curves;

/// <summary>
/// Everything that defines one pressure curve. Two of these run in series: curve 1 shapes
/// the pen's pressure, curve 2 shapes what curve 1 produced.
/// </summary>
/// <remarks>
/// <para>
/// These fields used to sit directly on <see cref="PressureCurveParams"/>, one set of
/// them. Splitting them out is what makes a second curve a second field rather than a
/// second copy of nine properties with a <c>2</c> on the end.
/// </para>
/// <para>
/// Note for any future default change: a preset whose JSON predates a field takes that
/// field's initializer here, so flipping a default silently rewrites how already-saved
/// presets behave.
/// </para>
/// <para>
/// One sharp edge: this is a record, so <c>==</c> looks like value equality, but
/// <see cref="ImmutableArray{T}"/> compares by reference. Two settings with identical
/// bezier points — one just deserialized, say — are NOT equal. Compare the points with
/// <c>SequenceEqual</c> when it matters.
/// </para>
/// </remarks>
public sealed record CurveSettings
{
    public CurveType CurveType { get; init; } = CurveType.Passthrough;
    public double Softness { get; init; } = 0.0;
    public double InputMinimum { get; init; } = 0;
    public double InputMaximum { get; init; } = 1;
    public double Minimum { get; init; } = 0;
    public double Maximum { get; init; } = 1;
    public MinApproach MinApproach { get; init; } = MinApproach.Clamp;
    public double FlatLevel { get; init; } = 0.5;
    public ImmutableArray<BezierPoint> BezierPoints { get; init; } = DefaultBezierPoints;

    public static readonly ImmutableArray<BezierPoint> DefaultBezierPoints =
    [
        new BezierPoint(0, 0, 0,    0, 0.33, 0, HandleMode.Broken),
        new BezierPoint(1, 1, 0.67, 1, 1,    1, HandleMode.Broken),
    ];

    public static CurveSettings Default { get; } = new();
}
