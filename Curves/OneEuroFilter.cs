namespace PenDynamicsLab.Curves;

/// <summary>
/// The 1€ filter (Casiez, Roussel and Vogel, "1€ Filter: A Simple Speed-based Low-pass Filter
/// for Noisy Input in Interactive Systems", CHI 2012), for one channel.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is.</b> An exponential moving average whose cutoff frequency follows the signal's
/// speed: <c>cutoff = minCutoff + beta × |speed|</c>. Holding pressure steady, the speed is near
/// zero and the cutoff sits at <see cref="MinCutoffDefault"/>, so jitter is smoothed away.
/// Pressing down or lifting off quickly, the cutoff rises and the filter follows closely, so
/// there is little lag. A fixed EMA has to choose one or the other; this is why it exists.
/// </para>
/// <para>
/// <b>It is driven by time, not by sample count.</b> Both the smoothing factor and the speed
/// estimate depend on the interval between samples, so each sample carries its timestamp. Pen
/// rates differ between APIs and devices, and a filter that assumed one would smooth differently
/// on each. When no usable interval is available — the first sample, a missing timestamp, or two
/// samples stamped at the same instant — <see cref="FallbackIntervalSeconds"/> stands in.
/// </para>
/// <para>
/// <b>Units.</b> The signal is normalised pressure, 0 to 1, so the speed is in pressure per
/// second. That is why <see cref="BetaDefault"/> is far larger than the 0.007 the paper suggests
/// for a mouse measured in pixels: the same responsiveness needs a much larger multiplier on a
/// signal whose whole range is 1.
/// </para>
/// </remarks>
public sealed class OneEuroFilter
{
    /// <summary>Smoothing when still, in Hz. Lower is smoother.</summary>
    public const double MinCutoffDefault = 1.0;
    public const double MinCutoffMin = 0.05;
    public const double MinCutoffMax = 10.0;

    /// <summary>How quickly smoothing backs off as pressure changes faster. Higher is less lag.</summary>
    public const double BetaDefault = 5.0;
    public const double BetaMin = 0.0;
    public const double BetaMax = 50.0;

    /// <summary>
    /// Cutoff for the speed estimate itself. Fixed at the paper's recommended 1 Hz: it rarely
    /// needs tuning, and a third slider would be one more thing to explain.
    /// </summary>
    public const double DerivativeCutoff = 1.0;

    /// <summary>The interval assumed when the timestamps don't give one: 200 samples a second.</summary>
    public const double FallbackIntervalSeconds = 1.0 / 200;

    private double? _x;
    private double _dx;
    private long? _lastMicroseconds;

    /// <summary>Forget everything, so the next sample is taken as-is.</summary>
    public void Reset()
    {
        _x = null;
        _dx = 0;
        _lastMicroseconds = null;
    }

    /// <summary>Filters one sample.</summary>
    /// <param name="value">The input.</param>
    /// <param name="timestampMicroseconds">When it was measured, or null if unknown.</param>
    /// <param name="minCutoff">Cutoff when still, in Hz.</param>
    /// <param name="beta">Speed coefficient.</param>
    public double Filter(double value, long? timestampMicroseconds, double minCutoff, double beta)
    {
        double dt = FallbackIntervalSeconds;
        if (timestampMicroseconds is { } now && _lastMicroseconds is { } last && now > last)
            dt = (now - last) / 1_000_000.0;
        if (timestampMicroseconds is { } stamp) _lastMicroseconds = stamp;

        if (_x is not { } previous)
        {
            // The first sample of a stroke passes through unchanged, as the EMA's does: there is
            // nothing to smooth it towards, and seeding at zero would ramp every stroke in.
            _x = value;
            _dx = 0;
            return value;
        }

        double speed = (value - previous) / dt;
        _dx += Alpha(DerivativeCutoff, dt) * (speed - _dx);

        double cutoff = Math.Max(minCutoff, MinCutoffMin) + Math.Max(beta, 0) * Math.Abs(_dx);
        double next = previous + Alpha(cutoff, dt) * (value - previous);
        _x = next;
        return next;
    }

    /// <summary>The smoothing factor for a first-order low-pass at this cutoff and interval.</summary>
    private static double Alpha(double cutoffHz, double dtSeconds)
    {
        double tau = 1.0 / (2 * Math.PI * cutoffHz);
        return 1.0 / (1.0 + tau / dtSeconds);
    }
}
