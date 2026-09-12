using Avalonia;
using PenDynamicsLab.Diagnostics;

namespace PenDynamicsLab;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Replay renders a recorded stroke to a PNG and exits. It never starts Avalonia: the
        // drawing code is plain SkiaSharp, and needing a window would make the one thing this is
        // for - rendering the same input many ways and comparing - impractical.
        if (args.Contains("--replay")) return ReplayCommand.Run(args);

        // Returned, not discarded. --selftest shuts the lifetime down with the report's exit
        // code, and swallowing it here would make every run look like a pass - which is the
        // one failure mode a self test must not have.
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            // UseWin32().UseSkia() rather than UsePlatformDetect(): that extension method
            // ships in Avalonia.Desktop, which this project no longer references.
            .UseWin32()
            .UseSkia()
            .LogToTrace();
}
