using Avalonia;
using Avalonia.Controls;
using PenDynamicsLab.Drawing;
using WinPenKit.Diagnostics;

namespace PenDynamicsLab.Diagnostics;

/// <summary>
/// The launch-time acceptance checks, run against the live window.
/// </summary>
/// <remarks>
/// <para>The checks and their report format come from <c>WinPenKit.Diagnostics</c>, so this
/// app is measured by exactly the same yardstick as the Scribble samples. Sharing the
/// yardstick is the point: two apps that both claim to be correct should be making the same
/// claim.</para>
/// <para><b>Named <c>--selftest</c>, not <c>--replay</c>.</b> This app already has a
/// <c>--replay</c> that renders a recorded stroke to a PNG, which is a different and older
/// feature. The Scribble samples split levels 0-1 from 2-3 across two flags because their
/// replay needs a recording; here there is no reason to withhold the coordinate checks, so
/// <c>--selftest</c> runs all four levels against the bundled reference stroke.</para>
/// </remarks>
public static class SelfTestCommand
{
    public static bool Requested(string[] args) =>
        Array.Exists(args, a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase));

    /// <summary>The stroke to replay: an explicit path after <c>--selftest</c>, or the
    /// reference recording shipped with WinPenKit.</summary>
    public static string? StrokePath(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--selftest", StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return args[i + 1];
            break;
        }
        return StrokeReplay.FindDefaultRecording();
    }

    /// <summary>
    /// Measures the window and returns the report. Runs after the first layout pass, since
    /// every level 1 check is about a surface that does not exist before then.
    /// </summary>
    /// <param name="window">The live main window.</param>
    /// <param name="surface">The processed canvas surface.</param>
    /// <param name="canvasOriginDip">Canvas offset within the window, in DIPs.</param>
    /// <param name="canvasSizeDip">Canvas size in DIPs.</param>
    /// <param name="strokePath">Recording for the level 2 and 3 checks, or null to skip them.</param>
    public static SelfTest Run(Window window, DrawSurface surface,
                               Point canvasOriginDip, Size canvasSizeDip, string? strokePath)
    {
        var t = new SelfTest { AppName = "PenDynamicsLab" };
        double scale = window.RenderScaling;

        t.CheckDpiAwareness();
        t.CheckWindowPlacement(window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        t.ReportScale(scale);

        // The surface is allowed to be larger than its host - it grows monotonically and never
        // shrinks, so a maximise-then-restore leaves it at the maximum, and the hosts clip. So
        // this is checked against the surface's own DIP size rather than the canvas bounds:
        // what matters is that DipWidth * scale is exactly Width, not that it matches the host.
        t.CheckSurfacePhysical(surface.Width, surface.Height,
                               surface.DipWidth, surface.DipHeight, scale);

        // Where the canvas actually lands, in device pixels.
        var windowOrigin = window.PointToScreen(new Point(0, 0));
        t.CheckSurfaceAlignment(windowOrigin.X + canvasOriginDip.X * scale,
                                windowOrigin.Y + canvasOriginDip.Y * scale);

        // The host is given an explicit DIP size equal to the bitmap's pixel count over the
        // scale, so the bitmap reaches the screen 1:1. That is the arrangement the deliberate
        // 96 dpi tag in DrawSurface exists to preserve, and this is what proves it still holds.
        t.CheckPresentation1To1(surface.Width, surface.Height,
                                surface.DipWidth * scale, surface.DipHeight * scale);

        if (strokePath != null && File.Exists(strokePath))
        {
            double originX = windowOrigin.X + canvasOriginDip.X * scale;
            double originY = windowOrigin.Y + canvasOriginDip.Y * scale;

            var stroke = StrokeReplay.Load(strokePath)
                .CenteredOn(originX, originY,
                            canvasSizeDip.Width * scale, canvasSizeDip.Height * scale);

            // The same two-step conversion a pen point goes through: desktop to client DIPs,
            // then client to canvas-local. Written out here rather than called into, because
            // the real path threads through hit-testing that needs a live pointer - but the
            // arithmetic is the arithmetic, and it is the arithmetic being measured.
            t.CheckReplay(stroke,
                (x, y) => ((x - windowOrigin.X) / scale - canvasOriginDip.X,
                           (y - windowOrigin.Y) / scale - canvasOriginDip.Y),
                scale);
        }
        else
        {
            t.Skip("L2.recording-subpixel",
                   strokePath == null ? "reference recording not found" : $"not found: {strokePath}");
        }

        // Last, because this one moves the window and puts it back.
        //
        // Every check above holds the window still, and none of them can see a canvas origin
        // that is read once and cached: the replay positions its input relative to the origin
        // the application reports and then subtracts the same value back off, so a wrong origin
        // cancels itself exactly, and L1.surface-alignment asks whether the origin is a whole
        // number rather than the right one.
        //
        // The delegate re-reads the window origin on every call, because that is what
        // RenderTimer_Tick does for every pen point. Handing it the windowOrigin captured
        // above would report a stale origin belonging to this file rather than to the input
        // path -- a check that fails for its own reasons measures nothing.
        //
        // canvasOriginDip is not re-read. It is an offset within the window, and the check
        // moves the window without resizing it, so it is the one part of the conversion the
        // move cannot change.
        t.CheckOriginTracksWindow(
            window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
            (x, y) =>
            {
                var origin = window.PointToScreen(new Point(0, 0));
                double dipScale = window.RenderScaling;
                return ((x - origin.X) / dipScale - canvasOriginDip.X,
                        (y - origin.Y) / dipScale - canvasOriginDip.Y);
            },
            scale);

        return t;
    }
}
