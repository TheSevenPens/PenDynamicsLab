using Avalonia;
using WinPenKit;

namespace PenDynamicsLab.Diagnostics;

/// <summary>
/// Collects raw pen samples into a <see cref="StrokeRecording"/> while recording is on.
/// </summary>
/// <remarks>
/// <para>
/// Takes <see cref="PenPoint"/> as the device delivered it, before the app has converted,
/// truncated, curved or smoothed anything. That is the whole value: a recording made while one
/// set of settings was in force can be replayed under any other.
/// </para>
/// <para>
/// Records hover samples too, not only samples with pressure. Where a stroke begins and ends is
/// something a replay should be able to decide for itself, and the approach to the canvas is
/// sometimes exactly the part that looks wrong.
/// </para>
/// </remarks>
public sealed class StrokeRecorder
{
    private readonly List<RecordedPoint> _points = [];
    private DateTime _start;

    /// <summary>Whether samples are currently being collected.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>How many samples have been collected so far.</summary>
    public int Count => _points.Count;

    /// <summary>Where recordings are written.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PenDynamicsLab", "recordings");

    public void Start()
    {
        _points.Clear();
        _start = DateTime.UtcNow;
        IsRecording = true;
    }

    /// <summary>Add one sample, if recording.</summary>
    public void Add(PenPoint pt)
    {
        if (!IsRecording) return;

        _points.Add(new RecordedPoint(
            T: (DateTime.UtcNow - _start).TotalSeconds,
            DesktopX: pt.DesktopX,
            DesktopY: pt.DesktopY,
            Pressure: pt.Pressure,
            Azimuth: pt.Azimuth,
            Altitude: pt.Altitude,
            Twist: pt.Twist,
            TiltX: pt.TiltX,
            TiltY: pt.TiltY));
    }

    /// <summary>
    /// Stop recording and write what was collected, returning the file path, or null if nothing
    /// was captured.
    /// </summary>
    public string? StopAndSave(string api, int maxPressure, double renderScaling,
        Point canvasOriginPhysical, Size canvasSizeDip)
    {
        IsRecording = false;
        if (_points.Count == 0) return null;

        var recording = new StrokeRecording
        {
            Api = api,
            MaxPressure = maxPressure,
            RenderScaling = renderScaling,
            CanvasOriginX = canvasOriginPhysical.X,
            CanvasOriginY = canvasOriginPhysical.Y,
            CanvasWidth = canvasSizeDip.Width,
            CanvasHeight = canvasSizeDip.Height,
            Points = [.. _points],
        };

        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, $"stroke-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        recording.Save(path);

        _points.Clear();
        return path;
    }
}
