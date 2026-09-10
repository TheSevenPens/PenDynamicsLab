using System.Collections.Immutable;

namespace PenDynamicsLab.Curves;

/// <summary>What a pipeline stage is currently doing, shown as a pill in its card header.</summary>
public enum StageState
{
    /// <summary>Bypassed — the stage is set to Passthrough.</summary>
    Off,

    /// <summary>Running, but its settings mean the output equals the input.</summary>
    NoEffect,

    /// <summary>Running and altering the signal.</summary>
    On,
}

/// <summary>
/// Derives each stage's <see cref="StageState"/> from the settings alone.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately structural rather than numerical: an earlier version sampled the mapping
/// and compared against <c>y = x</c> within a tolerance, which meant the pill's claim
/// depended on where you sampled and how tight the tolerance was. "No effect" here means
/// exactly one thing — <em>the settings are configured such that nothing changes</em> —
/// which is checkable, explainable, and cannot drift.
/// </para>
/// <para>
/// The consequence is that a curve which happens to be indistinguishable from identity
/// without being configured as identity reports <see cref="StageState.On"/>. That is the
/// honest answer: the stage is shaped, however slightly.
/// </para>
/// </remarks>
public static class StageStatus
{
    /// <summary>Off when Passthrough; otherwise whether the settings shape the signal.</summary>
    /// <remarks>
    /// Order matters: Passthrough is also neutral, so it has to be answered first or the
    /// Off state would be unreachable.
    /// </remarks>
    public static StageState Curve(PressureCurveParams p)
    {
        if (p.CurveType == CurveType.Passthrough) return StageState.Off;
        return CurveIsNeutral(p) ? StageState.NoEffect : StageState.On;
    }

    public static StageState Smoothing(PressureCurveParams p)
    {
        if (p.SmoothingType == SmoothingType.Passthrough) return StageState.Off;
        return p.EmaSmoothing <= 0 ? StageState.NoEffect : StageState.On;
    }

    /// <summary>
    /// The order only decides anything when both stages actually alter the signal; with
    /// either one idle, smooth-then-curve and curve-then-smooth produce the same result.
    /// </summary>
    public static StageState Processing(PressureCurveParams p)
        => Curve(p) == StageState.On && Smoothing(p) == StageState.On
            ? StageState.On
            : StageState.NoEffect;

    private static bool CurveIsNeutral(PressureCurveParams p)
    {
        // A constant output always changes something.
        if (p.CurveType == CurveType.Flat) return false;

        if (p.CurveType == CurveType.Bezier) return BezierIsDiagonal(p.BezierPoints);

        // Basic / Extended / Sigmoid. Sigmoid degenerates to a straight line below the
        // same threshold the evaluator uses, so mirror that rather than testing for zero
        // and misreporting just below it.
        bool shapeIsFlat = p.CurveType == CurveType.Sigmoid
            ? Math.Abs(p.Softness * CurveMath.SigmoidSteepness) < CurveMath.SigmoidLinearThreshold
            : p.Softness == 0;
        if (!shapeIsFlat) return false;

        // Basic ignores the range fields entirely, so they cannot make it non-neutral.
        if (!CurveMath.UsesRangeControls(p.CurveType)) return true;

        return p.InputMinimum == 0 && p.InputMaximum == 1
            && p.Minimum == 0 && p.Maximum == 1;
    }

    /// <summary>
    /// True when every control point lies on <c>y = x</c>. A cubic segment whose four
    /// control points are all on a line <em>is</em> that line, so this is exact — no
    /// sampling and no tolerance.
    /// </summary>
    private static bool BezierIsDiagonal(ImmutableArray<BezierPoint> points)
    {
        foreach (var pt in CurveMath.NormalizeBezierPoints(points))
        {
            if (pt.X != pt.Y || pt.InX != pt.InY || pt.OutX != pt.OutY) return false;
        }
        return true;
    }
}
