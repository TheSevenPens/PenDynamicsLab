namespace PenDynamicsLab.Curves;

/// <summary>
/// Restores one pipeline stage to the starting values for the type it is currently set to,
/// without changing the type itself.
/// </summary>
/// <remarks>
/// <para>
/// This splits two jobs that used to share one button: the dropdown chooses <em>which</em>
/// curve or smoothing you want (Passthrough included), and reset restores <em>this</em>
/// one. Reset used to drag the type back to Passthrough with it, so there was no way to
/// undo a tweak without also leaving the curve you were working on.
/// </para>
/// <para>
/// The values come from <see cref="PressureCurveParams.Default"/>, so "the initial Basic
/// curve" means exactly what a fresh session gives you after picking Basic.
/// </para>
/// <para>
/// Only the fields the current type actually uses are touched. Every curve type shares the
/// one record, so a blanket reset would silently discard a Bezier you had shaped while you
/// were sitting in Basic, where none of it is even on screen.
/// </para>
/// </remarks>
public static class CurveDefaults
{
    /// <summary>
    /// The curve settings for <paramref name="p"/>'s current type, restored to their
    /// defaults. Passthrough has nothing on screen to restore, so it comes back unchanged.
    /// </summary>
    public static PressureCurveParams ResetCurve(PressureCurveParams p)
    {
        var d = PressureCurveParams.Default;

        var reset = p.CurveType switch
        {
            CurveType.Basic or CurveType.Extended or CurveType.Sigmoid
                => p with { Softness = d.Softness },
            CurveType.Flat => p with { FlatLevel = d.FlatLevel },
            CurveType.Bezier => p with { BezierPoints = d.BezierPoints },
            _ => p,
        };

        // The range fields are shared, but only the types CurveMath.UsesRangeControls names
        // show or honour them — so for the others they are somebody else's settings.
        return CurveMath.UsesRangeControls(p.CurveType)
            ? reset with
            {
                InputMinimum = d.InputMinimum,
                InputMaximum = d.InputMaximum,
                Minimum = d.Minimum,
                Maximum = d.Maximum,
                MinApproach = d.MinApproach,
            }
            : reset;
    }

    /// <summary>
    /// The smoothing amount restored to its default, leaving the smoothing type alone.
    /// </summary>
    public static PressureCurveParams ResetSmoothing(PressureCurveParams p)
        => p.SmoothingType == SmoothingType.Passthrough
            ? p
            : p with { EmaSmoothing = PressureCurveParams.Default.EmaSmoothing };
}
