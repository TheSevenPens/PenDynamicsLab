using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Linq;
using PenSession;
using PenSession.Avalonia;
using PenDynamicsLab.Curves;
using SkiaSharp;

namespace PenDynamicsLab;

public partial class MainWindow : Window
{
    private IPenSession? _session;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private Point? _lastCanvasPoint;
    private double _brushSize = 6;
    private IReadOnlyList<InputApi> _apis = [];
    private DateTime _lastPointTime;

    // Skia bitmap-backed canvas.
    private SKBitmap? _skBitmap;
    private SKCanvas? _skCanvas;
    private WriteableBitmap? _avBitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;

    // Pressure curve state.
    private PressureCurveParams _curveParams = PressureCurveParams.Default;
    private bool _suppressCurveControlEvents;

    public MainWindow()
    {
        InitializeComponent();

        _renderTimer.Tick += RenderTimer_Tick;

        BrushSizeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                _brushSize = BrushSizeSlider.Value;
                BrushSizeLabel.Text = $"{(int)_brushSize} px";
            }
        };

        CanvasArea.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name is "Bounds")
                EnsureBitmap();
        };

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

            if (ApiCombo.Items.Count > 0)
                ApiCombo.SelectedIndex = 0;
        };

        Closing += (_, _) =>
        {
            _renderTimer.Stop();
            _session?.Stop();
            _session?.Dispose();
            _skCanvas?.Dispose();
            _skBitmap?.Dispose();
        };
    }

    // ── Pressure curve controls ──────────────────────────────────

    private void InitializeCurveControls()
    {
        foreach (var ct in Enum.GetValues<CurveType>())
            CurveTypeCombo.Items.Add(ct.ToString());

        _suppressCurveControlEvents = true;
        CurveTypeCombo.SelectedIndex = (int)_curveParams.CurveType;
        SoftnessSlider.Value = _curveParams.Softness;
        InputMinSlider.Value = _curveParams.InputMinimum;
        InputMaxSlider.Value = _curveParams.InputMaximum;
        OutputMinSlider.Value = _curveParams.Minimum;
        OutputMaxSlider.Value = _curveParams.Maximum;
        TransitionWidthSlider.Value = _curveParams.TransitionWidth;
        FlatLevelSlider.Value = _curveParams.FlatLevel;
        MinApproachClampRadio.IsChecked = _curveParams.MinApproach == MinApproach.Clamp;
        MinApproachCutRadio.IsChecked = _curveParams.MinApproach == MinApproach.Cut;
        _suppressCurveControlEvents = false;

        CurveTypeCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressCurveControlEvents) return;
            if (CurveTypeCombo.SelectedIndex >= 0)
                UpdateParams(p => p with { CurveType = (CurveType)CurveTypeCombo.SelectedIndex });
        };

        WireSlider(SoftnessSlider, SoftnessLabel, v => p => p with { Softness = v });
        WireSlider(InputMinSlider, InputMinLabel, v => p => p with { InputMinimum = v });
        WireSlider(InputMaxSlider, InputMaxLabel, v => p => p with { InputMaximum = v });
        WireSlider(OutputMinSlider, OutputMinLabel, v => p => p with { Minimum = v });
        WireSlider(OutputMaxSlider, OutputMaxLabel, v => p => p with { Maximum = v });
        WireSlider(TransitionWidthSlider, TransitionWidthLabel, v => p => p with { TransitionWidth = v });
        WireSlider(FlatLevelSlider, FlatLevelLabel, v => p => p with { FlatLevel = v });

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

        // Initial label sync + chart push
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
    }

    private static string FormatSliderValue(double v)
        => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    // ── Skia bitmap management ───────────────────────────────────

    private void EnsureBitmap()
    {
        int w = (int)CanvasArea.Bounds.Width;
        int h = (int)CanvasArea.Bounds.Height;
        if (w <= 0 || h <= 0) return;
        if (_skBitmap != null && _bitmapWidth == w && _bitmapHeight == h) return;

        var oldBitmap = _skBitmap;
        var oldCanvas = _skCanvas;

        _skBitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        _skCanvas = new SKCanvas(_skBitmap);
        _bitmapWidth = w;
        _bitmapHeight = h;

        _skCanvas.Clear(new SKColor(0xF0, 0xF0, 0xF0));

        if (oldBitmap != null)
        {
            _skCanvas.DrawBitmap(oldBitmap, 0, 0);
            oldCanvas?.Dispose();
            oldBitmap.Dispose();
        }

        _avBitmap = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            global::Avalonia.Platform.PixelFormat.Bgra8888,
            global::Avalonia.Platform.AlphaFormat.Premul);

        CopyToAvBitmap();
        DrawImage.Source = _avBitmap;
    }

    private void CopyToAvBitmap()
    {
        if (_skBitmap == null || _avBitmap == null) return;

        using var fb = _avBitmap.Lock();
        unsafe
        {
            var src = _skBitmap.GetPixels();
            var dst = fb.Address;
            int bytes = _bitmapWidth * _bitmapHeight * 4;
            Buffer.MemoryCopy((void*)src, (void*)dst, bytes, bytes);
        }
    }

    private void ClearBitmap()
    {
        _skCanvas?.Clear(new SKColor(0xF0, 0xF0, 0xF0));
        CopyToAvBitmap();
        DrawImage.InvalidateVisual();
    }

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
        _lastCanvasPoint = null;

        EnsureBitmap();

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

    // ── Render timer ─────────────────────────────────────────────

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        if (_session == null || _skCanvas == null) return;

        var points = _session.DrainPoints();
        if (points.Length == 0)
        {
            if ((DateTime.UtcNow - _lastPointTime).TotalMilliseconds > 200)
            {
                ProximityDot.Fill = Brushes.Gray;
                ProximityLabel.Text = "Out";
            }
            return;
        }

        int maxP = _session.MaxPressure;
        bool drew = false;

        foreach (var pt in points)
        {
            Point canvasPt;
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) continue;

                var screenPt = new PixelPoint((int)pt.DesktopX, (int)pt.DesktopY);
                var clientPt = topLevel.PointToClient(screenPt);
                var canvasOrigin = CanvasArea.TranslatePoint(new Point(0, 0), topLevel);
                if (canvasOrigin == null) continue;

                canvasPt = new Point(
                    clientPt.X - canvasOrigin.Value.X,
                    clientPt.Y - canvasOrigin.Value.Y);
            }
            catch
            {
                _lastCanvasPoint = null;
                continue;
            }

            if (canvasPt.X < 0 || canvasPt.X > _bitmapWidth ||
                canvasPt.Y < 0 || canvasPt.Y > _bitmapHeight)
            {
                _lastCanvasPoint = null;
                continue;
            }

            if (_lastCanvasPoint is { } from && pt.Pressure > 0 && maxP > 0)
            {
                float width = (float)pt.Pressure / maxP * (float)_brushSize + 0.5f;

                using var paint = new SKPaint
                {
                    Color = SKColors.Black,
                    StrokeWidth = width,
                    StrokeCap = SKStrokeCap.Round,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke
                };

                _skCanvas.DrawLine(
                    (float)from.X, (float)from.Y,
                    (float)canvasPt.X, (float)canvasPt.Y,
                    paint);
                drew = true;
            }

            _lastCanvasPoint = canvasPt;
        }

        if (drew)
        {
            CopyToAvBitmap();
            DrawImage.InvalidateVisual();
        }

        // Update telemetry.
        var last = points[^1];
        _lastPointTime = DateTime.UtcNow;

        ProximityDot.Fill = Brushes.LimeGreen;
        ProximityLabel.Text = "Proximity";
        CursorLabel.Text = $"Cursor: {last.Cursor}";

        RawPosLabel.Text = $"Raw: {last.RawX},{last.RawY}";
        ScreenPosLabel.Text = $"Screen: {last.DesktopX:F0},{last.DesktopY:F0}";

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var screenPt = new PixelPoint((int)last.DesktopX, (int)last.DesktopY);
                var clientPt = topLevel.PointToClient(screenPt);
                AppPosLabel.Text = $"App: {clientPt.X:F0},{clientPt.Y:F0}";
            }
        }
        catch { AppPosLabel.Text = "App: --,--"; }

        CanvasPosLabel.Text = _lastCanvasPoint is { } cp
            ? $"Canvas: {cp.X:F1},{cp.Y:F1}" : "Canvas: --,--";

        float pct = maxP > 0 ? (float)last.Pressure / maxP * 100f : 0f;
        RawPressureLabel.Text = $"Raw: {last.Pressure}";
        NormPressureLabel.Text = $"Norm: {pct:F1}%";

        AzimuthLabel.Text = $"Azimuth: {last.Azimuth:F1}";
        AltitudeLabel.Text = $"Altitude: {last.Altitude:F1}";
        TwistLabel.Text = $"Twist: {last.Twist:F1}";
    }

    // ── Event handlers ───────────────────────────────────────────

    private void ApiCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        StartSession();
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        ClearBitmap();
        _lastCanvasPoint = null;
    }
}
