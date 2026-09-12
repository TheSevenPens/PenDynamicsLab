using PenDynamicsLab.Curves;
using PenDynamicsLab.Drawing;

namespace PenDynamicsLab.Diagnostics;

/// <summary>
/// The <c>--replay</c> command line: render a recorded stroke to a PNG without starting the UI.
/// </summary>
/// <remarks>
/// Exists so a question about stroke quality can be asked and answered in a second, repeatedly,
/// against one fixed input. The alternative is launching the app and asking someone to draw the
/// same stroke again, which is neither fast nor the same stroke.
/// </remarks>
public static class ReplayCommand
{
    public const string Usage = """
        Usage: PenDynamicsLab --replay <recording.json> --out <image.png> [options]

          --coords exact|truncated   device position handling (default exact)
          --size <px>                brush size in DIPs (default 40)
          --smoothing <0..1>         EMA amount (default 0, off)
          --quantize <levels>        pressure levels, 0 for none (default 0)
          --drives size|opacity      what pressure controls (default size)
          --scale <factor>           override the recorded display scaling
          --zoom <n>                 nearest-neighbour magnification of the output
          --pos-smooth <0..0.95>     EMA on POSITION, which the app itself has none of
        """;

    public static int Run(string[] args)
    {
        string? input = null, output = null;
        var coords = CoordinateMode.Exact;
        double size = BrushSettings.DefaultSize;
        double smoothing = 0;
        int quantize = 0;
        var drives = PressureControl.Size;
        double? scale = null;
        int zoom = 1;
        double posSmooth = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (a)
            {
                case "--replay": input = Next(); break;
                case "--out": output = Next(); break;
                case "--coords":
                    coords = Next() is "truncated" ? CoordinateMode.TruncateToPixel : CoordinateMode.Exact;
                    break;
                case "--size": size = double.Parse(Next() ?? "40"); break;
                case "--smoothing": smoothing = double.Parse(Next() ?? "0"); break;
                case "--quantize": quantize = int.Parse(Next() ?? "0"); break;
                case "--drives":
                    drives = Next() is "opacity" ? PressureControl.Opacity : PressureControl.Size;
                    break;
                case "--scale": scale = double.Parse(Next() ?? "0"); break;
                case "--zoom": zoom = int.Parse(Next() ?? "1"); break;
                case "--pos-smooth": posSmooth = double.Parse(Next() ?? "0"); break;
            }
        }

        if (input is null || output is null)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        var recording = StrokeRecording.Load(input);

        var curve = PressureCurveParams.Default with
        {
            QuantizationLevels = quantize,
            SmoothingType = smoothing > 0 ? SmoothingType.Ema : SmoothingType.Passthrough,
            EmaSmoothing = smoothing,
        };
        var brush = BrushSettings.Default with { Size = size, PressureDrives = drives };

        RecordingRenderer.RenderToFile(recording, output, curve,
            SmoothingOrder.SmoothThenCurve, brush, coords, scale, zoom, posSmooth);

        Console.WriteLine(
            $"{recording.Points.Count} points, {recording.Duration:F2}s, api={recording.Api}, " +
            $"maxPressure={recording.MaxPressure}, scale={recording.RenderScaling}, " +
            $"canvas={recording.CanvasWidth:F0}x{recording.CanvasHeight:F0} DIP");
        Console.WriteLine($"coords={coords} size={size} smoothing={smoothing} quantize={quantize} -> {output}");
        return 0;
    }
}
