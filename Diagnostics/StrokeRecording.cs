using System.Text.Json;
using System.Text.Json.Serialization;

namespace PenDynamicsLab.Diagnostics;

/// <summary>One pen sample, exactly as the device reported it.</summary>
/// <param name="T">
/// Seconds since the first sample, taken from this application's clock when the sample was
/// drained. <b>Not the pen's own time</b> — see <paramref name="PenTimeUs"/>.
/// </param>
/// <param name="DesktopX">Physical screen pixels, at full precision. <b>Not rounded.</b></param>
/// <param name="Pressure">Raw device units, not normalized — <see cref="StrokeRecording.MaxPressure"/> is the scale.</param>
/// <param name="PenTimeUs">
/// Microseconds since the first sample, on the clock the pen backend supplied. Zero throughout
/// when <see cref="StrokeRecording.TimestampSource"/> is <c>None</c>, which means the backend
/// reported no clock rather than that no time passed.
/// </param>
/// <remarks>
/// <para>
/// <b>Two clocks, on purpose.</b> Samples are drained in batches on a render tick, so every point
/// in a batch gets nearly the same <paramref name="T"/> — the column is a staircase, not a
/// per-sample time. It is kept because it describes when this application got to the sample,
/// which is worth knowing; it is simply not what the pen reported.
/// </para>
/// <para>
/// Both are relative to the first sample, so a recording carries no machine uptime. That means
/// their difference measures how much the drain delay <i>varied</i> across the recording, not its
/// absolute size — the first sample's delay is subtracted out of both by construction.
/// </para>
/// </remarks>
public readonly record struct RecordedPoint(
    double T,
    double DesktopX,
    double DesktopY,
    uint Pressure,
    double Azimuth,
    double Altitude,
    double Twist,
    double TiltX,
    double TiltY,
    long PenTimeUs = 0);

/// <summary>
/// A captured stroke, stored as raw device input rather than as a drawn mark.
/// </summary>
/// <remarks>
/// <para>
/// The point of this type is that <b>nothing here has been through the pipeline</b>. Pressure is
/// in device units, positions are the untouched doubles the driver reported, and no curve,
/// smoothing or brush setting has been applied. A recording can therefore be replayed under
/// settings it was never drawn under, which is what makes "does this look wrong because of the
/// curve, the smoothing, or the renderer?" a question with an answer.
/// </para>
/// <para>
/// It also decouples investigation from hardware. Wintab cannot be driven by synthetic input, so
/// without recordings every question about stroke quality costs a round trip to someone holding a
/// pen.
/// </para>
/// <para>
/// The geometry fields are what let a replay reconstruct canvas coordinates itself, at whatever
/// precision it likes — including deliberately reproducing a precision loss to see what it costs.
/// </para>
/// </remarks>
public sealed record StrokeRecording
{
    /// <summary>
    /// 2 added <see cref="RecordedPoint.PenTimeUs"/> and <see cref="TimestampSource"/>.
    /// </summary>
    /// <remarks>
    /// Version 1 files deserialize with <c>PenTimeUs</c> zero throughout and no timestamp source,
    /// which is indistinguishable from a backend that reported no clock. Reading the version is
    /// how a consumer tells "this file predates pen time" from "this device had none" — the two
    /// mean different things and the zero cannot carry both.
    /// </remarks>
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;

    public string RecordedUtc { get; init; } = DateTime.UtcNow.ToString("O");

    /// <summary>Which input API produced these samples.</summary>
    public string Api { get; init; } = "";

    /// <summary>
    /// The clock behind <see cref="RecordedPoint.PenTimeUs"/>, as
    /// <c>PenConventions.Timestamp</c> named it when recording started.
    /// </summary>
    /// <remarks>
    /// Recorded because resolution differs by four orders of magnitude across backends, and the
    /// name is what says which. Measured on hardware in WinPenKit: WM_POINTER and WinUI resolve
    /// to a microsecond, Avalonia and Wintab to a millisecond with one stamp per point, WPF gives
    /// about three points one stamp, and Qt steps by 15.6 ms. A consumer computing velocity needs
    /// to know which of those it is holding.
    /// </remarks>
    public string TimestampSource { get; init; } = "";

    /// <summary>Full-scale pressure in device units, the denominator for <see cref="RecordedPoint.Pressure"/>.</summary>
    public int MaxPressure { get; init; }

    /// <summary>Display scaling in force when this was recorded — 1.75 on a 168 dpi monitor.</summary>
    public double RenderScaling { get; init; } = 1;

    /// <summary>The drawing canvas's top-left corner, in physical screen pixels.</summary>
    public double CanvasOriginX { get; init; }

    /// <summary>The drawing canvas's top-left corner, in physical screen pixels.</summary>
    public double CanvasOriginY { get; init; }

    /// <summary>Canvas size in DIPs, so a replay can size a surface the way the app did.</summary>
    public double CanvasWidth { get; init; }

    /// <summary>Canvas size in DIPs.</summary>
    public double CanvasHeight { get; init; }

    public List<RecordedPoint> Points { get; init; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public void Save(string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    public static StrokeRecording Load(string path)
        => JsonSerializer.Deserialize<StrokeRecording>(File.ReadAllText(path), Json)
           ?? throw new InvalidDataException($"Not a stroke recording: {path}");

    /// <summary>Seconds from the first sample to the last, or 0 for a recording with fewer than two.</summary>
    public double Duration => Points.Count < 2 ? 0 : Points[^1].T - Points[0].T;

    /// <summary>
    /// The same span on the pen's clock, or 0 when there is none. Prefer this to
    /// <see cref="Duration"/> for anything about how fast the pen moved.
    /// </summary>
    public double PenDuration => Points.Count < 2
        ? 0
        : (Points[^1].PenTimeUs - Points[0].PenTimeUs) / 1_000_000.0;
}
