using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PenDynamicsLab.Curves;

namespace PenDynamicsLab.Controls;

/// <summary>
/// Read-only visualization of a <see cref="PressureCurveParams"/> as an X→Y curve plot.
/// Chart layout (padding, grid, label format) mirrors WebPressureExplorer's canvasUtils.js
/// so the desktop port reads the same to anyone familiar with the web app.
/// </summary>
public sealed class PressureChartControl : Control
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
    private static readonly IPen CurvePen = new Pen(Brushes.Black, 2);
    private static readonly Typeface MonoTypeface = new("Consolas, monospace");
    private static readonly Typeface SansTypeface = new("Segoe UI");

    public static readonly StyledProperty<PressureCurveParams> ParamsProperty =
        AvaloniaProperty.Register<PressureChartControl, PressureCurveParams>(
            nameof(Params),
            defaultValue: PressureCurveParams.Default);

    public PressureCurveParams Params
    {
        get => GetValue(ParamsProperty);
        set => SetValue(ParamsProperty, value);
    }

    static PressureChartControl()
    {
        AffectsRender<PressureChartControl>(ParamsProperty);
    }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double height = Bounds.Height;
        double plotW = width - PadLeft - PadRight;
        double plotH = height - PadTop - PadBottom;
        if (plotW <= 0 || plotH <= 0) return;

        // Background
        context.FillRectangle(BackgroundBrush, new Rect(0, 0, width, height));
        context.FillRectangle(PlotBrush, new Rect(PadLeft, PadTop, plotW, plotH));

        // Grid (5 lines including borders)
        for (int i = 0; i <= 4; i++)
        {
            double gx = PadLeft + i / 4.0 * plotW;
            double gy = PadTop + i / 4.0 * plotH;
            context.DrawLine(GridPen, new Point(gx, PadTop), new Point(gx, PadTop + plotH));
            context.DrawLine(GridPen, new Point(PadLeft, gy), new Point(PadLeft + plotW, gy));
        }

        DrawLabels(context, width, height, plotW, plotH);
        DrawCurve(context, plotW, plotH);
    }

    private void DrawLabels(DrawingContext context, double width, double height, double plotW, double plotH)
    {
        // X tick labels (0, 0.25, 0.5, 0.75, 1)
        for (int i = 0; i <= 4; i++)
        {
            double gx = Math.Round(PadLeft + i / 4.0 * plotW);
            string label = FormatTick(i * 0.25);
            var ft = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, MonoTypeface, 10, LabelBrush);
            context.DrawText(ft, new Point(gx - ft.Width / 2, Math.Round(PadTop + plotH + XLabelSpacing)));
        }

        // Y tick labels
        for (int i = 0; i <= 4; i++)
        {
            double gy = Math.Round(PadTop + plotH - i / 4.0 * plotH);
            string label = FormatTick(i * 0.25);
            var ft = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, MonoTypeface, 10, LabelBrush);
            context.DrawText(ft, new Point(Math.Round(PadLeft - YLabelSpacing) - ft.Width, gy - ft.Height / 2));
        }

        // X axis label
        var xAxisFt = new FormattedText("INPUT", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, SansTypeface, 10, LabelBrush);
        context.DrawText(xAxisFt,
            new Point(Math.Round(PadLeft + plotW / 2 - xAxisFt.Width / 2),
                     Math.Round(height - XAxisLabelSpacing - xAxisFt.Height)));

        // Y axis label (rotated)
        var yAxisFt = new FormattedText("OUTPUT", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, SansTypeface, 10, LabelBrush);
        var yAxisOrigin = new Point(Math.Round(YAxisLabelSpacing), Math.Round(PadTop + plotH / 2));
        using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 2) *
                                     Matrix.CreateTranslation(yAxisOrigin.X, yAxisOrigin.Y)))
        {
            context.DrawText(yAxisFt, new Point(-yAxisFt.Width / 2, 0));
        }
    }

    private void DrawCurve(DrawingContext context, double plotW, double plotH)
    {
        var curveType = Params.CurveType;

        if (curveType == CurveType.Passthrough)
        {
            context.DrawLine(CurvePen,
                new Point(PadLeft, PadTop + plotH),
                new Point(PadLeft + plotW, PadTop));
            return;
        }

        if (curveType == CurveType.Flat)
        {
            double fy = PadTop + plotH - Params.FlatLevel * plotH;
            context.DrawLine(CurvePen, new Point(PadLeft, fy), new Point(PadLeft + plotW, fy));
            return;
        }

        if (curveType == CurveType.Bezier)
        {
            DrawBezier(context, plotW, plotH);
            return;
        }

        // Power-law / sigmoid: draw flat lead-in (clamp) or step (cut), then sampled curve, then flat lead-out
        double inMin = Params.InputMinimum;
        double inMax = Params.InputMaximum;
        double outMin = Params.Minimum;
        double outMax = Params.Maximum;

        if (Params.MinApproach == MinApproach.Cut)
        {
            var cutFigure = new PathFigure
            {
                StartPoint = new Point(PadLeft, PadTop + plotH),
                IsClosed = false,
                Segments = new PathSegments
                {
                    new LineSegment { Point = new Point(PadLeft + inMin * plotW, PadTop + plotH) },
                    new LineSegment { Point = new Point(PadLeft + inMin * plotW, PadTop + plotH - outMin * plotH) },
                },
            };
            context.DrawGeometry(null, CurvePen, new PathGeometry { Figures = new PathFigures {cutFigure } });
        }
        else
        {
            context.DrawLine(CurvePen,
                new Point(PadLeft, PadTop + plotH - outMin * plotH),
                new Point(PadLeft + inMin * plotW, PadTop + plotH - outMin * plotH));
        }

        // Sampled curve segment between inMin and inMax (one sample per pixel)
        int pxStart = (int)Math.Round(inMin * plotW);
        int pxEnd = (int)Math.Round(inMax * plotW);
        if (pxEnd > pxStart)
        {
            var figure = new PathFigure { IsClosed = false, Segments = new PathSegments() };
            for (int px = pxStart; px <= pxEnd; px++)
            {
                double xNorm = px / plotW;
                double y = CurveMath.ApplyPressureCurve(xNorm, Params);
                var pt = new Point(PadLeft + px, PadTop + plotH - y * plotH);
                if (px == pxStart) figure.StartPoint = pt;
                else figure.Segments.Add(new LineSegment { Point = pt });
            }
            context.DrawGeometry(null, CurvePen, new PathGeometry { Figures = new PathFigures {figure } });
        }

        // Flat lead-out
        context.DrawLine(CurvePen,
            new Point(PadLeft + inMax * plotW, PadTop + plotH - outMax * plotH),
            new Point(PadLeft + plotW, PadTop + plotH - outMax * plotH));
    }

    private void DrawBezier(DrawingContext context, double plotW, double plotH)
    {
        var pts = CurveMath.NormalizeBezierPoints(Params.BezierPoints);
        if (pts.Length == 0) return;

        var figure = new PathFigure
        {
            IsClosed = false,
            StartPoint = new Point(PadLeft + pts[0].X * plotW, PadTop + plotH - pts[0].Y * plotH),
            Segments = new PathSegments(),
        };
        for (int i = 0; i < pts.Length - 1; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Point(PadLeft + a.OutX * plotW, PadTop + plotH - a.OutY * plotH),
                Point2 = new Point(PadLeft + b.InX * plotW, PadTop + plotH - b.InY * plotH),
                Point3 = new Point(PadLeft + b.X * plotW, PadTop + plotH - b.Y * plotH),
            });
        }
        context.DrawGeometry(null, CurvePen, new PathGeometry { Figures = new PathFigures {figure } });
    }

    private static string FormatTick(double v)
    {
        // Match the JS toFixed(2).replace(/\.?0+$/, '') — strip trailing zeros and decimal point.
        string s = v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        s = s.TrimEnd('0').TrimEnd('.');
        return s.Length == 0 ? "0" : s;
    }
}
