using Avalonia;
using PenDynamicsLab.Drawing;
using SkiaSharp;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Pins the order <see cref="IBrushEngine"/> is called in: every mark falls inside a stroke, and
/// strokes do not nest.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RoundBrushEngine"/> satisfies this without trying, because it carries nothing between
/// segments. That is exactly why the protocol needs a test rather than an assumption: the first
/// engine that does carry something — a distance accumulator, an elapsed-time counter — would
/// otherwise discover a missed call site as marks landing in the wrong place, and only on the paths
/// nobody exercised.
/// </para>
/// <para>
/// <see cref="DrawingSession"/> is constructed with no canvas targets and its surfaces sized
/// directly, which is what makes this runnable with no window. The targets are only used for
/// hit-testing and sizing, neither of which these tests need.
/// </para>
/// </remarks>
public class BrushEngineProtocolTests
{
    /// <summary>Records the call order and objects to anything out of place as it happens.</summary>
    private sealed class Spy : IBrushEngine
    {
        public readonly List<string> Calls = [];
        private int _open;

        /// <summary>Marks drawn while no stroke was open.</summary>
        public int Unbracketed { get; private set; }

        /// <summary>Strokes begun while one was already open.</summary>
        public int Nested { get; private set; }

        /// <summary>Ends with no matching begin.</summary>
        public int Unmatched { get; private set; }

        /// <summary>True when every stroke that was begun has been ended.</summary>
        public bool Balanced => _open == 0;

        public int StrokeCount => Calls.Count(c => c == "begin");

        public void BeginStroke()
        {
            if (_open > 0) Nested++;
            _open++;
            Calls.Add("begin");
        }

        public void EndStroke()
        {
            if (_open == 0) Unmatched++;
            else _open--;
            Calls.Add("end");
        }

        public void DrawSegment(SKCanvas canvas, in StrokeSample from, in StrokeSample to,
            BrushSettings brush, SKColor color, PressureChannel channel)
        {
            if (_open == 0) Unbracketed++;
            Calls.Add("draw:" + channel);
        }

        public void Dispose() { }
    }

    private static (DrawingSession Session, Spy Spy) NewSession()
    {
        var spy = new Spy();
        var session = new DrawingSession([], spy);
        session.Processed.EnsureSize(100, 100, 1);
        session.Raw.EnsureSize(100, 100, 1);
        return (session, spy);
    }

    private static void Stroke(DrawingSession s, int samples, double x = 10)
    {
        var brush = BrushSettings.Default;
        for (int i = 0; i < samples; i++)
            s.AddSample(new Point(x + i, 10 + i), 0.5, 0.5, brush);
        s.EndStroke();
    }

    private static void AssertClean(Spy spy)
    {
        Assert.Equal(0, spy.Unbracketed);
        Assert.Equal(0, spy.Nested);
        Assert.Equal(0, spy.Unmatched);
        Assert.True(spy.Balanced, "a stroke was begun and never ended");
    }

    [Fact]
    public void A_live_stroke_is_bracketed()
    {
        var (s, spy) = NewSession();
        Stroke(s, 4);

        AssertClean(spy);
        Assert.Equal(1, spy.StrokeCount);
        Assert.Equal("begin", spy.Calls[0]);
        Assert.Equal("end", spy.Calls[^1]);
        Assert.Contains("draw:Processed", spy.Calls);
        Assert.Contains("draw:Raw", spy.Calls);
    }

    [Fact]
    public void Two_strokes_are_two_brackets()
    {
        var (s, spy) = NewSession();
        Stroke(s, 3, x: 10);
        Stroke(s, 3, x: 50);

        AssertClean(spy);
        Assert.Equal(2, spy.StrokeCount);
    }

    [Fact]
    public void A_hovering_pen_does_not_emit_unmatched_ends()
    {
        // AddSample ends the stroke whenever pressure is zero, and a pen resting above the tablet
        // reports that at the device's full rate. Before the guard, each one was an end with no
        // begin.
        var (s, spy) = NewSession();
        for (int i = 0; i < 20; i++)
            s.AddSample(new Point(10 + i, 10), rawPressure: 0, processedPressure: 0, BrushSettings.Default);

        AssertClean(spy);
        Assert.Equal(0, spy.StrokeCount);
    }

    [Fact]
    public void Undo_replays_each_remaining_stroke_inside_its_own_brackets()
    {
        var (s, spy) = NewSession();
        Stroke(s, 3, x: 10);
        Stroke(s, 3, x: 40);
        Stroke(s, 3, x: 70);

        spy.Calls.Clear();
        Assert.True(s.UndoLastStroke());

        AssertClean(spy);

        // Two strokes survive the undo, and each is replayed on its own.
        Assert.Equal(2, spy.StrokeCount);
        Assert.Equal("begin", spy.Calls[0]);
        Assert.Equal("end", spy.Calls[^1]);
    }

    [Fact]
    public void Resetting_closes_an_open_stroke()
    {
        var (s, spy) = NewSession();
        var brush = BrushSettings.Default;
        s.AddSample(new Point(10, 10), 0.5, 0.5, brush);
        s.AddSample(new Point(11, 11), 0.5, 0.5, brush);

        s.ResetStroke();

        AssertClean(spy);
        Assert.Equal(1, spy.StrokeCount);
    }

    [Fact]
    public void Both_channels_are_drawn_inside_one_pair_of_brackets()
    {
        // One gesture draws both surfaces through the same engine, alternating. They share the
        // stroke; only the per-mark channel separates them. An engine keying its state on the
        // engine rather than the channel would have both streams advancing one accumulator.
        var (s, spy) = NewSession();
        Stroke(s, 3);

        int begins = spy.Calls.Count(c => c == "begin");
        Assert.Equal(1, begins);
        Assert.True(spy.Calls.Count(c => c == "draw:Processed") > 0);
        Assert.True(spy.Calls.Count(c => c == "draw:Raw") > 0);
    }
}
