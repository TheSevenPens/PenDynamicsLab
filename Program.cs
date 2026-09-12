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

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
