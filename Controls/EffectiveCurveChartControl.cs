using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PenDynamicsLab.Curves;
using PenDynamicsLab.Theming;

namespace PenDynamicsLab.Controls;

/// <summary>
/// Draws the mapping the brush actually obeys: curve 1 with curve 2 applied over its
/// output, sampled across [0, 1].
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <b>not</b> a mode of <see cref="PressureChartControl"/>. That control is
/// an editor — hit-testing, drag dispatch, a bezier context menu, draggable range nodes —
/// and none of it applies here. A composition cannot be dragged: there is no single answer
/// to which of the two curves a moved point should change. Keeping them apart means the
/// read-only chart carries no interaction code that has to be conditionally switched off,
/// which is how a "read-only" mode ends up editable by accident.
/// </para>
/// <para>
/// It is drawn to look derived rather than authored: the accent trace instead of the
/// curves' black, and the identity diagonal behind it so lighter-or-heavier-than-the-pen
/// reads without arithmetic.
/// </para>
/// </remarks>
public sealed class EffectiveCurveChartControl : Control
{
    // Matches PressureChartControl exactly: the two sit in the same column and any
    // difference in plot geometry would read as a drawing error rather than a choice.
    private const double Pad = 8;

    private IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private IBrush PlotBrush = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xFB));
    private IPen GridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xF4)), 1);
    private IPen IdentityPen = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xD8)), 1)
    { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
    private IPen CurvePen = new Pen(new SolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBD)), 2.5);

    // Live indicators, same colours and meaning as the editable charts.
    private static readonly IBrush EffectiveDotBrush = new SolidColorBrush(Color.FromRgb(0x14, 0xA0, 0x50));
    private static readonly IBrush RawDotBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x33, 0xCC));

    public static readonly StyledProperty<PressureCurveParams> ParamsProperty =
        AvaloniaProperty.Register<EffectiveCurveChartControl, PressureCurveParams>(
            nameof(Params), defaultValue: PressureCurveParams.Default);

    /// <summary>Pressure entering curve 1, or null when the pen is away.</summary>
    public static readonly StyledProperty<double?> LivePressureProperty =
        AvaloniaProperty.Register<EffectiveCurveChartControl, double?>(nameof(LivePressure));

    /// <summary>Raw pen pressure, before smoothing, or null when the pen is away.</summary>
    public static readonly StyledProperty<double?> LiveRawPressureProperty =
        AvaloniaProperty.Register<EffectiveCurveChartControl, double?>(nameof(LiveRawPressure));

    public PressureCurveParams Params
    {
        get => GetValue(ParamsProperty);
        set => SetValue(ParamsProperty, value);
    }

    public double? LivePressure
    {
        get => GetValue(LivePressureProperty);
        set => SetValue(LivePressureProperty, value);
    }

    public double? LiveRawPressure
    {
        get => GetValue(LiveRawPressureProperty);
        set => SetValue(LiveRawPressureProperty, value);
    }

    static EffectiveCurveChartControl()
    {
        AffectsRender<EffectiveCurveChartControl>(
            ParamsProperty, LivePressureProperty, LiveRawPressureProperty);
    }

    public EffectiveCurveChartControl()
    {
        RefreshInk();
        ActualThemeVariantChanged += (_, _) => { RefreshInk(); InvalidateVisual(); };
    }

    private void RefreshInk()
    {
        var dash = new DashStyle(new double[] { 3, 4 }, 0);
        BackgroundBrush = ThemeInk.Brush(this, "Pdl.Surface", BackgroundBrush);
        PlotBrush = ThemeInk.Brush(this, "Pdl.PlotField", PlotBrush);
        GridPen = ThemeInk.Pen(this, "Pdl.PlotGrid", GridPen, 1);
        IdentityPen = ThemeInk.Pen(this, "Pdl.PlotGrid", IdentityPen, 1, dash);
        CurvePen = ThemeInk.Pen(this, "Pdl.Accent", CurvePen, 2.5);
    }

    /// <summary>Square plot, sized off the available width — same rule as the editable chart.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = availableSize.Width;
        if (double.IsInfinity(width) || double.IsNaN(width) || width <= 0)
            return base.MeasureOverride(availableSize);

        double plotSide = Math.Max(0, width - 2 * Pad);
        return new Size(width, plotSide + 2 * Pad);
    }

    public override void Render(DrawingContext context)
    {
        double plotW = Bounds.Width - 2 * Pad;
        double plotH = Bounds.Height - 2 * Pad;
        if (plotW <= 0 || plotH <= 0) return;

        context.FillRectangle(BackgroundBrush, new Rect(0, 0, Bounds.Width, Bounds.Height));
        context.FillRectangle(PlotBrush, new Rect(Pad, Pad, plotW, plotH));

        for (int i = 0; i <= 4; i++)
        {
            double gx = Pad + i / 4.0 * plotW;
            double gy = Pad + i / 4.0 * plotH;
            context.DrawLine(GridPen, new Point(gx, Pad), new Point(gx, Pad + plotH));
            context.DrawLine(GridPen, new Point(Pad, gy), new Point(Pad + plotW, gy));
        }

        // The reference the whole chart is read against: above it the pair is lighter than
        // the pen, below it heavier.
        context.DrawLine(IdentityPen, new Point(Pad, Pad + plotH), new Point(Pad + plotW, Pad));

        DrawComposition(context, plotW, plotH);
        DrawIndicators(context, plotW, plotH);
    }

    private void DrawComposition(DrawingContext context, double plotW, double plotH)
    {
        // Sampled per pixel rather than solved: the pair can be any two of six curve types,
        // including a bezier whose own evaluation is already a binary search.
        int steps = Math.Max(2, (int)Math.Round(plotW));
        var figure = new PathFigure { IsClosed = false, Segments = new PathSegments() };

        for (int px = 0; px <= steps; px++)
        {
            double x = px / (double)steps;
            double y = Math.Clamp(CurveMath.ApplyPressureCurve(x, Params), 0, 1);
            var pt = new Point(Pad + x * plotW, Pad + plotH - y * plotH);
            if (px == 0) figure.StartPoint = pt;
            else figure.Segments.Add(new LineSegment { Point = pt });
        }

        context.DrawGeometry(null, CurvePen, new PathGeometry { Figures = new PathFigures { figure } });
    }

    private void DrawIndicators(DrawingContext context, double plotW, double plotH)
    {
        // Both dots sit on the composed curve, because that is the mapping this chart
        // shows. The x positions differ: raw is the pen, effective is what reaches curve 1
        // after smoothing.
        Dot(LiveRawPressure, RawDotBrush);
        Dot(LivePressure, EffectiveDotBrush);

        void Dot(double? value, IBrush brush)
        {
            if (value is not { } v) return;
            double x = Math.Clamp(v, 0, 1);
            double y = Math.Clamp(CurveMath.ApplyPressureCurve(x, Params), 0, 1);
            context.DrawEllipse(brush, null,
                new Point(Pad + x * plotW, Pad + plotH - y * plotH), 4, 4);
        }
    }
}
