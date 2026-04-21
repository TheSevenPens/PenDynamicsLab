using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PenDynamicsLab.Curves;
using PenDynamicsLab.Persistence;

namespace PenDynamicsLab.Controls;

/// <summary>
/// Renders a pen pressure-response dataset (physical grams-force vs logical pressure %).
/// When <see cref="Params"/> + <see cref="ShowCurveEffect"/> are set the y axis becomes
/// the curve's output % so users can see how their curve transforms the hardware response.
/// Mirrors WebPressureExplorer's PressureResponseChart.svelte.
/// </summary>
public sealed class PressureResponseChartControl : Control
{
    private const double PadLeft = 42;
    private const double PadRight = 20;
    private const double PadTop = 20;
    private const double PadBottom = 32;
    private const double XLabelSpacing = 8;
    private const double YLabelSpacing = 8;
    private const double XAxisLabelSpacing = 2;
    private const double YAxisLabelSpacing = 7;

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly IBrush PlotBrush = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xFB));
    private static readonly IBrush LabelBrush = Brushes.Black;
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xF4)), 1);
    private static readonly IPen ResponsePen = new Pen(Brushes.Black, 1.5);
    private static readonly IBrush ResponseDotBrush = Brushes.Black;
    private static readonly Typeface MonoTypeface = new("Consolas, monospace");
    private static readonly Typeface SansTypeface = new("Segoe UI");

    private static readonly IBrush EffectiveDotBrush = new SolidColorBrush(Color.FromRgb(0x14, 0xA0, 0x50));
    private static readonly IPen EffectiveGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x14, 0xA0, 0x50)), 1)
    { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
    private static readonly IBrush RawDotBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x33, 0xCC));
    private static readonly IPen RawGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x88, 0x33, 0xCC)), 1)
    { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };

    public static readonly StyledProperty<PressureResponseData?> DataProperty =
        AvaloniaProperty.Register<PressureResponseChartControl, PressureResponseData?>(nameof(Data));

    public PressureResponseData? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly StyledProperty<PressureCurveParams?> ParamsProperty =
        AvaloniaProperty.Register<PressureResponseChartControl, PressureCurveParams?>(nameof(Params));

    public PressureCurveParams? Params
    {
        get => GetValue(ParamsProperty);
        set => SetValue(ParamsProperty, value);
    }

    public static readonly StyledProperty<bool> ShowCurveEffectProperty =
        AvaloniaProperty.Register<PressureResponseChartControl, bool>(nameof(ShowCurveEffect), defaultValue: true);

    public bool ShowCurveEffect
    {
        get => GetValue(ShowCurveEffectProperty);
        set => SetValue(ShowCurveEffectProperty, value);
    }

    public static readonly StyledProperty<double?> LiveRawPressureProperty =
        AvaloniaProperty.Register<PressureResponseChartControl, double?>(nameof(LiveRawPressure));

    public double? LiveRawPressure
    {
        get => GetValue(LiveRawPressureProperty);
        set => SetValue(LiveRawPressureProperty, value);
    }

    public static readonly StyledProperty<double?> LivePressureProperty =
        AvaloniaProperty.Register<PressureResponseChartControl, double?>(nameof(LivePressure));

    public double? LivePressure
    {
        get => GetValue(LivePressureProperty);
        set => SetValue(LivePressureProperty, value);
    }

    static PressureResponseChartControl()
    {
        AffectsRender<PressureResponseChartControl>(
            DataProperty, ParamsProperty, ShowCurveEffectProperty,
            LiveRawPressureProperty, LivePressureProperty);
    }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double height = Bounds.Height;
        double plotW = width - PadLeft - PadRight;
        double plotH = height - PadTop - PadBottom;
        if (plotW <= 0 || plotH <= 0) return;

        context.FillRectangle(BackgroundBrush, new Rect(0, 0, width, height));
        context.FillRectangle(PlotBrush, new Rect(PadLeft, PadTop, plotW, plotH));

        for (int i = 0; i <= 4; i++)
        {
            double gx = PadLeft + i / 4.0 * plotW;
            double gy = PadTop + i / 4.0 * plotH;
            context.DrawLine(GridPen, new Point(gx, PadTop), new Point(gx, PadTop + plotH));
            context.DrawLine(GridPen, new Point(PadLeft, gy), new Point(PadLeft + plotW, gy));
        }

        var records = Data?.Records;
        double maxGf = records is { Count: > 0 } ? records.Max(r => r.Gf) : 1;
        bool useCurve = ShowCurveEffect && Params is not null;

        DrawLabels(context, width, height, plotW, plotH, maxGf, useCurve);

        if (records == null || records.Count == 0 || maxGf <= 0) return;

        // Connecting line.
        var figure = new PathFigure { IsClosed = false, Segments = new PathSegments() };
        for (int i = 0; i < records.Count; i++)
        {
            var pt = ToCanvas(records[i], maxGf, plotW, plotH);
            if (i == 0) figure.StartPoint = pt;
            else figure.Segments.Add(new LineSegment { Point = pt });
        }
        context.DrawGeometry(null, ResponsePen, new PathGeometry { Figures = new PathFigures { figure } });

        // Data points.
        foreach (var rec in records)
        {
            var pt = ToCanvas(rec, maxGf, plotW, plotH);
            context.DrawEllipse(ResponseDotBrush, null, pt, 1.5, 1.5);
        }

        // Live indicators — projected onto the response curve at their y value.
        if (LiveRawPressure is { } raw)
            DrawLiveIndicator(context, raw, records, maxGf, plotW, plotH, RawDotBrush, RawGuidePen);
        if (LivePressure is { } eff)
            DrawLiveIndicator(context, eff, records, maxGf, plotW, plotH, EffectiveDotBrush, EffectiveGuidePen);
    }

    private Point ToCanvas(ResponseRecord rec, double maxGf, double plotW, double plotH)
    {
        double yNorm = ApplyEffect(rec.LogicalPercent / 100.0);
        double cx = PadLeft + rec.Gf / maxGf * plotW;
        double cy = PadTop + plotH - yNorm * plotH;
        return new Point(cx, cy);
    }

    private double ApplyEffect(double logical)
    {
        if (ShowCurveEffect && Params is { } p) return CurveMath.ApplyPressureCurve(logical, p);
        return logical;
    }

    private void DrawLiveIndicator(DrawingContext context, double pressure, IReadOnlyList<ResponseRecord> records,
        double maxGf, double plotW, double plotH, IBrush dotBrush, IPen guidePen)
    {
        // Project the live pressure (already in 0..1 logical) up through the same vertical axis
        // the response data uses — so the dot lands on the displayed response trace.
        double yNorm = ApplyEffect(pressure);
        double cx = FindCxForY(yNorm, records, maxGf, plotW, plotH);
        double cy = PadTop + plotH - yNorm * plotH;

        context.DrawLine(guidePen, new Point(PadLeft, cy), new Point(cx, cy));
        context.DrawLine(guidePen, new Point(cx, cy), new Point(cx, PadTop + plotH));
        context.DrawEllipse(dotBrush, null, new Point(cx, cy), 3, 3);
    }

    private double FindCxForY(double yNorm, IReadOnlyList<ResponseRecord> records, double maxGf, double plotW, double plotH)
    {
        for (int i = 0; i < records.Count - 1; i++)
        {
            double y0 = ApplyEffect(records[i].LogicalPercent / 100.0);
            double y1 = ApplyEffect(records[i + 1].LogicalPercent / 100.0);
            double minY = Math.Min(y0, y1);
            double maxY = Math.Max(y0, y1);
            if (yNorm >= minY && yNorm <= maxY)
            {
                double t = y1 != y0 ? (yNorm - y0) / (y1 - y0) : 0;
                double gf = records[i].Gf + t * (records[i + 1].Gf - records[i].Gf);
                return PadLeft + gf / maxGf * plotW;
            }
        }
        double firstY = ApplyEffect(records[0].LogicalPercent / 100.0);
        double lastY = ApplyEffect(records[^1].LogicalPercent / 100.0);
        bool nearFirst = Math.Abs(yNorm - firstY) <= Math.Abs(yNorm - lastY);
        return nearFirst
            ? PadLeft + records[0].Gf / maxGf * plotW
            : PadLeft + records[^1].Gf / maxGf * plotW;
    }

    private void DrawLabels(DrawingContext context, double width, double height, double plotW, double plotH,
        double maxGf, bool useCurve)
    {
        for (int i = 0; i <= 4; i++)
        {
            double gx = Math.Round(PadLeft + i / 4.0 * plotW);
            double gfValue = i / 4.0 * maxGf;
            string label = gfValue == Math.Floor(gfValue)
                ? gfValue.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                : gfValue.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            var ft = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, MonoTypeface, 10, LabelBrush);
            context.DrawText(ft, new Point(gx - ft.Width / 2, Math.Round(PadTop + plotH + XLabelSpacing)));
        }
        for (int i = 0; i <= 4; i++)
        {
            double gy = Math.Round(PadTop + plotH - i / 4.0 * plotH);
            string label = (i * 25).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            var ft = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, MonoTypeface, 10, LabelBrush);
            context.DrawText(ft, new Point(Math.Round(PadLeft - YLabelSpacing) - ft.Width, gy - ft.Height / 2));
        }
        var xAxis = new FormattedText("PHYSICAL (gf)", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, SansTypeface, 10, LabelBrush);
        context.DrawText(xAxis,
            new Point(Math.Round(PadLeft + plotW / 2 - xAxis.Width / 2),
                     Math.Round(height - XAxisLabelSpacing - xAxis.Height)));

        var yAxis = new FormattedText(useCurve ? "OUTPUT %" : "LOGICAL %",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, SansTypeface, 10, LabelBrush);
        var yAxisOrigin = new Point(Math.Round(YAxisLabelSpacing), Math.Round(PadTop + plotH / 2));
        using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2) *
                                     Matrix.CreateTranslation(yAxisOrigin.X, yAxisOrigin.Y)))
        {
            context.DrawText(yAxis, new Point(-yAxis.Width / 2, 0));
        }
    }
}
