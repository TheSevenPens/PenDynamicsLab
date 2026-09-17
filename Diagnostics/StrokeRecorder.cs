// Aliased rather than imported whole: StrokeKit.Strokes has a Stroke and so does
// PenDynamicsLab.Drawing, and they are different things. Deciding which one this
// application means is part of adopting the kit properly, and is not this change.
using Draining = StrokeKit.Strokes.Draining;
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
    private long? _start;
    private long _penOrigin;
    private RecordingContext _context;

    /// <summary>Whether samples are currently being collected.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>How many samples have been collected so far.</summary>
    public int Count => _points.Count;

    /// <summary>Where recordings are written.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PenDynamicsLab", "recordings");

    /// <summary>
    /// Begin collecting, and snapshot the context these samples are being captured under.
    /// </summary>
    /// <remarks>
    /// Taken here rather than at save time, because by then it may describe a different
    /// device or a different canvas. Switching the pen API mid-recording used to write the
    /// new session's <c>MaxPressure</c> over samples scaled to the old device's, so every
    /// replayed pressure came out wrong; changing tab used to write the visible pane's
    /// geometry over points drawn on another.
    /// </remarks>
    public void Start(RecordingContext context)
    {
        _points.Clear();
        _start = null;                // both origins come from the first sample, not from here
        _penOrigin = long.MinValue;
        _context = context;
        IsRecording = true;
    }

    /// <summary>Add one sample, if recording.</summary>
    /// <param name="arrivedUs">
    /// When the batch this sample came over in was taken off the driver's queue, on
    /// <see cref="Draining.Arrival"/>'s clock. <b>One value for a whole batch.</b>
    /// </param>
    /// <remarks>
    /// <para>
    /// Both clocks are zeroed on the first sample that arrives rather than on <see cref="Start"/>.
    /// Arming the recorder and then reaching for the pen used to put that wait into the tick
    /// column -- a recording started 26 seconds before first contact opened at T=26.28 while the
    /// pen column opened at 0. <see cref="StrokeRecording.Duration"/> subtracted it out and so
    /// never showed it, but the two columns could not be compared against each other, which is
    /// the only reason to keep both.
    /// </para>
    /// <para>
    /// <b>The arrival is passed in rather than read here, and that is the point.</b> This used
    /// to call <c>DateTime.UtcNow</c> once per sample, from inside the loop over a drained
    /// batch -- so samples that reached this application in the same poll were stamped
    /// microseconds apart, and what separated them was how long this code took to draw the
    /// preceding ones. The column conflated when the pen reported with what the application
    /// was doing at the time, in an instrument whose subject is timing.
    /// </para>
    /// <para>
    /// Every packet handed over in one call genuinely arrived together. Stamping once makes
    /// "did these two reach the application in the same poll" an equality check rather than an
    /// argument about a spread the delivery did not have.
    /// </para>
    /// <para>
    /// It is also monotonic where a wall clock is not. Both have microsecond resolution on the
    /// machine this was measured on, so that was never the difference; a clock that can step
    /// backwards under NTP is.
    /// </para>
    /// </remarks>
    public void Add(PenPoint pt, long arrivedUs)
    {
        if (!IsRecording) return;

        if (_start is null)
        {
            _start = arrivedUs;
            _penOrigin = pt.TimestampMicroseconds;
        }

        _points.Add(new RecordedPoint(
            T: (arrivedUs - _start.Value) / 1_000_000.0,
            DesktopX: pt.DesktopX,
            DesktopY: pt.DesktopY,
            Pressure: pt.Pressure,
            Azimuth: pt.Azimuth,
            Altitude: pt.Altitude,
            Twist: pt.Twist,
            TiltX: pt.TiltX,
            TiltY: pt.TiltY,
            PenTimeUs: pt.TimestampMicroseconds - _penOrigin));
    }

    /// <summary>
    /// Stop recording and write what was collected, returning the file path, or null if nothing
    /// was captured.
    /// </summary>
    public string? StopAndSave()
    {
        IsRecording = false;
        if (_points.Count == 0) return null;

        var recording = new StrokeRecording
        {
            Api = _context.Api,
            TimestampSource = _context.TimestampSource,
            MaxPressure = _context.MaxPressure,
            RenderScaling = _context.RenderScaling,
            CanvasOriginX = _context.CanvasOriginPhysical.X,
            CanvasOriginY = _context.CanvasOriginPhysical.Y,
            CanvasWidth = _context.CanvasSizeDip.Width,
            CanvasHeight = _context.CanvasSizeDip.Height,
            Points = [.. _points],
        };

        Directory.CreateDirectory(Folder);
        string path = NextFreePath();
        recording.Save(path);

        _points.Clear();
        return path;
    }

    /// <summary>
    /// A path in <see cref="Folder"/> that no file occupies yet.
    /// </summary>
    /// <remarks>
    /// The name used to be the timestamp alone, to the second. Two recordings saved inside the
    /// same second then landed on one path, and the second one overwrote the first with no error
    /// and no sign that anything had been lost — stop, start, stop is quick enough to do by hand.
    /// The suffix only appears when it is needed, so the ordinary case reads as it always did.
    /// </remarks>
    private static string NextFreePath()
    {
        string stem = Path.Combine(Folder, $"stroke-{DateTime.Now:yyyyMMdd-HHmmss}");
        string path = stem + ".json";

        for (int n = 2; File.Exists(path); n++)
            path = $"{stem}-{n}.json";

        return path;
    }
}

/// <summary>
/// What was true when a recording started: which device produced the samples, and where the
/// canvas they were aimed at sat on the desktop.
/// </summary>
/// <remarks>
/// A recording is only replayable against the conditions it was captured under. Carrying them
/// as one value makes it awkward to supply them from the wrong moment, which is the mistake
/// this type exists to prevent.
/// </remarks>
public readonly record struct RecordingContext(
    string Api,
    int MaxPressure,
    double RenderScaling,
    Point CanvasOriginPhysical,
    Size CanvasSizeDip,
    string TimestampSource = "");
