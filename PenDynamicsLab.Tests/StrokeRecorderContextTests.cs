using Avalonia;
using PenDynamicsLab.Diagnostics;
using WinPenKit;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Pins the decision that a recording carries the context it was captured under, not whatever
/// was true when it was saved.
/// </summary>
/// <remarks>
/// The recorder used to take its metadata as arguments to the save call. Switching the pen API
/// mid-recording then wrote the new session's <see cref="StrokeRecording.MaxPressure"/> over
/// samples scaled to the old device's range, so every replayed pressure came out wrong;
/// changing tab wrote the visible pane's geometry over points drawn on another.
/// </remarks>
public class StrokeRecorderContextTests
{
    private static readonly RecordingContext Wintab =
        new("WintabDigitizer", 8192, 2.25, new Point(100, 200), new Size(800, 600));

    private static readonly RecordingContext Pointer =
        new("AvaloniaPointer", 1024, 1.0, new Point(0, 0), new Size(400, 300));

    private static PenPoint Sample(double x, uint pressure) =>
        new(DesktopX: x, DesktopY: 0, RawX: 0, RawY: 0, Pressure: pressure,
            Azimuth: 0, Altitude: 0, Twist: 0, TiltX: 0, TiltY: 0, Z: 0,
            Status: 0, Buttons: 0, Cursor: PenCursorType.PenTip,
            Source: InputApi.WintabDigitizer);

    private static StrokeRecording SaveAndRead(StrokeRecorder r)
    {
        string? path = r.StopAndSave();
        Assert.NotNull(path);
        try { return StrokeRecording.Load(path!); }
        finally { File.Delete(path!); }
    }

    [Fact]
    public void Recording_carries_the_context_given_at_start()
    {
        var r = new StrokeRecorder();
        r.Start(Wintab);
        r.Add(Sample(10, 4096));

        var saved = SaveAndRead(r);

        Assert.Equal("WintabDigitizer", saved.Api);
        Assert.Equal(8192, saved.MaxPressure);
        Assert.Equal(2.25, saved.RenderScaling);
        Assert.Equal(100, saved.CanvasOriginX);
        Assert.Equal(800, saved.CanvasWidth);
    }

    [Fact]
    public void Starting_again_replaces_the_context_and_drops_the_earlier_samples()
    {
        // The failing case this exists for: samples captured under one device must never be
        // saved under another device's pressure range. A restart is the only way the context
        // changes, and it takes the samples with it.
        var r = new StrokeRecorder();
        r.Start(Wintab);
        r.Add(Sample(10, 4096));
        r.Add(Sample(11, 4097));

        r.Start(Pointer);
        r.Add(Sample(20, 512));

        var saved = SaveAndRead(r);

        Assert.Equal("AvaloniaPointer", saved.Api);
        Assert.Equal(1024, saved.MaxPressure);
        Assert.Single(saved.Points);
        Assert.Equal(20, saved.Points[0].DesktopX);
    }

    [Fact]
    public void Nothing_captured_saves_nothing()
    {
        var r = new StrokeRecorder();
        r.Start(Wintab);

        Assert.Null(r.StopAndSave());
        Assert.False(r.IsRecording);
    }

    [Fact]
    public void Samples_are_ignored_unless_recording()
    {
        var r = new StrokeRecorder();
        r.Add(Sample(10, 4096));

        Assert.Equal(0, r.Count);
    }
}
