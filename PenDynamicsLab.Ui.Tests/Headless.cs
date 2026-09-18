using Avalonia;
using Avalonia.Headless;
using PenDynamicsLab;
using PenDynamicsLab.Ui.Tests;

[assembly: AvaloniaTestApplication(typeof(Headless))]

namespace PenDynamicsLab.Ui.Tests;

/// <summary>
/// A real Avalonia application with no screen attached.
/// </summary>
/// <remarks>
/// <para>
/// Everything in this project needs one. Everything that does not stays in
/// <c>PenDynamicsLab.Tests</c>, which needs no UI framework and is the cheaper place to check
/// anything checkable without a window.
/// </para>
/// <para>
/// <b><c>UseHeadlessDrawing = false</c> is the part that matters.</b> The default headless
/// backend records drawing operations and renders nothing, which would make every claim here a
/// claim about calls rather than about pixels. With it off, Skia rasterises into a real
/// framebuffer and a frame can be read back — so what is asserted is what a reader would have
/// seen.
/// </para>
/// </remarks>
public static class Headless
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHarfBuzz()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
