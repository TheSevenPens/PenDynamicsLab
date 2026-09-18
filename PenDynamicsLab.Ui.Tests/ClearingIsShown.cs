using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using PenDynamicsLab.Drawing;

using SurfaceView = StrokeKit.Avalonia.SurfaceView;

namespace PenDynamicsLab.Ui.Tests;

/// <summary>
/// That clearing the canvas reaches the screen, rather than waiting for the next stroke.
/// </summary>
/// <remarks>
/// <para>
/// The regression this project exists for. Clearing wiped both surfaces and told nothing to
/// repaint, so a reader went on seeing the canvas they had just asked to be rid of until the
/// first mark of the next stroke made it vanish underneath — reported by the person drawing,
/// fixed in <c>#84</c>, and untestable here until now.
/// </para>
/// <para>
/// <b>Every pixel assertion in the other project passed the whole time it was broken.</b> The
/// pixels really were cleared; what was not cleared was the screen. That is the distinction
/// this project can make and that one cannot.
/// </para>
/// </remarks>
public class ClearingIsShown
{
    private const int Side = 120;

    /// <summary>A window with one canvas in it, wired the way the application wires its own.</summary>
    private static (Window Window, SurfaceView View, DrawingSession Session) Open()
    {
        var view = new SurfaceView { DragPans = false, KeepsCentre = false };
        var host = new Border { Width = Side, Height = Side, Child = view };

        var window = new Window { Width = Side, Height = Side, Content = host };

        window.Show();

        var session = new DrawingSession([new CanvasTarget(host, view, CanvasRole.Processed)]);

        session.EnsureAtLeast(CanvasRole.Processed, Side, Side, 1);

        return (window, view, session);
    }

    /// <summary>A stroke across the middle, so there is something to be rid of.</summary>
    private static void Draw(DrawingSession session)
    {
        var brush = BrushSettings.Default with { Size = 12 };

        for (var each = 0; each < 10; each++)
        {
            session.AddSample(new Point(20 + each * 8, 60), 0.8, 0.8, brush);
        }

        session.EndStroke();
        session.PresentDirty();
    }

    /// <summary>How much of a captured frame is not the paper it was cleared to.</summary>
    /// <remarks>
    /// Read off the frame the compositor produced, not off the surface. A surface that has been
    /// wiped says nothing about whether anybody was told.
    /// </remarks>
    private static int MarkedPixels(Window window)
    {
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("the window rendered no frame");

        // The frame's own buffer rather than a PNG round trip. Encoding and decoding would
        // answer the same question and add two more things that could be wrong.
        using var locked = frame.Lock();

        var marked = 0;

        unsafe
        {
            var bytes = (byte*)locked.Address;

            for (var row = 0; row < locked.Size.Height; row++)
            {
                var at = bytes + row * locked.RowBytes;

                for (var column = 0; column < locked.Size.Width; column++)
                {
                    var pixel = at + column * 4;

                    // Ink is near-black and paper near-white, so a dark pixel is a mark
                    // whichever way round the channels are. The fourth byte is alpha.
                    if (pixel[3] > 0 && pixel[0] < 128 && pixel[1] < 128 && pixel[2] < 128)
                    {
                        marked++;
                    }
                }
            }
        }

        return marked;
    }

    [AvaloniaFact]
    public void A_cleared_canvas_is_shown_cleared_without_waiting_for_the_next_stroke()
    {
        var (window, _, session) = Open();

        using (session)
        {
            Draw(session);

            var drawn = MarkedPixels(window);

            Assert.True(drawn > 0, "the stroke should have reached the screen");

            // The whole of the bug: clear, and do nothing else. No further sample, no other
            // call that would present as a side effect.
            session.Clear();

            var after = MarkedPixels(window);

            Assert.True(after == 0,
                $"a cleared canvas should show nothing; {after} pixels of the {drawn} drawn are "
                + "still on screen, which is what a reader saw until their next stroke");
        }

        window.Close();
    }
}
