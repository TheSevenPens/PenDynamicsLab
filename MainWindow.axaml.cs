using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System.Linq;
using PenSession;
using PenSession.Avalonia;
using PenDynamicsLab.Curves;
using PenDynamicsLab.Drawing;
using SkiaSharp;

namespace PenDynamicsLab;

public partial class MainWindow : Window
{
    private static readonly SKColor[] StrokePalette =
    [
        new(0xE6, 0x19, 0x4B), new(0x3C, 0xB4, 0x4B), new(0x43, 0x63, 0xD8), new(0xF5, 0x82, 0x31),
        new(0x91, 0x1E, 0xB4), new(0x42, 0xD4, 0xF4), new(0xF0, 0x32, 0xE6), new(0xBF, 0xEF, 0x45),
        new(0xFA, 0xBE, 0xD4), new(0x46, 0x99, 0x90), new(0xDC, 0xBE, 0xFF), new(0x9A, 0x63, 0x24),
        new(0x80, 0x00, 0x00), new(0xAA, 0xFF, 0xC3), new(0x80, 0x80, 0x00), new(0x00, 0x00, 0x75),
    ];
    private static readonly SKColor BlackStrokeColor = new(0x1A, 0x1A, 0x2E);

    private IPenSession? _session;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private DrawSurface? _processed;
    private DrawSurface? _raw;

    private IReadOnlyList<InputApi> _apis = [];
    private DateTime _lastPointTime;

    // Pressure curve state.
    private PressureCurveParams _curveParams = PressureCurveParams.Default;
    private bool _suppressCurveControlEvents;

    // Brush / drawing state.
    private double _brushSize = 40;
    private ColorMode _colorMode = ColorMode.Black;
    private PressureControl _pressureControl = PressureControl.Size;
    private bool _drawZeroPressure;
    private SKColor _strokeColor = BlackStrokeColor;
    private int _lastColorIndex = -1;

    // Stroke-local smoothing state. Reset whenever the pen lifts or the active canvas changes.
    private enum ActiveCanvas { None, Processed, Raw }
    private ActiveCanvas _activeCanvas = ActiveCanvas.None;
    private Point? _lastDrawPos;
    private Point? _smoothedPos;
    private double? _smoothedPressure;
    private readonly Random _rng = new();

