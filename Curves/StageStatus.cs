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
    public static StageState Curve(CurveSettings c)
    {
        if (c.CurveType == CurveType.Passthrough) return StageState.Off;
        return CurveIsNeutral(c) ? StageState.NoEffect : StageState.On;
    }

    /// <summary>The pair taken together, for the effective chart's pill.</summary>
    /// <remarks>
    /// <para>
    /// Off only when both curves are bypassed; On as soon as either one shapes the signal;
    /// otherwise the composition is running and changing nothing.
    /// </para>
    /// <para>
    /// This carries the same honesty as the per-stage rule, and the same known consequence
    /// in a new place: two curves can cancel without either being configured as identity —
    /// Inverted followed by Inverted is exactly the diagonal — and this reports
    /// <see cref="StageState.On"/> for that. Two stages really are shaping the signal; that
    /// they happen to undo one another is what the effective chart is there to show you.
    /// </para>
    /// </remarks>
    public static StageState Effective(PressureCurveParams p)
    {
        var a = Curve(p.Curve1);
        var b = Curve(p.Curve2);

        if (a == StageState.Off && b == StageState.Off) return StageState.Off;
        if (a == StageState.On || b == StageState.On) return StageState.On;
        return StageState.NoEffect;
    }

    public static StageState Smoothing(PressureCurveParams p)
    {
        if (p.SmoothingType == SmoothingType.Passthrough) return StageState.Off;
        return p.EmaSmoothing <= 0 ? StageState.NoEffect : StageState.On;
    }

    /// <summary>
    /// The order only decides anything when smoothing and the curves both alter the
    /// signal; with either side idle, smooth-first and curve-first produce the same result.
    /// </summary>
    public static StageState Processing(PressureCurveParams p)
        => Effective(p) == StageState.On && Smoothing(p) == StageState.On
            ? StageState.On
            : StageState.NoEffect;

    private static bool CurveIsNeutral(CurveSettings p)
    {
        // A constant output always changes something, and so does a reflection. Neither
        // has a setting that could dial it back to identity.
        if (p.CurveType is CurveType.Flat or CurveType.Inverted) return false;

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
