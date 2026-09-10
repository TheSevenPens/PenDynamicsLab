using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PenDynamicsLab.Curves;
using PenDynamicsLab.Theming;
using System.Collections.Immutable;

namespace PenDynamicsLab.Controls;

/// <summary>
/// Visualization + editor for a <see cref="PressureCurveParams"/>. Mirrors
/// WebPressureExplorer's PressureChart.svelte: read-only curve trace plus draggable
/// min/max control nodes (power/sigmoid/extended) or full bezier anchor/handle editing.
/// </summary>
public sealed class PressureChartControl : Control
{
    // The chart draws no text at all — no axis titles, numeric labels or tick marks — so
    // the padding exists only to keep a node centred on the plot boundary from being
    // clipped. Uniform on all four sides, and comfortably clear of NodeDrawRadius.
    private const double Pad = 16;
    private const double NodeRadius = 8;
    private const double NodeDrawRadius = 6;
    private const double HandleRadius = 5;

    private IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private IBrush PlotBrush = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xFB));
    private IPen GridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xF4)), 1);
    private IPen CurvePen = new Pen(Brushes.Black, 2);

    // Live indicator colors — green = effective (post-smoothing pre-curve), purple = raw input.
    private static readonly IBrush EffectiveDotBrush = new SolidColorBrush(Color.FromRgb(0x14, 0xA0, 0x50));
    private static readonly IPen EffectiveGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0x14, 0xA0, 0x50)), 1)
    { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
    private static readonly IBrush RawDotBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x33, 0xCC));
    private static readonly IPen RawGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x33, 0xCC)), 1)
    { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };

    // Standard control node colors (min = pink, max = cyan); guides are translucent black dashed.
    private static readonly IBrush MinNodeBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x88));
    private static readonly IBrush MaxNodeBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xD0, 0xFF));
    private IPen StandardNodeGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), 1)
    { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) };
    private IPen NodeOutlineWhite = new Pen(Brushes.White, 1.5);
    private IPen NodeOutlineSelected = new Pen(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), 2.2);

    // Bezier-specific colors.
    private IBrush BezierEndpointBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x8B));
    private IBrush BezierInteriorBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x55, 0xCC));
    private IBrush BezierHandleSelectedBrush = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
    private IPen BezierHandleStemPen = new Pen(new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)), 1);
    private IPen BezierHandleOutlinePen = new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0x55, 0xCC)), 1.3);

    public static readonly StyledProperty<PressureCurveParams> ParamsProperty =
        AvaloniaProperty.Register<PressureChartControl, PressureCurveParams>(
            nameof(Params), defaultValue: PressureCurveParams.Default);

    public PressureCurveParams Params
    {
        get => GetValue(ParamsProperty);
        set => SetValue(ParamsProperty, value);
    }

    public static readonly StyledProperty<double?> LiveRawPressureProperty =
        AvaloniaProperty.Register<PressureChartControl, double?>(nameof(LiveRawPressure));

    public double? LiveRawPressure
    {
        get => GetValue(LiveRawPressureProperty);
        set => SetValue(LiveRawPressureProperty, value);
    }

    public static readonly StyledProperty<double?> LivePressureProperty =
        AvaloniaProperty.Register<PressureChartControl, double?>(nameof(LivePressure));

    public double? LivePressure
    {
        get => GetValue(LivePressureProperty);
        set => SetValue(LivePressureProperty, value);
    }

    /// <summary>Index of the selected bezier point (for highlight + remove default), or null.</summary>
    public int? SelectedBezierPoint { get; private set; }
    public BezierHandleSide? SelectedBezierHandle { get; private set; }

    static PressureChartControl()
    {
        AffectsRender<PressureChartControl>(ParamsProperty, LiveRawPressureProperty, LivePressureProperty);
        FocusableProperty.OverrideDefaultValue<PressureChartControl>(true);
    }

    /// <summary>Constrain height so the inner plot area is square at the given available width.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = availableSize.Width;
        if (double.IsInfinity(width) || double.IsNaN(width) || width <= 0)
            return base.MeasureOverride(availableSize);

        // plotW = width - 2*Pad; for a square plot area, plotH = plotW;
        // total height = plotH + 2*Pad.
        double plotSide = Math.Max(0, width - 2 * Pad);
        double height = plotSide + 2 * Pad;
        return new Size(width, height);
    }

    public PressureChartControl()
    {
        RefreshInk();
        ActualThemeVariantChanged += (_, _) => { RefreshInk(); InvalidateVisual(); };

        ContextMenu = new ContextMenu();
        // Suppress the default ContextMenu open: we open it manually on right-click in the bezier
        // path so the menu can carry per-location data (insert position / hit-tested point index).
        ContextMenu.Opening += (_, e) => e.Cancel = true;
    }

    /// <summary>
    /// Repoints the chart's chrome at the active theme. The data colours (raw purple,
    /// effective green, min pink, max cyan) are left alone on purpose — see ThemeInk.
    /// </summary>
    private void RefreshInk()
    {
        var dash = new DashStyle(new double[] { 3, 4 }, 0);

        BackgroundBrush          = ThemeInk.Brush(this, "Pdl.Surface", BackgroundBrush);
        PlotBrush                = ThemeInk.Brush(this, "Pdl.PlotField", PlotBrush);
        BezierEndpointBrush      = ThemeInk.Brush(this, "Pdl.BezierEnd", BezierEndpointBrush);
        BezierInteriorBrush      = ThemeInk.Brush(this, "Pdl.BezierNode", BezierInteriorBrush);
        BezierHandleSelectedBrush = ThemeInk.Brush(this, "Pdl.CurveInk", BezierHandleSelectedBrush);

        GridPen                  = ThemeInk.Pen(this, "Pdl.PlotGrid", GridPen, 1);
        CurvePen                 = ThemeInk.Pen(this, "Pdl.CurveInk", CurvePen, 2);
        StandardNodeGuidePen     = ThemeInk.Pen(this, "Pdl.GuideInk", StandardNodeGuidePen, 1, dash);
        NodeOutlineWhite         = ThemeInk.Pen(this, "Pdl.NodeOutline", NodeOutlineWhite, 1.5);
        NodeOutlineSelected      = ThemeInk.Pen(this, "Pdl.CurveInk", NodeOutlineSelected, 2.2);
        BezierHandleStemPen      = ThemeInk.Pen(this, "Pdl.HandleStem", BezierHandleStemPen, 1);
        BezierHandleOutlinePen   = ThemeInk.Pen(this, "Pdl.BezierNode", BezierHandleOutlinePen, 1.3);
    }

    // ── Layout helpers ──────────────────────────────────────────

    private (double plotW, double plotH) Layout()
        => (Bounds.Width - 2 * Pad, Bounds.Height - 2 * Pad);

    /// <summary>
    /// The plot area in control-local DIP coordinates — the gridded square only,
    /// excluding the axis titles. Used by "Plot area only" export.
    /// </summary>
    public Rect PlotRect
    {
        get
        {
            var (w, h) = Layout();
            return new Rect(Pad, Pad, Math.Max(0, w), Math.Max(0, h));
        }
    }

    private static double Clamp01(double v) => Math.Min(1, Math.Max(0, v));

    private static double Round2(double v) => Math.Round(v * 100) / 100;

    private static double Distance(Point p, double cx, double cy)
    {
        double dx = p.X - cx;
        double dy = p.Y - cy;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private double XValueFromCanvas(double cssX)
    {
        var (plotW, _) = Layout();
        return plotW > 0 ? Clamp01((cssX - Pad) / plotW) : 0;
    }

    private double YValueFromCanvas(double cssY)
    {
        var (_, plotH) = Layout();
        return plotH > 0 ? Clamp01((Pad + plotH - cssY) / plotH) : 0;
    }

    private bool IsInsidePlotArea(Point p)
    {
        var (plotW, plotH) = Layout();
        return p.X >= Pad && p.X <= Pad + plotW
            && p.Y >= Pad && p.Y <= Pad + plotH;
    }

    // ── Render ──────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var (plotW, plotH) = Layout();
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

        DrawCurve(context, plotW, plotH);
        DrawIndicators(context, plotW, plotH);
    }

    private void DrawCurve(DrawingContext context, double plotW, double plotH)
    {
        var curveType = Params.CurveType;

        if (curveType == CurveType.Passthrough)
        {
            context.DrawLine(CurvePen, new Point(Pad, Pad + plotH), new Point(Pad + plotW, Pad));
            return;
        }
        if (curveType == CurveType.Flat)
        {
            double fy = Pad + plotH - Params.FlatLevel * plotH;
            context.DrawLine(CurvePen, new Point(Pad, fy), new Point(Pad + plotW, fy));
            return;
        }
        if (curveType == CurveType.Inverted)
        {
            context.DrawLine(CurvePen, new Point(Pad, Pad), new Point(Pad + plotW, Pad + plotH));
            return;
        }
        if (curveType == CurveType.Bezier)
        {
            DrawBezier(context, plotW, plotH);
            return;
        }

        // Power-law / sigmoid: flat lead-in, sampled curve, flat lead-out, then control nodes.
        double inMin = Params.InputMinimum;
        double inMax = Params.InputMaximum;
        double outMin = Params.Minimum;
        double outMax = Params.Maximum;

        if (Params.MinApproach == MinApproach.Cut)
        {
            var cutFigure = new PathFigure
            {
                StartPoint = new Point(Pad, Pad + plotH),
                IsClosed = false,
                Segments = new PathSegments
                {
                    new LineSegment { Point = new Point(Pad + inMin * plotW, Pad + plotH) },
                    new LineSegment { Point = new Point(Pad + inMin * plotW, Pad + plotH - outMin * plotH) },
                },
            };
            context.DrawGeometry(null, CurvePen, new PathGeometry { Figures = new PathFigures { cutFigure } });
        }
        else
        {
            context.DrawLine(CurvePen,
                new Point(Pad, Pad + plotH - outMin * plotH),
                new Point(Pad + inMin * plotW, Pad + plotH - outMin * plotH));
        }

        int pxStart = (int)Math.Round(inMin * plotW);
        int pxEnd = (int)Math.Round(inMax * plotW);
        if (pxEnd > pxStart)
        {
            var figure = new PathFigure { IsClosed = false, Segments = new PathSegments() };
            for (int px = pxStart; px <= pxEnd; px++)
            {
                double xNorm = px / plotW;
                double y = CurveMath.ApplyPressureCurve(xNorm, Params);
                var pt = new Point(Pad + px, Pad + plotH - y * plotH);
                if (px == pxStart) figure.StartPoint = pt;
                else figure.Segments.Add(new LineSegment { Point = pt });
            }
            context.DrawGeometry(null, CurvePen, new PathGeometry { Figures = new PathFigures { figure } });
        }

        context.DrawLine(CurvePen,
            new Point(Pad + inMax * plotW, Pad + plotH - outMax * plotH),
            new Point(Pad + plotW, Pad + plotH - outMax * plotH));

        // Standard control nodes — only for the types that actually honour the range
        // fields. Asking UsesRangeControls rather than naming the types keeps this in step
        // with the evaluator: a node you can drag but that changes nothing would be a lie.
        if (CurveMath.UsesRangeControls(curveType))
        {
            DrawStandardNode(context, inMin, outMin, MinNodeBrush);
            DrawStandardNode(context, inMax, outMax, MaxNodeBrush);
        }
    }

    private void DrawStandardNode(DrawingContext context, double xValue, double yValue, IBrush color)
    {
        var (plotW, plotH) = Layout();
        double cx = Pad + xValue * plotW;
        double cy = Pad + plotH - yValue * plotH;

        context.DrawLine(StandardNodeGuidePen, new Point(cx, cy), new Point(cx, Pad + plotH));
        context.DrawLine(StandardNodeGuidePen, new Point(cx, cy), new Point(Pad, cy));

        context.DrawEllipse(color, NodeOutlineWhite, new Point(cx, cy), NodeDrawRadius, NodeDrawRadius);
    }

    private void DrawBezier(DrawingContext context, double plotW, double plotH)
    {
        var pts = CurveMath.NormalizeBezierPoints(Params.BezierPoints);
        if (pts.Length == 0) return;

        // Curve trace.
        var figure = new PathFigure
        {
            IsClosed = false,
            StartPoint = new Point(Pad + pts[0].X * plotW, Pad + plotH - pts[0].Y * plotH),
            Segments = new PathSegments(),
        };
        for (int i = 0; i < pts.Length - 1; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Point(Pad + a.OutX * plotW, Pad + plotH - a.OutY * plotH),
                Point2 = new Point(Pad + b.InX * plotW, Pad + plotH - b.InY * plotH),
                Point3 = new Point(Pad + b.X * plotW, Pad + plotH - b.Y * plotH),
            });
        }
        context.DrawGeometry(null, CurvePen, new PathGeometry { Figures = new PathFigures { figure } });

        // Anchors + handles.
        for (int i = 0; i < pts.Length; i++)
        {
            var p = pts[i];
            double nodeX = Pad + p.X * plotW;
            double nodeY = Pad + plotH - p.Y * plotH;
            bool isEndpoint = i == 0 || i == pts.Length - 1;
            bool isSelected = i == SelectedBezierPoint;

            // In handle (only for non-first points).
            if (i > 0)
            {
                double hx = Pad + p.InX * plotW;
                double hy = Pad + plotH - p.InY * plotH;
                context.DrawLine(BezierHandleStemPen, new Point(nodeX, nodeY), new Point(hx, hy));
                bool selected = isSelected && SelectedBezierHandle == BezierHandleSide.In;
                var fill = selected ? BezierHandleSelectedBrush : (IBrush)Brushes.White;
                context.DrawEllipse(fill, BezierHandleOutlinePen, new Point(hx, hy), HandleRadius, HandleRadius);
            }
            // Out handle (only for non-last points).
            if (i < pts.Length - 1)
            {
                double hx = Pad + p.OutX * plotW;
                double hy = Pad + plotH - p.OutY * plotH;
                context.DrawLine(BezierHandleStemPen, new Point(nodeX, nodeY), new Point(hx, hy));
                bool selected = isSelected && SelectedBezierHandle == BezierHandleSide.Out;
                var fill = selected ? BezierHandleSelectedBrush : (IBrush)Brushes.White;
                context.DrawEllipse(fill, BezierHandleOutlinePen, new Point(hx, hy), HandleRadius, HandleRadius);
            }

            var anchorBrush = isEndpoint ? BezierEndpointBrush : BezierInteriorBrush;
            var outlinePen = isSelected ? NodeOutlineSelected : NodeOutlineWhite;
            context.DrawEllipse(anchorBrush, outlinePen, new Point(nodeX, nodeY), NodeDrawRadius, NodeDrawRadius);
        }
    }

    private void DrawIndicators(DrawingContext context, double plotW, double plotH)
    {
        if (LiveRawPressure is { } raw)
        {
            double mapped = CurveMath.ApplyPressureCurve(raw, Params);
            DrawIndicator(context, plotW, plotH, raw, mapped, RawDotBrush, RawGuidePen);
        }
        if (LivePressure is { } eff)
        {
            double mapped = CurveMath.ApplyPressureCurve(eff, Params);
            DrawIndicator(context, plotW, plotH, eff, mapped, EffectiveDotBrush, EffectiveGuidePen);
        }
    }

    private static void DrawIndicator(DrawingContext context, double plotW, double plotH,
        double inputValue, double outputValue, IBrush dotBrush, IPen guidePen)
    {
        double dotX = Pad + inputValue * plotW;
        double dotY = Pad + plotH - outputValue * plotH;
        context.DrawLine(guidePen, new Point(dotX, Pad + plotH), new Point(dotX, dotY));
        context.DrawLine(guidePen, new Point(Pad, dotY), new Point(dotX, dotY));
        context.DrawEllipse(dotBrush, null, new Point(dotX, dotY), 4, 4);
    }


    // ── Hit testing ─────────────────────────────────────────────

    public enum BezierHandleSide { In, Out }

    private enum DragKind { None, MinNode, MaxNode, BezierAnchor, BezierHandle }
    private DragKind _dragging = DragKind.None;
    private int _dragBezierIndex;
    private BezierHandleSide _dragBezierHandle;

    private int? HitTestBezierAnchor(Point p, ImmutableArray<BezierPoint> pts)
    {
        var (plotW, plotH) = Layout();
        for (int i = pts.Length - 1; i >= 0; i--)
        {
            var pt = pts[i];
            double cx = Pad + pt.X * plotW;
            double cy = Pad + plotH - pt.Y * plotH;
            if (Distance(p, cx, cy) <= NodeRadius) return i;
        }
        return null;
    }

    private (int index, BezierHandleSide handle)? HitTestBezierHandle(Point p, ImmutableArray<BezierPoint> pts)
    {
        var (plotW, plotH) = Layout();
        for (int i = pts.Length - 1; i >= 0; i--)
        {
            var pt = pts[i];
            if (i > 0 && (pt.InX != pt.X || pt.InY != pt.Y))
            {
                double hx = Pad + pt.InX * plotW;
                double hy = Pad + plotH - pt.InY * plotH;
                if (Distance(p, hx, hy) <= HandleRadius + 1)
                    return (i, BezierHandleSide.In);
            }
            if (i < pts.Length - 1 && (pt.OutX != pt.X || pt.OutY != pt.Y))
            {
                double hx = Pad + pt.OutX * plotW;
                double hy = Pad + plotH - pt.OutY * plotH;
                if (Distance(p, hx, hy) <= HandleRadius + 1)
                    return (i, BezierHandleSide.Out);
            }
        }
        return null;
    }

    private DragKind HitTestStandardNode(Point p)
    {
        var (plotW, plotH) = Layout();
        if (Distance(p, Pad + Params.InputMinimum * plotW, Pad + plotH - Params.Minimum * plotH) <= NodeRadius)
            return DragKind.MinNode;
        if (Distance(p, Pad + Params.InputMaximum * plotW, Pad + plotH - Params.Maximum * plotH) <= NodeRadius)
            return DragKind.MaxNode;
        return DragKind.None;
    }

    // ── Pointer interaction ─────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        var pos = e.GetPosition(this);

        if (props.IsRightButtonPressed)
        {
            HandleRightClick(e, pos);
            return;
        }
        if (!props.IsLeftButtonPressed) return;

        var ct = Params.CurveType;
        if (ct == CurveType.Bezier)
        {
            var pts = CurveMath.NormalizeBezierPoints(Params.BezierPoints);
            if (HitTestBezierHandle(pos, pts) is { } h)
            {
                SelectedBezierPoint = h.index;
                SelectedBezierHandle = h.handle;
                _dragging = DragKind.BezierHandle;
                _dragBezierIndex = h.index;
                _dragBezierHandle = h.handle;
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }
            int? hitAnchor = HitTestBezierAnchor(pos, pts);
            if (hitAnchor is { } idx)
            {
                SelectedBezierPoint = idx;
                SelectedBezierHandle = null;
                _dragging = DragKind.BezierAnchor;
                _dragBezierIndex = idx;
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }
            // Click on empty plot area — clear selection.
            SelectedBezierPoint = null;
            SelectedBezierHandle = null;
            InvalidateVisual();
            return;
        }

        if (ct is CurveType.Sigmoid or CurveType.Extended)
        {
            var hit = HitTestStandardNode(pos);
            if (hit != DragKind.None)
            {
                _dragging = hit;
                e.Pointer.Capture(this);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        switch (_dragging)
        {
            case DragKind.MinNode:
            case DragKind.MaxNode:
                DragStandardNode(pos);
                return;
            case DragKind.BezierAnchor:
                DragBezierAnchor(pos);
                return;
            case DragKind.BezierHandle:
                DragBezierHandle(pos);
                return;
        }

        // Cursor feedback (not strictly necessary but helps discoverability).
        Cursor = ResolveCursor(pos);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging != DragKind.None)
        {
            _dragging = DragKind.None;
            e.Pointer.Capture(null);
        }
    }

    private Cursor ResolveCursor(Point pos)
    {
        var ct = Params.CurveType;
        if (ct == CurveType.Bezier)
        {
            var pts = CurveMath.NormalizeBezierPoints(Params.BezierPoints);
            if (HitTestBezierHandle(pos, pts) is not null) return new Cursor(StandardCursorType.Cross);
            if (HitTestBezierAnchor(pos, pts) is not null) return new Cursor(StandardCursorType.SizeAll);
            return Cursor.Default;
        }
        if (ct is CurveType.Sigmoid or CurveType.Extended)
        {
            return HitTestStandardNode(pos) != DragKind.None
                ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
        }
        return Cursor.Default;
    }

    // ── Drag handlers ───────────────────────────────────────────

    private void DragStandardNode(Point pos)
    {
        double inVal = Round2(XValueFromCanvas(pos.X));
        double outVal = Round2(YValueFromCanvas(pos.Y));

        if (_dragging == DragKind.MinNode)
        {
            inVal = Math.Min(inVal, Params.InputMaximum - 0.01);
            outVal = Math.Min(outVal, Params.Maximum);
            Params = Params with { InputMinimum = Clamp01(inVal), Minimum = Clamp01(outVal) };
        }
        else
        {
            inVal = Math.Max(inVal, Params.InputMinimum + 0.01);
            outVal = Math.Max(outVal, Params.Minimum);
            Params = Params with { InputMaximum = Clamp01(inVal), Maximum = Clamp01(outVal) };
        }
    }

    private void DragBezierAnchor(Point pos)
    {
        var pts = CurveMath.NormalizeBezierPoints(Params.BezierPoints);
        int idx = _dragBezierIndex;
        if (idx < 0 || idx >= pts.Length) return;

        var pt = pts[idx];
        double prevX = idx > 0 ? pts[idx - 1].X : 0;
        double nextX = idx < pts.Length - 1 ? pts[idx + 1].X : 1;

        double inVal = Round2(XValueFromCanvas(pos.X));
        double outVal = Round2(YValueFromCanvas(pos.Y));
        if (idx == 0) inVal = 0;
        else if (idx == pts.Length - 1) inVal = 1;
        else inVal = Math.Max(prevX + 0.01, Math.Min(nextX - 0.01, inVal));

        double dx = inVal - pt.X;
        double dy = outVal - pt.Y;

        var updated = pt with
        {
            X = inVal,
            Y = outVal,
            InX = pt.InX + dx,
            InY = pt.InY + dy,
            OutX = pt.OutX + dx,
            OutY = pt.OutY + dy,
        };

        var arr = pts.SetItem(idx, updated);
        Params = Params with { BezierPoints = CurveMath.NormalizeBezierPoints(arr) };
    }

    private void DragBezierHandle(Point pos)
    {
        var pts = CurveMath.NormalizeBezierPoints(Params.BezierPoints);
        int idx = _dragBezierIndex;
        if (idx < 0 || idx >= pts.Length) return;
        var pt = pts[idx];
        double xVal = Round2(XValueFromCanvas(pos.X));
        double yVal = Round2(YValueFromCanvas(pos.Y));
        double prevX = idx > 0 ? pts[idx - 1].X : pt.X;
        double nextX = idx < pts.Length - 1 ? pts[idx + 1].X : pt.X;

        double ClampInX(double v) => Math.Max(prevX, Math.Min(pt.X, v));
        double ClampOutX(double v) => Math.Max(pt.X, Math.Min(nextX, v));

        BezierPoint updated;
        if (_dragBezierHandle == BezierHandleSide.In)
        {
            double inX = ClampInX(xVal);
            double inY = yVal;
            updated = pt with { InX = inX, InY = inY };
            if (pt.HandleMode == HandleMode.Mirrored && idx > 0 && idx < pts.Length - 1)
            {
                updated = updated with
                {
                    OutX = ClampOutX(pt.X + (pt.X - inX)),
                    OutY = Clamp01(pt.Y + (pt.Y - inY)),
                };
            }
        }
        else
        {
            double outX = ClampOutX(xVal);
            double outY = yVal;
            updated = pt with { OutX = outX, OutY = outY };
            if (pt.HandleMode == HandleMode.Mirrored && idx > 0 && idx < pts.Length - 1)
            {
                updated = updated with
                {
                    InX = ClampInX(pt.X - (outX - pt.X)),
                    InY = Clamp01(pt.Y - (outY - pt.Y)),
                };
            }
        }

        var arr = pts.SetItem(idx, updated);
        Params = Params with { BezierPoints = CurveMath.NormalizeBezierPoints(arr) };
    }

    // ── Right-click context menu ────────────────────────────────

    /// <summary>
    /// Supplies the export entries appended to the chart's right-click menu. The owner
    /// provides them so this control stays free of clipboard and file-picker concerns.
    /// Must return freshly built controls each call — a menu item can only have one parent,
    /// and a new <see cref="ContextMenu"/> is constructed per right-click.
    /// </summary>
    public Func<IEnumerable<Control>>? BuildExportMenuItems { get; set; }

    private void HandleRightClick(PointerPressedEventArgs e, Point pos)
    {
        var menu = new ContextMenu();

        // Bezier editing entries come first, and only when the click is somewhere they
        // apply. Other curve types get the export entries alone.
        if (Params.CurveType == CurveType.Bezier)
        {
            var pts = CurveMath.NormalizeBezierPoints(Params.BezierPoints);
            int? hitIndex = HitTestBezierAnchor(pos, pts);
            bool canAdd = pts.Length < 16 && IsInsidePlotArea(pos) && hitIndex == null;
            bool canRemoveAtHit = hitIndex is { } hi && hi > 0 && hi < pts.Length - 1;

            if (canAdd)
            {
                double rx = XValueFromCanvas(pos.X);
                double ry = YValueFromCanvas(pos.Y);
                var addItem = new MenuItem { Header = $"Add point at ({rx:0.00}, {ry:0.00})" };
                addItem.Click += (_, _) => InsertBezierPointAt(rx, ry);
                menu.Items.Add(addItem);
            }
            if (canRemoveAtHit)
            {
                int target = hitIndex!.Value;
                SelectedBezierPoint = target;
                var removeItem = new MenuItem { Header = $"Remove point #{target}" };
                removeItem.Click += (_, _) => RemoveBezierPoint(target);
                menu.Items.Add(removeItem);

                var brokenItem = new MenuItem { Header = "Handles: Broken" };
                brokenItem.Click += (_, _) => SetHandleMode(target, HandleMode.Broken);
                menu.Items.Add(brokenItem);
                var mirroredItem = new MenuItem { Header = "Handles: Mirrored" };
                mirroredItem.Click += (_, _) => SetHandleMode(target, HandleMode.Mirrored);
                menu.Items.Add(mirroredItem);
            }
        }

        if (BuildExportMenuItems?.Invoke() is { } exportItems)
        {
            bool needsSeparator = menu.Items.Count > 0;
            foreach (var item in exportItems)
            {
                if (needsSeparator) { menu.Items.Add(new Separator()); needsSeparator = false; }
                menu.Items.Add(item);
            }
        }

        if (menu.Items.Count == 0) return;

        // Every menu we install carries the same guard as the initial one: Avalonia opens
        // a control's ContextMenu automatically on right-click, and that would fire at the
        // wrong moment (and, once this menu is the installed one, alongside our own open).
        // The flag lets our deliberate Open through and cancels everything else.
        menu.Opening += (_, ev) => { if (!_openingMenuManually) ev.Cancel = true; };

        ContextMenu = menu;
        _openingMenuManually = true;
        try { menu.Open(this); }
        finally { _openingMenuManually = false; }
    }

    private bool _openingMenuManually;

    public bool CanAddBezierPoint
        => Params.CurveType == CurveType.Bezier
           && CurveMath.NormalizeBezierPoints(Params.BezierPoints).Length < 16;

    public bool CanRemoveBezierPoint
        => Params.CurveType == CurveType.Bezier
           && CurveMath.NormalizeBezierPoints(Params.BezierPoints).Length > 2;

    /// <summary>Insert a point at the midpoint of the largest existing X-gap (toolbar Add button).</summary>
    public void AddBezierPointAtLargestGap()
    {
        if (!CanAddBezierPoint) return;
        var pts = CurveMath.NormalizeBezierPoints(Params.BezierPoints);
        int target = 0;
        double maxGap = -1;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            double gap = pts[i + 1].X - pts[i].X;
            if (gap > maxGap) { maxGap = gap; target = i; }
        }
        var left = pts[target];
        var right = pts[target + 1];
        var newPt = new BezierPoint(
            X: Round2((left.X + right.X) / 2),
            Y: Round2((left.Y + right.Y) / 2),
            InX: Round2(left.X * 0.66 + right.X * 0.34),
            InY: Round2(left.Y * 0.66 + right.Y * 0.34),
            OutX: Round2(left.X * 0.34 + right.X * 0.66),
            OutY: Round2(left.Y * 0.34 + right.Y * 0.66),
            HandleMode: HandleMode.Broken);
        var list = pts.ToList();
        list.Insert(target + 1, newPt);
        Params = Params with { BezierPoints = CurveMath.NormalizeBezierPoints(list) };
        SelectedBezierPoint = target + 1;
    }

    /// <summary>Remove the currently-selected interior point, or default to second-to-last.</summary>
    public void RemoveSelectedBezierPoint()
    {
        if (!CanRemoveBezierPoint) return;
        var pts = CurveMath.NormalizeBezierPoints(Params.BezierPoints);
        int removeIdx = SelectedBezierPoint is { } s && s > 0 && s < pts.Length - 1
            ? s : pts.Length - 2;
        RemoveBezierPoint(removeIdx);
    }

    private void InsertBezierPointAt(double rawX, double rawY)
    {
        if (!CanAddBezierPoint) return;
        var pts = CurveMath.NormalizeBezierPoints(Params.BezierPoints);

        int insertIdx = 0;
        for (int i = 0; i < pts.Length; i++) { if (pts[i].X > rawX) { insertIdx = i; break; } }
        if (insertIdx <= 0) insertIdx = 1;
        else if (insertIdx >= pts.Length) insertIdx = pts.Length - 1;

        double prevX = pts[insertIdx - 1].X;
        double nextX = pts[insertIdx].X;
        double minX = prevX + 0.01;
        double maxX = nextX - 0.01;
        if (minX > maxX) return;

        double x = Math.Min(maxX, Math.Max(minX, Round2(Math.Min(maxX, Math.Max(minX, rawX)))));
        double y = Round2(rawY);
        var newPt = new BezierPoint(
            X: x, Y: y,
            InX: Round2((prevX + x) / 2),
            InY: y,
            OutX: Round2((x + nextX) / 2),
            OutY: y,
            HandleMode: HandleMode.Broken);
        var list = pts.ToList();
        list.Insert(insertIdx, newPt);
        Params = Params with { BezierPoints = CurveMath.NormalizeBezierPoints(list) };
        SelectedBezierPoint = insertIdx;
    }

    private void RemoveBezierPoint(int index)
    {
        var pts = CurveMath.NormalizeBezierPoints(Params.BezierPoints);
        if (index <= 0 || index >= pts.Length - 1) return;
        var list = pts.ToList();
        list.RemoveAt(index);
        Params = Params with { BezierPoints = CurveMath.NormalizeBezierPoints(list) };
        SelectedBezierPoint = null;
        SelectedBezierHandle = null;
    }

    private void SetHandleMode(int index, HandleMode mode)
    {
        var pts = CurveMath.NormalizeBezierPoints(Params.BezierPoints);
        if (index <= 0 || index >= pts.Length - 1) return;
        var list = pts.SetItem(index, pts[index] with { HandleMode = mode });
        Params = Params with { BezierPoints = CurveMath.NormalizeBezierPoints(list) };
    }
}
