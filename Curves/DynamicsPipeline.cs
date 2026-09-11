namespace PenDynamicsLab.Curves;

/// <summary>
/// The pen channels this app can filter. Pressure is the only one wired to the mark today;
/// the rest already arrive on every <c>PenPoint</c> and are shown in the telemetry ribbon.
/// </summary>
/// <remarks>
/// Listing them is not speculation — it is what keeps the filter state from being a single
/// field again. The distinction that matters is <see cref="IsAngular"/>: twist wraps at
/// 360°, so the linear EMA below would take the long way round between 359° and 1°.
/// </remarks>
public enum PenChannel
{
    /// <summary>Pen tip pressure, normalised to 0-1.</summary>
    Pressure,

    /// <summary>Planar tilt. Linear, does not wrap.</summary>
    Tilt,

    /// <summary>Barrel rotation in degrees. <b>Angular</b> — wraps at 360.</summary>
    Twist,
}

/// <summary>
/// The live pen pipeline: quantize, then smoothing and the curves in the configured order.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not called <c>PressurePipeline</c>. The composition it runs — coarsen, filter,
/// shape — is the same one tilt and twist will need, and those are not hypothetical: they
/// already arrive on <c>PenPoint</c> and already render to the ribbon. A type named and shaped
/// around one axis would have to be reopened by the first feature that maps a second one, and
/// by then the drawing session and the stroke model will both be built on its signature.
/// </para>
/// <para>
/// So filter state is held per channel rather than in one field, and <see cref="Reset"/>
/// clears all of it. Pressure is the only channel with a caller today; that is a wiring
/// question, not a shape question.
/// </para>
/// <para>
/// This type knows nothing about rendering. No <c>RenderScaling</c>, no Skia, no Avalonia —
/// callers still work in DIPs and this works in normalised values, which is what lets the
/// whole composition be tested without starting a UI.
/// </para>
/// </remarks>
public sealed class DynamicsPipeline
{
    private static readonly int ChannelCount = Enum.GetValues<PenChannel>().Length;

    /// <summary>Per-channel EMA state. Null means "no previous sample" — start fresh.</summary>
    private readonly double?[] _filter = new double?[ChannelCount];

    /// <summary>The pressure channel's three published values.</summary>
    /// <param name="Raw">Pressure after quantization. What the charts call "raw".</param>
    /// <param name="PreCurve">
    /// The value the curve-1 chart highlights. This means different things in the two orders —
    /// see <see cref="Process"/> — and that asymmetry is deliberate.
    /// </param>
    /// <param name="Output">The pipeline's final value, which drives the mark.</param>
    public readonly record struct PressureResult(double Raw, double PreCurve, double Output);

    /// <summary>Whether a channel is an angle that wraps, rather than a plain magnitude.</summary>
    public static bool IsAngular(PenChannel channel) => channel == PenChannel.Twist;

    /// <summary>
    /// Clear every channel's filter state, so the next sample is taken as-is rather than
    /// blended with whatever came before.
    /// </summary>
    /// <remarks>
    /// Callers reset on canvas crossing, tab change, session start, Clear, and true
    /// out-of-range. Zero-pressure samples are handled inside <see cref="Process"/>, because
    /// the state being cleared belongs to this type rather than to the window.
    /// </remarks>
    public void Reset() => Array.Clear(_filter);

    /// <summary>
    /// Run one pressure sample through the pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Quantization is first and unconditional: it models the resolution the pressure arrived
    /// at, and no later stage can restore detail it has discarded. Everything downstream —
    /// including the value the charts show as "raw" — sees the coarsened signal, because that
    /// is what the pen effectively gave us.
    /// </para>
    /// <para>
    /// <b>The two orders publish different <c>PreCurve</c> values, on purpose.</b> Smooth-then-curve
    /// has a distinct value between the stages, so the chart highlights the smoothed input.
    /// Curve-then-smooth does not — smoothing happens after the curve — so it highlights the
    /// raw input instead. Anything reading <c>PreCurve</c> has to accept both meanings.
    /// </para>
    /// </remarks>
    public PressureResult Process(double rawInput, PressureCurveParams p, SmoothingOrder order)
    {
        double raw = Quantization.Apply(rawInput, p.QuantizationLevels);

        // Passthrough short-circuits to the same path as an amount of 0: no smoothing, and the
        // filter still tracks the input so switching back mid-stroke doesn't jump from a stale
        // value.
        double amount = p.SmoothingType == SmoothingType.Passthrough
            ? 0
            : Math.Clamp(p.EmaSmoothing, 0, EmaConstants.Max);

        PressureResult result;
        if (order == SmoothingOrder.CurveThenSmooth)
        {
            double curved = CurveMath.ApplyPressureCurve(raw, p);
            double smoothed = Smooth(PenChannel.Pressure, curved, amount);
            result = new PressureResult(Raw: raw, PreCurve: raw, Output: smoothed);
        }
        else
        {
            double smoothed = Smooth(PenChannel.Pressure, raw, amount);
            double curved = CurveMath.ApplyPressureCurve(smoothed, p);
            result = new PressureResult(Raw: raw, PreCurve: smoothed, Output: curved);
        }

        // A sample with no pressure means the pen is not making a mark, so the next one starts
        // a new stroke and must not inherit this one's filter state — carried-over smoothing
        // produces artifacts an artist notices and cannot explain.
        //
        // Keyed off the *input*, not the output: with a Flat curve or a raised output minimum,
        // zero pressure can still produce a nonzero output, and it is contact that decides
        // whether a stroke is in progress.
        //
        // Clearing after the sample rather than before is what makes the next stroke start
        // unsmoothed. Clearing first would seed the filter at zero, and the first real sample
        // would be dragged down toward it — a slow ramp-in, which is the artifact inverted
        // rather than removed.
        if (raw <= 0) Reset();

        return result;
    }

    /// <summary>Exponential moving average for one channel, holding that channel's state.</summary>
    private double Smooth(PenChannel channel, double value, double amount)
    {
        if (IsAngular(channel))
        {
            // Not reachable today - no angular channel has a caller. Left as a tripwire rather
            // than silently applying the linear blend below, which would swing a nib the long
            // way round between 359 and 1 degrees.
            throw new NotSupportedException(
                $"{channel} is angular; a linear EMA wraps incorrectly. Implement angular smoothing before wiring it.");
        }

        int i = (int)channel;
        if (amount <= 0) { _filter[i] = value; return value; }
        if (_filter[i] is not { } prev) { _filter[i] = value; return value; }

        double alpha = 1 - amount;
        double next = prev + alpha * (value - prev);
        _filter[i] = next;
        return next;
    }
}
