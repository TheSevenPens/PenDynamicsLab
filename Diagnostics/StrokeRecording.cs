using System.Text.Json;
using System.Text.Json.Serialization;

namespace PenDynamicsLab.Diagnostics;

/// <summary>One pen sample, exactly as the device reported it.</summary>
/// <param name="T">Seconds since the first sample in the recording.</param>
/// <param name="DesktopX">Physical screen pixels, at full precision. <b>Not rounded.</b></param>
/// <param name="Pressure">Raw device units, not normalized — <see cref="StrokeRecording.MaxPressure"/> is the scale.</param>
public readonly record struct RecordedPoint(
    double T,
    double DesktopX,
    double DesktopY,
    uint Pressure,
    double Azimuth,
    double Altitude,
    double Twist,
    double TiltX,
    double TiltY);

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
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public string RecordedUtc { get; init; } = DateTime.UtcNow.ToString("O");

    /// <summary>Which input API produced these samples.</summary>
    public string Api { get; init; } = "";

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
}