    public MainWindow()
    {
        InitializeComponent();

        _renderTimer.Tick += RenderTimer_Tick;

        // Resize bitmaps to follow each canvas host's bounds.
        ProcessedCanvasHost.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Bounds") EnsureSurfaces();
        };
        RawCanvasHost.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Bounds") EnsureSurfaces();
        };

        InitializeBrushControls();
        InitializeCurveControls();

        Opened += (_, _) =>
        {
            // WM_POINTER subclassing doesn't receive events in Avalonia.
            var apiList = PenSessionFactory.GetAvailableApis()
                .Where(a => a != InputApi.WmPointer).ToList();
            apiList.Add(InputApi.AvaloniaPointer);
            _apis = apiList;

            foreach (var api in _apis)
            {
                string name = api switch
                {
                    InputApi.WintabSystem => "Wintab",
                    InputApi.WintabDigitizer => "Wintab (high-res)",
                    InputApi.AvaloniaPointer => "Avalonia Pointer",
                    _ => api.ToString()
                };
                ApiCombo.Items.Add(name);
            }
            ApiCombo.SelectionChanged += ApiCombo_SelectionChanged;
            if (ApiCombo.Items.Count > 0) ApiCombo.SelectedIndex = 0;
        };

        Closing += (_, _) =>
        {
            _renderTimer.Stop();
            _session?.Stop();
            _session?.Dispose();
            _processed?.Dispose();
            _raw?.Dispose();
        };
    }

    // ── Surface management ──────────────────────────────────────

    private void EnsureSurfaces()
    {
        _processed ??= new DrawSurface(ProcessedImage);
        _raw ??= new DrawSurface(RawImage);
        _processed.EnsureSize((int)ProcessedCanvasHost.Bounds.Width, (int)ProcessedCanvasHost.Bounds.Height);
        _raw.EnsureSize((int)RawCanvasHost.Bounds.Width, (int)RawCanvasHost.Bounds.Height);
    }

    // ── Brush controls ──────────────────────────────────────────

    private void InitializeBrushControls()
    {
        BrushSizeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != "Value") return;
            _brushSize = BrushSizeSlider.Value;
            BrushSizeLabel.Text = $"{(int)_brushSize} px";
        };

        ColorBlackRadio.IsCheckedChanged += (_, _) =>
        {
            if (ColorBlackRadio.IsChecked == true) _colorMode = ColorMode.Black;
        };
        ColorRandomRadio.IsCheckedChanged += (_, _) =>
        {
            if (ColorRandomRadio.IsChecked == true) _colorMode = ColorMode.Random;
        };

        PressureSizeRadio.IsCheckedChanged += (_, _) =>
        {
            if (PressureSizeRadio.IsChecked == true) _pressureControl = PressureControl.Size;
        };
        PressureOpacityRadio.IsCheckedChanged += (_, _) =>
        {
            if (PressureOpacityRadio.IsChecked == true) _pressureControl = PressureControl.Opacity;
        };

        DrawZeroPressureCheck.IsCheckedChanged += (_, _) =>
        {
            _drawZeroPressure = DrawZeroPressureCheck.IsChecked == true;
        };

        BrushSizeLabel.Text = $"{(int)_brushSize} px";
    }

    private void PickStrokeColor()
    {
        if (_colorMode == ColorMode.Black)
        {
            _strokeColor = BlackStrokeColor;
            return;
        }
        int idx;
        do { idx = _rng.Next(StrokePalette.Length); } while (idx == _lastColorIndex && StrokePalette.Length > 1);
        _lastColorIndex = idx;
        _strokeColor = StrokePalette[idx];
    }

    // ── Curve controls ──────────────────────────────────────────

    private void InitializeCurveControls()
    {
        foreach (var ct in Enum.GetValues<CurveType>())
            CurveTypeCombo.Items.Add(ct.ToString());
        foreach (var so in Enum.GetValues<SmoothingOrder>())
            SmoothingOrderCombo.Items.Add(FormatSmoothingOrder(so));

        _suppressCurveControlEvents = true;
        CurveTypeCombo.SelectedIndex = (int)_curveParams.CurveType;
        SmoothingOrderCombo.SelectedIndex = (int)_curveParams.SmoothingOrder;
        SoftnessSlider.Value = _curveParams.Softness;
        InputMinSlider.Value = _curveParams.InputMinimum;
        InputMaxSlider.Value = _curveParams.InputMaximum;
        OutputMinSlider.Value = _curveParams.Minimum;
        OutputMaxSlider.Value = _curveParams.Maximum;
        TransitionWidthSlider.Value = _curveParams.TransitionWidth;
        FlatLevelSlider.Value = _curveParams.FlatLevel;
        PressureEmaSlider.Value = _curveParams.EmaSmoothing;
        PositionEmaSlider.Value = _curveParams.PositionEmaSmoothing;
        MinApproachClampRadio.IsChecked = _curveParams.MinApproach == MinApproach.Clamp;
        MinApproachCutRadio.IsChecked = _curveParams.MinApproach == MinApproach.Cut;
        _suppressCurveControlEvents = false;

        CurveTypeCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressCurveControlEvents || CurveTypeCombo.SelectedIndex < 0) return;
            UpdateParams(p => p with { CurveType = (CurveType)CurveTypeCombo.SelectedIndex });
        };
        SmoothingOrderCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressCurveControlEvents || SmoothingOrderCombo.SelectedIndex < 0) return;
            UpdateParams(p => p with { SmoothingOrder = (SmoothingOrder)SmoothingOrderCombo.SelectedIndex });
        };

        WireSlider(SoftnessSlider, SoftnessLabel, v => p => p with { Softness = v });
        WireSlider(InputMinSlider, InputMinLabel, v => p => p with { InputMinimum = v });
        WireSlider(InputMaxSlider, InputMaxLabel, v => p => p with { InputMaximum = v });
        WireSlider(OutputMinSlider, OutputMinLabel, v => p => p with { Minimum = v });
        WireSlider(OutputMaxSlider, OutputMaxLabel, v => p => p with { Maximum = v });
        WireSlider(TransitionWidthSlider, TransitionWidthLabel, v => p => p with { TransitionWidth = v });
        WireSlider(FlatLevelSlider, FlatLevelLabel, v => p => p with { FlatLevel = v });
        WireSlider(PressureEmaSlider, PressureEmaLabel, v => p => p with { EmaSmoothing = v });
        WireSlider(PositionEmaSlider, PositionEmaLabel, v => p => p with { PositionEmaSmoothing = v });

        MinApproachClampRadio.IsCheckedChanged += (_, _) =>
        {
            if (_suppressCurveControlEvents) return;
            if (MinApproachClampRadio.IsChecked == true)
                UpdateParams(p => p with { MinApproach = MinApproach.Clamp });
        };
        MinApproachCutRadio.IsCheckedChanged += (_, _) =>
        {
            if (_suppressCurveControlEvents) return;
            if (MinApproachCutRadio.IsChecked == true)
                UpdateParams(p => p with { MinApproach = MinApproach.Cut });
        };

        UpdateAllSliderLabels();
        PressureChart.Params = _curveParams;
    }

    private void WireSlider(Slider slider, TextBlock label, Func<double, Func<PressureCurveParams, PressureCurveParams>> patch)
    {
        slider.PropertyChanged += (_, e) =>
        {
            if (_suppressCurveControlEvents || e.Property.Name != "Value") return;
            UpdateParams(patch(slider.Value));
            label.Text = FormatSliderValue(slider.Value);
        };
    }

    private void UpdateParams(Func<PressureCurveParams, PressureCurveParams> patch)
    {
        _curveParams = patch(_curveParams);
        PressureChart.Params = _curveParams;
    }

    private void UpdateAllSliderLabels()
    {
        SoftnessLabel.Text = FormatSliderValue(SoftnessSlider.Value);
        InputMinLabel.Text = FormatSliderValue(InputMinSlider.Value);
        InputMaxLabel.Text = FormatSliderValue(InputMaxSlider.Value);
        OutputMinLabel.Text = FormatSliderValue(OutputMinSlider.Value);
        OutputMaxLabel.Text = FormatSliderValue(OutputMaxSlider.Value);
        TransitionWidthLabel.Text = FormatSliderValue(TransitionWidthSlider.Value);
        FlatLevelLabel.Text = FormatSliderValue(FlatLevelSlider.Value);
        PressureEmaLabel.Text = FormatSliderValue(PressureEmaSlider.Value);
        PositionEmaLabel.Text = FormatSliderValue(PositionEmaSlider.Value);
    }

    private static string FormatSliderValue(double v)
        => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatSmoothingOrder(SmoothingOrder so) => so switch
    {
        SmoothingOrder.SmoothThenCurve => "Smooth → Curve",
        SmoothingOrder.CurveThenSmooth => "Curve → Smooth",
        _ => so.ToString(),
    };

    // ── Session lifecycle ────────────────────────────────────────

    private void StartSession()
    {
        if (_apis.Count == 0 || ApiCombo.SelectedIndex < 0) return;

        _session?.Stop();
        _session?.Dispose();

        var api = _apis[ApiCombo.SelectedIndex];
        _session = api == InputApi.AvaloniaPointer
            ? new AvaloniaPointerSession(CanvasArea)
            : PenSessionFactory.Create(api);
        ResetStrokeState();

        EnsureSurfaces();

        IntPtr hwnd = IntPtr.Zero;
        if (TryGetPlatformHandle() is { } handle)
            hwnd = handle.Handle;

        var error = _session.Start(hwnd);
        if (error != null)
        {
            Title = $"PenDynamicsLab - {error}";
            _session.Dispose();
            _session = null;
            return;
        }

        Title = "PenDynamicsLab";
        _renderTimer.Start();
    }

    private void ResetStrokeState()
    {
        _activeCanvas = ActiveCanvas.None;
        _lastDrawPos = null;
        _smoothedPos = null;
        _smoothedPressure = null;
        PressureChart.LiveRawPressure = null;
        PressureChart.LivePressure = null;
    }

    // ── Pressure pipeline ───────────────────────────────────────

    private readonly record struct PressurePipelineResult(
        double Raw, double PreCurve, double Output);

    private PressurePipelineResult ProcessPressure(double raw)
    {
        double smoothing = Math.Clamp(_curveParams.EmaSmoothing, 0, EmaConstants.Max);
        double Smooth(double v)
        {
            if (smoothing <= 0) { _smoothedPressure = v; return v; }
            if (_smoothedPressure is not { } prev) { _smoothedPressure = v; return v; }
            double alpha = 1 - smoothing;
            double next = prev + alpha * (v - prev);
            _smoothedPressure = next;
            return next;
        }

        if (_curveParams.SmoothingOrder == SmoothingOrder.CurveThenSmooth)
        {
            double curved = CurveMath.ApplyPressureCurve(raw, _curveParams);
            double smoothed = Smooth(curved);
            // In this order the chart's "live" indicator shows the raw input — the smoothing
            // happens after the curve, so there's no distinct pre-curve value to highlight.
            return new PressurePipelineResult(Raw: raw, PreCurve: raw, Output: smoothed);
        }
        else
        {
            double smoothed = Smooth(raw);
            double curved = CurveMath.ApplyPressureCurve(smoothed, _curveParams);
            return new PressurePipelineResult(Raw: raw, PreCurve: smoothed, Output: curved);
        }
    }

    private Point SmoothPosition(Point raw)
    {
        double smoothing = Math.Clamp(_curveParams.PositionEmaSmoothing, 0, EmaConstants.Max);
        if (smoothing <= 0) { _smoothedPos = raw; return raw; }
        if (_smoothedPos is not { } prev) { _smoothedPos = raw; return raw; }
        double alpha = 1 - smoothing;
        var next = new Point(prev.X + alpha * (raw.X - prev.X), prev.Y + alpha * (raw.Y - prev.Y));
        _smoothedPos = next;
        return next;
    }

    // ── Render timer ─────────────────────────────────────────────

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        if (_session == null) return;

        var points = _session.DrainPoints();
        if (points.Length == 0)
        {
            if ((DateTime.UtcNow - _lastPointTime).TotalMilliseconds > 200)
            {
                ProximityDot.Fill = Brushes.Gray;
                ProximityLabel.Text = "Out";
                if (_activeCanvas != ActiveCanvas.None)
                {
                    ResetStrokeState();
                }
            }
            return;
        }

        if (_processed == null || _raw == null || _processed.Canvas == null || _raw.Canvas == null)
        {
            EnsureSurfaces();
            if (_processed?.Canvas == null || _raw?.Canvas == null) return;
        }

        int maxP = _session.MaxPressure;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        bool processedDirty = false, rawDirty = false;

        foreach (var pt in points)
        {
            // Determine which sub-canvas the pen is over by translating screen coords into each
            // host's local frame. The host where local Y ∈ [0, height] wins.
            Point clientPt;
            try
            {
                var screenPt = new PixelPoint((int)pt.DesktopX, (int)pt.DesktopY);
                clientPt = topLevel.PointToClient(screenPt);
            }
            catch
            {
                _lastDrawPos = null;
                continue;
            }

            var (over, localPt) = ResolveActiveCanvas(topLevel, clientPt);
            if (over == ActiveCanvas.None)
            {
                _lastDrawPos = null;
                continue;
            }

            // Switching canvases mid-stroke restarts the smoothing state so segments don't bleed across.
            if (over != _activeCanvas)
            {
                _activeCanvas = over;
                _lastDrawPos = null;
                _smoothedPos = null;
                _smoothedPressure = null;
                if (pt.Pressure > 0) PickStrokeColor();
            }

            var smoothedPos = SmoothPosition(localPt);

            double rawPressure = maxP > 0 ? (double)pt.Pressure / maxP : 0;
            var pipeline = ProcessPressure(rawPressure);

            if (rawPressure > 0)
            {
                if (_lastDrawPos is { } from)
                {
                    DrawSegment(_processed!.Canvas!, from, smoothedPos,
                        SizeFor(pipeline.Output), OpacityFor(pipeline.Output),
                        skipIfZero: !_drawZeroPressure && pipeline.Output <= 0);
                    DrawSegment(_raw!.Canvas!, from, smoothedPos,
                        SizeFor(rawPressure), OpacityFor(rawPressure),
                        skipIfZero: false);
                    processedDirty = true;
                    rawDirty = true;
                }
                _lastDrawPos = smoothedPos;
            }
            else
            {
                _lastDrawPos = null;
            }

            UpdateTelemetry(pt, clientPt, smoothedPos, maxP);
            PressureChart.LiveRawPressure = pipeline.Raw;
            PressureChart.LivePressure = pipeline.PreCurve;
        }

        if (processedDirty) _processed!.Present();
        if (rawDirty) _raw!.Present();

        _lastPointTime = DateTime.UtcNow;
    }

    private (ActiveCanvas, Point) ResolveActiveCanvas(TopLevel topLevel, Point clientPt)
    {
        var processedOrigin = ProcessedCanvasHost.TranslatePoint(new Point(0, 0), topLevel);
        if (processedOrigin is { } po)
        {
            var local = new Point(clientPt.X - po.X, clientPt.Y - po.Y);
            if (local.X >= 0 && local.X < ProcessedCanvasHost.Bounds.Width &&
                local.Y >= 0 && local.Y < ProcessedCanvasHost.Bounds.Height)
                return (ActiveCanvas.Processed, local);
        }
        var rawOrigin = RawCanvasHost.TranslatePoint(new Point(0, 0), topLevel);
        if (rawOrigin is { } ro)
        {
            var local = new Point(clientPt.X - ro.X, clientPt.Y - ro.Y);
            if (local.X >= 0 && local.X < RawCanvasHost.Bounds.Width &&
                local.Y >= 0 && local.Y < RawCanvasHost.Bounds.Height)
                return (ActiveCanvas.Raw, local);
        }
        return (ActiveCanvas.None, default);
    }

    private float SizeFor(double pressure)
    {
        if (_pressureControl == PressureControl.Opacity) return (float)_brushSize;
        return (float)Math.Max(1, pressure * _brushSize);
    }

    private float OpacityFor(double pressure)
    {
        if (_pressureControl == PressureControl.Opacity)
            return (float)Math.Max(0.02, pressure);
        return 1f;
    }

    private void DrawSegment(SKCanvas canvas, Point from, Point to, float strokeWidth, float opacity, bool skipIfZero)
    {
        if (skipIfZero) return;
        byte alpha = (byte)Math.Clamp(opacity * 255, 0, 255);
        var color = _strokeColor.WithAlpha(alpha);
        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = strokeWidth,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        canvas.DrawLine((float)from.X, (float)from.Y, (float)to.X, (float)to.Y, paint);
    }

    private void UpdateTelemetry(PenPoint pt, Point clientPt, Point canvasLocal, int maxP)
    {
        ProximityDot.Fill = Brushes.LimeGreen;
        ProximityLabel.Text = "Proximity";
        CursorLabel.Text = $"Cursor: {pt.Cursor}";

        RawPosLabel.Text = $"Raw: {pt.RawX},{pt.RawY}";
        ScreenPosLabel.Text = $"Screen: {pt.DesktopX:F0},{pt.DesktopY:F0}";
        AppPosLabel.Text = $"App: {clientPt.X:F0},{clientPt.Y:F0}";
        CanvasPosLabel.Text = $"Canvas: {canvasLocal.X:F1},{canvasLocal.Y:F1}";

        float pct = maxP > 0 ? (float)pt.Pressure / maxP * 100f : 0f;
        RawPressureLabel.Text = $"Raw: {pt.Pressure}";
        NormPressureLabel.Text = $"Norm: {pct:F1}%";

        AzimuthLabel.Text = $"Azimuth: {pt.Azimuth:F1}";
        AltitudeLabel.Text = $"Altitude: {pt.Altitude:F1}";
        TwistLabel.Text = $"Twist: {pt.Twist:F1}";
    }

    // ── Event handlers ───────────────────────────────────────────

    private void ApiCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) => StartSession();

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        _processed?.Clear();
        _raw?.Clear();
        ResetStrokeState();
    }
}
