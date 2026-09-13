using Avalonia;
using PenDynamicsLab.Diagnostics;
using WinPenKit;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Pins the decision that a recording's timing comes from the pen, not from this application.
/// </summary>
/// <remarks>
/// <para>
/// The recorder used to carry one time column, taken from <c>DateTime.UtcNow</c> at the moment a
/// sample was drained. Samples arrive in batches on a render tick, so every point in a batch got
/// nearly the same value and the column was a staircase rather than a per-sample time — the same
/// shape a coarse backend clock produces, except generated here and recorded as though the device
/// had reported it.
/// </para>
/// <para>
/// These tests are built so that the old behaviour fails them. Everything is added in one tight
/// loop with no delays, so a wall-clock column collapses to near-zero spacing, while the pen
/// timestamps below are milliseconds apart. A column that reports the pen's spacing cannot have
/// come from this process's clock.
/// </para>
/// <para>
/// Shares a collection with the other recorder tests so the two classes do not run at the same
/// time. They write into one folder and the file name is a timestamp, so concurrent saves race
/// for a path. That is a fact about this test harness, not about the recorder: the application
/// has one recorder on one thread, and nothing here claims it is safe to share.
/// </para>
/// </remarks>
[Collection("StrokeRecorder files")]
public class StrokeRecorderTimeTests
{
    private static readonly RecordingContext Wintab =
        new("WintabDigitizer", 8192, 2.25, new Point(100, 200), new Size(800, 600), "DeviceTicks");

    /// <summary>A sample whose pen clock reads <paramref name="penUs"/>.</summary>
    private static PenPoint At(long penUs) =>
        new(DesktopX: 0, DesktopY: 0, RawX: 0, RawY: 0, Pressure: 4096,
            Azimuth: 0, Altitude: 0, Twist: 0, TiltX: 0, TiltY: 0, Z: 0,
            Status: 0, Buttons: 0, Cursor: PenCursorType.PenTip,
            Source: InputApi.WintabDigitizer,
            TimestampMicroseconds: penUs);

    private static StrokeRecording SaveAndRead(StrokeRecorder r)
    {
        string? path = r.StopAndSave();
        Assert.NotNull(path);
        try { return StrokeRecording.Load(path!); }
        finally { File.Delete(path!); }
    }

    [Fact]
    public void Pen_time_is_the_pens_own_spacing_not_the_recorders()
    {
        var r = new StrokeRecorder();
        r.Start(Wintab);

        // A 180 Hz device on a millisecond clock: 5, 6, 5, 6 ms apart. Added with no delay, so
        // this process's own clock advances by microseconds across the whole loop.
        r.Add(At(9_000_000));
        r.Add(At(9_005_000));
        r.Add(At(9_011_000));
        r.Add(At(9_016_000));

        var saved = SaveAndRead(r);

        Assert.Equal([0L, 5_000L, 11_000L, 16_000L],
                     saved.Points.Select(p => p.PenTimeUs).ToArray());
    }

    [Fact]
    public void Pen_time_is_relative_to_the_first_sample_so_no_uptime_is_written()
    {
        // Nearly four days of uptime on the pen clock. None of it belongs in the file.
        var r = new StrokeRecorder();
        r.Start(Wintab);
        r.Add(At(330_000_000_000));
        r.Add(At(330_000_005_000));

        var saved = SaveAndRead(r);

        Assert.Equal(0, saved.Points[0].PenTimeUs);
        Assert.Equal(5_000, saved.Points[1].PenTimeUs);
    }

    [Fact]
    public void Pen_time_is_zeroed_on_the_first_sample_not_on_Start()
    {
        // The gap between arming the recorder and the pen touching down is not part of the
        // stroke, and writing it into the file would show as a long flat lead-in.
        var r = new StrokeRecorder();
        r.Start(Wintab);
        r.Add(At(50_000_000));   // first contact, whenever it came
        r.Add(At(50_006_000));

        var saved = SaveAndRead(r);

        Assert.Equal(0, saved.Points[0].PenTimeUs);
        Assert.Equal(6_000, saved.Points[1].PenTimeUs);
    }

    [Fact]
    public void Restarting_rebases_the_pen_clock()
    {
        var r = new StrokeRecorder();
        r.Start(Wintab);
        r.Add(At(1_000_000));

        r.Start(Wintab);           // discards the first stroke
        r.Add(At(77_000_000));
        r.Add(At(77_004_000));

        var saved = SaveAndRead(r);

        Assert.Equal(2, saved.Points.Count);
        Assert.Equal(0, saved.Points[0].PenTimeUs);
        Assert.Equal(4_000, saved.Points[1].PenTimeUs);
    }

    [Fact]
    public void The_clock_behind_the_column_is_named_in_the_file()
    {
        // Resolution differs by four orders of magnitude across backends, so a consumer
        // computing velocity has to be able to tell which clock it is holding.
        var r = new StrokeRecorder();
        r.Start(Wintab);
        r.Add(At(1_000_000));

        Assert.Equal("DeviceTicks", SaveAndRead(r).TimestampSource);
    }

    [Fact]
    public void Pen_duration_reports_the_pens_span_and_not_the_recorders()
    {
        var r = new StrokeRecorder();
        r.Start(Wintab);
        r.Add(At(2_000_000));
        r.Add(At(3_500_000));      // 1.5 s later on the pen's clock

        var saved = SaveAndRead(r);

        Assert.Equal(1.5, saved.PenDuration, precision: 6);

        // The recorder's own column was taken across a loop with no delays, so it saw almost
        // none of that 1.5 s. Naming the two columns apart is the whole point.
        Assert.True(saved.Duration < 0.5,
            $"expected the tick column to be near zero, got {saved.Duration}s");
    }

    [Fact]
    public void Both_columns_open_at_zero_on_the_first_sample()
    {
        // Arming the recorder and then reaching for the pen is normal, and that wait is not part
        // of the stroke. It used to land in the tick column: a recording started 26 seconds
        // before first contact opened at T=26.28 while the pen column opened at 0. The two are
        // only worth keeping side by side if they share an origin.
        var r = new StrokeRecorder();
        r.Start(Wintab);
        Thread.Sleep(60);              // the wait before the pen arrives
        r.Add(At(4_000_000));
        r.Add(At(4_005_000));

        var saved = SaveAndRead(r);

        Assert.Equal(0, saved.Points[0].PenTimeUs);
        Assert.True(saved.Points[0].T < 0.02,
            $"the tick column should open at zero too, got {saved.Points[0].T}s");
    }

    [Fact]
    public void A_recording_written_now_says_it_carries_pen_time()
    {
        // Version 1 files deserialize with PenTimeUs zero throughout, which is indistinguishable
        // from a backend that reported no clock. The version is how a consumer tells those apart.
        var r = new StrokeRecorder();
        r.Start(Wintab);
        r.Add(At(1_000_000));

        Assert.Equal(2, SaveAndRead(r).Version);
    }
}
