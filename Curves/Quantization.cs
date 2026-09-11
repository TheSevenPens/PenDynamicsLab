namespace PenDynamicsLab.Curves;

/// <summary>
/// Coarsens pressure into a fixed number of levels, the way a lower-resolution tablet
/// would report it. The first stage of the pipeline, before smoothing and the curves.
/// </summary>
/// <remarks>
/// <para>
/// A level of <c>N</c> yields <c>N + 1</c> possible values — <c>0, 1/N, 2/N … 1</c> —
/// reached by <b>ceiling</b>: zero only when the pen genuinely reports zero, and above
/// that exactly <c>N</c> equal-width buckets. At two levels, <c>(0, 0.5]</c> gives 0.5
/// and <c>(0.5, 1]</c> gives 1.
/// </para>
/// <para>
/// Ceiling rather than nearest or floor, deliberately. Nearest turns the bottom
/// <c>1/(2N)</c> of the range into zero, so at low levels a light touch makes no mark at
/// all. Floor makes the top bucket a single point — full pressure only at exactly the
/// pen's maximum, which never happens in practice. Ceiling gives the <c>N</c> equal
/// nonzero buckets the level number promises, at the cost of having no soft entry: the
/// faintest contact registers at <c>1/N</c>.
/// </para>
/// <para>
/// Unlike the curves and smoothing, this stage has no configurable order. It models the
/// resolution the pressure arrived at, and nothing downstream can restore detail it has
/// already discarded — so it can only be first.
/// </para>
/// </remarks>
public static class Quantization
{
    /// <summary>Level values offered in the UI, coarsest last. 0 means no quantization.</summary>
    /// <remarks>
    /// The upper end mirrors real tablet pressure resolutions, which is what makes the
    /// setting legible: picking 1024 on an 8192-level pen shows what that pen would feel
    /// like. The bottom three are below anything real hardware does and exist because
    /// that is where the effect becomes unmistakable on screen.
    /// </remarks>
    public static readonly int[] Levels =
        [0, 8192, 4096, 2048, 1024, 512, 256, 128, 64, 32, 16, 8, 4, 2];

    /// <summary>
    /// Tolerance for the ceiling, in units of the scaled value.
    /// </summary>
    /// <remarks>
    /// Without it, a value that should sit exactly on a bucket edge but lands a hair above
    /// it in floating point — <c>3.0000000000000004</c> rather than <c>3</c> — would be
    /// pushed a whole bucket up. At 8192 levels one bucket is 0.0001 wide, so an epsilon
    /// this small cannot swallow a real one.
    /// </remarks>
    private const double Epsilon = 1e-9;

    /// <summary>
    /// Quantizes <paramref name="x"/> to <paramref name="levels"/> steps. A level of 0 or
    /// less passes the value through untouched.
    /// </summary>
    public static double Apply(double x, int levels)
    {
        if (levels <= 0) return x;

        x = Math.Clamp(x, 0, 1);
        if (x <= 0) return 0;

        double step = Math.Ceiling(x * levels - Epsilon);
        return Math.Clamp(step, 1, levels) / levels;
    }

    /// <summary>Whether this level actually coarsens anything.</summary>
    public static bool IsActive(int levels) => levels > 0;

    /// <summary>The dropdown label for a level.</summary>
    public static string Format(int levels) => levels <= 0 ? "Passthrough" : levels.ToString();
}
