namespace PenDynamicsLab.Curves;

/// <summary>
/// The smoothing curve: an EMA whose amount depends on the pen's pressure, looked up per sample.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> Jitter is most visible in light strokes, and lag most costly in firm
/// ones. A single EMA amount has to trade one against the other everywhere; a curve can smooth
/// light touches heavily and leave firm pressure responsive. By default it starts high at zero
/// pressure and falls to nothing at full pressure.
/// </para>
/// <para>
/// <b>It is an Extended curve, read differently.</b> The shape is exactly
/// <see cref="CurveType.Extended"/>'s — softness, an input range, an output range — so the same
/// math and the same chart edit it. Only the output means something else: a smoothing amount
/// rather than a pressure. With the output's minimum above its maximum the curve descends, which
/// is the whole of the "upside down" requirement; nothing is mirrored. <see cref="CurveSettings.Minimum"/>
/// is the smoothing at and below the input minimum (light pressure), and
/// <see cref="CurveSettings.Maximum"/> the smoothing at and above the input maximum (firm
/// pressure). The type is forced to Extended when evaluated, so a stray type in a preset cannot
/// turn it into something the controls do not describe.
/// </para>
/// <para>
/// <b>Driven by the incoming pressure</b>, after quantization — never by the smoothed value,
/// which would let the smoothing change the pressure that decides the smoothing. In
/// curve-then-smooth order it still reads the pen's pressure, not the curved one.
/// </para>
/// </remarks>
public static class SmoothingCurve
{
    /// <summary>Heavy smoothing at light pressure, falling linearly to none at full pressure.</summary>
    public static CurveSettings Default { get; } = new()
    {
        CurveType = CurveType.Extended,
        Softness = 0,
        InputMinimum = 0,
        InputMaximum = 1,
        Minimum = 0.9,
        Maximum = 0,
    };

    /// <summary>The EMA amount for a pressure, clamped to what the EMA accepts.</summary>
    public static double AmountFor(double pressure, CurveSettings curve)
    {
        var extended = curve with { CurveType = CurveType.Extended, MinApproach = MinApproach.Clamp };
        double amount = CurveMath.ApplyCurve(Math.Clamp(pressure, 0, 1), extended);
        return Math.Clamp(amount, 0, EmaConstants.Max);
    }

    /// <summary>Whether the curve smooths nowhere: both ends at zero.</summary>
    public static bool IsNeutral(CurveSettings curve) => curve.Minimum <= 0 && curve.Maximum <= 0;
}
