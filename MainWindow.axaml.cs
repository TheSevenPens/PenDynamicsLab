using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Linq;
using WinPenKit;
using WinPenKit.Avalonia;
using PenDynamicsLab.Controls;
using PenDynamicsLab.Curves;
using PenDynamicsLab.Drawing;
using PenDynamicsLab.Persistence;
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
    private SKColor _strokeColor = BlackStrokeColor;

    // Single BrushRibbon instance reparented into whichever stroke tab is active —
    // keeps brush settings synced across tabs without duplicating UI state.
    private readonly Controls.BrushRibbon BrushRibbon = new();
    private int _lastColorIndex = -1;

    // Stroke-local pressure-smoothing state. Reset whenever the pen lifts or the active canvas changes.
    private enum ActiveCanvas { None, Processed, Raw }
    private ActiveCanvas _activeCanvas = ActiveCanvas.None;
    private Point? _lastDrawPos;
    private double? _smoothedPressure;
    private readonly Random _rng = new();

    private readonly PresetStore _presetStore = new();

    public MainWindow()
    {
        InitializeComponent();

        _renderTimer.Tick += RenderTimer_Tick;

        // The processed surface is shared across both the Stroke tab and the Stroke compare
        // tab — drawing on either is the same content. The raw surface is unique to the
        // compare tab.
        _processed = new DrawSurface();
        _processed.AddHost(StrokeView.Image);
        _processed.AddHost(CompareProcessedView.Image);
        _raw = new DrawSurface();
        _raw.AddHost(CompareRawView.Image);

        // Resize bitmaps to follow whichever host is currently visible (the active tab's).
        StrokeView.Host.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Bounds") EnsureSurfaces();
        };
        CompareProcessedView.Host.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Bounds") EnsureSurfaces();
        };
        CompareRawView.Host.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Bounds") EnsureSurfaces();
        };
        RightTabs.SelectionChanged += (_, _) =>
        {
            EnsureSurfaces();
            ResetStrokeState();
            UpdateBrushRibbonHost();
        };
        UpdateBrushRibbonHost();

        // Dragging to a monitor with different DPI keeps the window the same size in
        // DIPs, so no Bounds change fires — the surfaces have to be re-allocated off
        // the scaling change itself or they'd stay at the old pixel density.
        ScalingChanged += (_, _) => EnsureSurfaces();

        // Save buttons on each canvas view.
        StrokeView.SaveRequested += async (_, _) => await SaveSurfaceAsPngAsync(_processed, "stroke.png");
        CompareProcessedView.SaveRequested += async (_, _) => await SaveSurfaceAsPngAsync(_processed, "processed.png");
        CompareRawView.SaveRequested += async (_, _) => await SaveSurfaceAsPngAsync(_raw, "unprocessed.png");

        BrushRibbon.ClearRequested += (_, _) => ClearCanvases();
        InitializeCurveControls();
        InitializeBezierPresets();
        InitializeResponseSection();
        RebuildUserPresetList();

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

        // Delete / Backspace clear the canvas — but only when no text input has focus,
        // otherwise these keys would steal characters from a slider value edit or the
        // preset name TextBox.
        KeyDown += (_, e) =>
        {
            if (e.Key != global::Avalonia.Input.Key.Delete && e.Key != global::Avalonia.Input.Key.Back) return;
            if (e.Handled) return;
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (focused is TextBox) return;
            ClearCanvases();
            e.Handled = true;
        };
    }

    // ── Surface management ──────────────────────────────────────

    private void EnsureSurfaces()
    {
        // Bounds are in DIPs; the surfaces allocate at DIP * RenderScaling physical
        // pixels so strokes render at the display's true resolution rather than being
        // magnified by the compositor. Read the scaling every time — it changes when
        // the window is dragged to a monitor with different DPI.
        double scale = RenderScaling;

        // IsEffectivelyVisible is true only for the active tab's content — using it
        // (rather than checking Bounds) avoids picking a host whose layout from a
        // previous tab is still cached.
        var processedHost = StrokeView.IsEffectivelyVisible ? StrokeView.Host
                          : CompareProcessedView.IsEffectivelyVisible ? CompareProcessedView.Host
                          : null;
        if (processedHost is { } ph && ph.Bounds.Width > 0 && ph.Bounds.Height > 0)
            _processed?.EnsureSize(ph.Bounds.Width, ph.Bounds.Height, scale);

        if (CompareRawView.IsEffectivelyVisible &&
            CompareRawView.Host.Bounds.Width > 0 && CompareRawView.Host.Bounds.Height > 0)
            _raw?.EnsureSize(CompareRawView.Host.Bounds.Width, CompareRawView.Host.Bounds.Height, scale);
    }

    // ── Brush controls ──────────────────────────────────────────

    private void PickStrokeColor()
    {
        if (BrushRibbon.ColorMode == ColorMode.Black)
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

        foreach (var st in Enum.GetValues<SmoothingType>())
            SmoothingTypeCombo.Items.Add(FormatSmoothingType(st));

        _suppressCurveControlEvents = true;
        CurveTypeCombo.SelectedIndex = (int)_curveParams.CurveType;
        SmoothingTypeCombo.SelectedIndex = (int)_curveParams.SmoothingType;
        SmoothThenCurveRadio.IsChecked = _curveParams.SmoothingOrder == SmoothingOrder.SmoothThenCurve;
        CurveThenSmoothRadio.IsChecked = _curveParams.SmoothingOrder == SmoothingOrder.CurveThenSmooth;
        SoftnessSlider.Value = _curveParams.Softness;
        InputMinSlider.Value = _curveParams.InputMinimum;
        InputMaxSlider.Value = _curveParams.InputMaximum;
        OutputMinSlider.Value = _curveParams.Minimum;
        OutputMaxSlider.Value = _curveParams.Maximum;
        FlatLevelSlider.Value = _curveParams.FlatLevel;
        PressureEmaSlider.Value = _curveParams.EmaSmoothing;
        MinApproachClampRadio.IsChecked = _curveParams.MinApproach == MinApproach.Clamp;
        MinApproachCutRadio.IsChecked = _curveParams.MinApproach == MinApproach.Cut;
        _suppressCurveControlEvents = false;

        CurveTypeCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressCurveControlEvents || CurveTypeCombo.SelectedIndex < 0) return;
            UpdateParams(p => p with { CurveType = (CurveType)CurveTypeCombo.SelectedIndex });
        };
        SmoothingTypeCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressCurveControlEvents || SmoothingTypeCombo.SelectedIndex < 0) return;
            UpdateParams(p => p with { SmoothingType = (SmoothingType)SmoothingTypeCombo.SelectedIndex });
        };
        SmoothThenCurveRadio.IsCheckedChanged += (_, _) =>
        {
            if (_suppressCurveControlEvents) return;
            if (SmoothThenCurveRadio.IsChecked == true)
                UpdateParams(p => p with { SmoothingOrder = SmoothingOrder.SmoothThenCurve });
        };
        CurveThenSmoothRadio.IsCheckedChanged += (_, _) =>
        {
            if (_suppressCurveControlEvents) return;
            if (CurveThenSmoothRadio.IsChecked == true)
                UpdateParams(p => p with { SmoothingOrder = SmoothingOrder.CurveThenSmooth });
        };

        WireSlider(SoftnessSlider, v => p => p with { Softness = v });
        WireSlider(InputMinSlider, v => p => p with { InputMinimum = v });
        WireSlider(InputMaxSlider, v => p => p with { InputMaximum = v });
        WireSlider(OutputMinSlider, v => p => p with { Minimum = v });
        WireSlider(OutputMaxSlider, v => p => p with { Maximum = v });
        WireSlider(FlatLevelSlider, v => p => p with { FlatLevel = v });
        WireSlider(PressureEmaSlider, v => p => p with { EmaSmoothing = v });

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

        UpdateBezierToolbar();
        UpdateCardStatuses();
        PressureChart.Params = _curveParams;

        // The chart writes back to Params when the user drags nodes / handles or uses the
        // right-click context menu. Mirror those changes back into the controls UI.
        PressureChart.PropertyChanged += (_, e) =>
        {
            if (e.Property != PressureChartControl.ParamsProperty) return;
            if (e.NewValue is not PressureCurveParams newParams) return;
            if (ReferenceEquals(newParams, _curveParams)) return;
            _curveParams = newParams;
            ResponseChart.Params = _curveParams;
            SyncCurveControlsFromParams();
        };
    }

    private void SyncCurveControlsFromParams()
    {
        _suppressCurveControlEvents = true;
        CurveTypeCombo.SelectedIndex = (int)_curveParams.CurveType;
        SmoothingTypeCombo.SelectedIndex = (int)_curveParams.SmoothingType;
        SmoothThenCurveRadio.IsChecked = _curveParams.SmoothingOrder == SmoothingOrder.SmoothThenCurve;
        CurveThenSmoothRadio.IsChecked = _curveParams.SmoothingOrder == SmoothingOrder.CurveThenSmooth;
        SoftnessSlider.Value = _curveParams.Softness;
        InputMinSlider.Value = _curveParams.InputMinimum;
        InputMaxSlider.Value = _curveParams.InputMaximum;
        OutputMinSlider.Value = _curveParams.Minimum;
        OutputMaxSlider.Value = _curveParams.Maximum;
        FlatLevelSlider.Value = _curveParams.FlatLevel;
        PressureEmaSlider.Value = _curveParams.EmaSmoothing;
        MinApproachClampRadio.IsChecked = _curveParams.MinApproach == MinApproach.Clamp;
        MinApproachCutRadio.IsChecked = _curveParams.MinApproach == MinApproach.Cut;
        _suppressCurveControlEvents = false;

        UpdateBezierToolbar();
        UpdateCardStatuses();
    }

    private void UpdateBezierToolbar()
    {
        var ct = _curveParams.CurveType;

        // Visibility per curve type. Smoothing / response / presets sections live below
        // and are independent of the curve type.
        bool hasSoftness = ct is CurveType.Basic or CurveType.Extended or CurveType.Sigmoid;
        bool hasRangeControls = ct is CurveType.Extended or CurveType.Sigmoid;
        bool isBezier = ct == CurveType.Bezier;
        bool isFlat = ct == CurveType.Flat;

        // Sigmoid only makes sense with positive steepness (k = softness * 14), and the
        // top of the range gets numerically unstable near ±1 — clamp to [0, 0.95].
        if (ct == CurveType.Sigmoid)
        {
            SoftnessSlider.Minimum = 0;
            SoftnessSlider.Maximum = 0.95;
        }
        else
        {
            SoftnessSlider.Minimum = -0.9;
            SoftnessSlider.Maximum = 0.9;
        }
        if (_curveParams.Softness < SoftnessSlider.Minimum || _curveParams.Softness > SoftnessSlider.Maximum)
        {
            double clamped = Math.Clamp(_curveParams.Softness, SoftnessSlider.Minimum, SoftnessSlider.Maximum);
            _curveParams = _curveParams with { Softness = clamped };
            PressureChart.Params = _curveParams;
            ResponseChart.Params = _curveParams;
            _suppressCurveControlEvents = true;
            SoftnessSlider.Value = clamped;
            _suppressCurveControlEvents = false;
        }

        SoftnessSlider.IsVisible = hasSoftness;
        InputMinSlider.IsVisible = hasRangeControls;
        InputMaxSlider.IsVisible = hasRangeControls;
        OutputMinSlider.IsVisible = hasRangeControls;
        OutputMaxSlider.IsVisible = hasRangeControls;
        MinApproachPanel.IsVisible = hasRangeControls;
        FlatLevelSlider.IsVisible = isFlat;
        BezierToolbar.IsVisible = isBezier;

        // Passthrough smoothing ignores the amount, so hide it — same convention as the
        // curve card, where Passthrough hides softness and the range controls.
        PressureEmaSlider.IsVisible = _curveParams.SmoothingType != SmoothingType.Passthrough;

        // Range values are driven by dragging the pink/cyan nodes on the chart, so the
        // slider track would be redundant — show only label + value.
        InputMinSlider.ShowSlider = false;
        InputMaxSlider.ShowSlider = false;
        OutputMinSlider.ShowSlider = false;
        OutputMaxSlider.ShowSlider = false;

        if (isBezier)
        {
            int count = CurveMath.NormalizeBezierPoints(_curveParams.BezierPoints).Length;
            BezierCountLabel.Text = $"{count}/16";
            BezierAddButton.IsEnabled = count < 16;
            BezierRemoveButton.IsEnabled = count > 2;
        }
    }

    // ── Card headers ────────────────────────────────────────────

    /// <summary>
    /// Refresh the card header suffixes, so each card's state is readable while collapsed.
    /// Curve and Smoothing show "(OFF)" when the stage currently does nothing: smoothing
    /// when passthrough or its amount is zero, the curve when it maps input to output
    /// unchanged (see <see cref="CurveMath.IsIdentity"/>). Processing shows the order as
    /// "(S → C)" or "(C → S)".
    /// </summary>
    private void UpdateCardStatuses()
    {
        CurveCard.Status = CurveMath.IsIdentity(_curveParams) ? "(OFF)" : "";

        bool smoothingActive = _curveParams.SmoothingType != SmoothingType.Passthrough
                            && _curveParams.EmaSmoothing > 0;
        SmoothingCard.Status = smoothingActive ? "" : "(OFF)";

        ProcessingOrderCard.Status =
            _curveParams.SmoothingOrder == SmoothingOrder.SmoothThenCurve ? "(S → C)" : "(C → S)";
    }

    // ── Section resets ──────────────────────────────────────────

    private void CurveReset_Click(object? sender, RoutedEventArgs e)
    {
        var d = PressureCurveParams.Default;
        UpdateParams(p => p with
        {
            CurveType = d.CurveType,
            Softness = d.Softness,
            InputMinimum = d.InputMinimum,
            InputMaximum = d.InputMaximum,
            Minimum = d.Minimum,
            Maximum = d.Maximum,
            MinApproach = d.MinApproach,
            FlatLevel = d.FlatLevel,
            BezierPoints = d.BezierPoints,
        });
        SyncCurveControlsFromParams();
    }

    private void SmoothingReset_Click(object? sender, RoutedEventArgs e)
    {
        var d = PressureCurveParams.Default;
        UpdateParams(p => p with { SmoothingType = d.SmoothingType, EmaSmoothing = d.EmaSmoothing });
        SyncCurveControlsFromParams();
    }

    private void BezierAdd_Click(object? sender, RoutedEventArgs e) => PressureChart.AddBezierPointAtLargestGap();
    private void BezierRemove_Click(object? sender, RoutedEventArgs e) => PressureChart.RemoveSelectedBezierPoint();

    // ── Bezier presets ──────────────────────────────────────────

    private void InitializeBezierPresets()
    {
        foreach (var preset in BezierPresets.All)
            BezierPresetCombo.Items.Add(preset.Name);

        BezierPresetCombo.SelectionChanged += (_, _) =>
        {
            if (BezierPresetCombo.SelectedIndex < 0) return;
            var preset = BezierPresets.All[BezierPresetCombo.SelectedIndex];
            UpdateParams(p => p with { BezierPoints = preset.Points });
            // Reset selection so the same preset can be re-applied later.
            BezierPresetCombo.SelectedIndex = -1;
        };
    }

    // ── User presets ────────────────────────────────────────────

    // "Save settings" reveals an inline name box rather than opening a dialog, keeping
    // the card compact when it isn't being used.
    private void PresetSave_Click(object? sender, RoutedEventArgs e)
    {
        PresetNameRow.IsVisible = true;
        PresetSaveButton.IsVisible = false;
        PresetNameInput.Text = "";
        PresetNameInput.Focus();
    }

    private void PresetCancel_Click(object? sender, RoutedEventArgs e) => HidePresetNameRow();

    private void PresetConfirm_Click(object? sender, RoutedEventArgs e)
    {
        var name = PresetNameInput.Text?.Trim() ?? "";
        if (name.Length == 0) return;
        _presetStore.Save(name, _curveParams);
        HidePresetNameRow();
        RebuildUserPresetList();
    }

    private void HidePresetNameRow()
    {
        PresetNameRow.IsVisible = false;
        PresetSaveButton.IsVisible = true;
        PresetNameInput.Text = "";
    }

    // ── Pressure response section ───────────────────────────────

    private const string UploadJsonSentinel = "__upload__";

    private void InitializeResponseSection()
    {
        ResponseDataCombo.Items.Add("(none)");
        foreach (var s in PressureResponseLoader.Samples)
            ResponseDataCombo.Items.Add(s.Label);
        ResponseDataCombo.Items.Add("Upload JSON...");

        ResponseDataCombo.SelectionChanged += async (_, _) =>
        {
            int idx = ResponseDataCombo.SelectedIndex;
            if (idx <= 0) { SetResponseData(null); return; }

            // Last item is the upload picker.
            if (idx == ResponseDataCombo.Items.Count - 1)
            {
                await PickResponseJsonFileAsync();
                return;
            }
            try
            {
                var data = PressureResponseLoader.LoadSample(PressureResponseLoader.Samples[idx - 1].ResourceName);
                SetResponseData(data);
            }
            catch
            {
                SetResponseData(null);
            }
        };

        ResponseShowCurveEffectCheck.IsCheckedChanged += (_, _) =>
        {
            ResponseChart.ShowCurveEffect = ResponseShowCurveEffectCheck.IsChecked == true;
        };
        ResponseChart.ShowCurveEffect = true;

        // Default to the first bundled sample so the chart shows something interesting
        // immediately. Index 0 is "(none)", index 1 is the first sample.
        if (PressureResponseLoader.Samples.Count > 0) ResponseDataCombo.SelectedIndex = 1;
        else ResponseDataCombo.SelectedIndex = 0;
        ResponseChart.Params = _curveParams;
    }

    private void SetResponseData(PressureResponseData? data)
    {
        ResponseChart.Data = data;
        ResponseClearButton.IsEnabled = data != null;
        ResponseInfoLabel.Text = data is null
            ? ""
            : $"{data.InventoryId} — {data.Brand} {data.Pen} · {data.Tablet} · {data.Date} · {data.Records.Count} pts";
    }

    private void ResponseClear_Click(object? sender, RoutedEventArgs e)
    {
        ResponseDataCombo.SelectedIndex = 0;
        SetResponseData(null);
    }

    private async Task PickResponseJsonFileAsync()
    {
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp is null) return;

        var files = await sp.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select pressure-response JSON",
            AllowMultiple = false,
            FileTypeFilter = [new global::Avalonia.Platform.Storage.FilePickerFileType("JSON")
                { Patterns = new[] { "*.json" } }],
        });
        if (files.Count == 0)
        {
            ResponseDataCombo.SelectedIndex = 0;
            SetResponseData(null);
            return;
        }
        try
        {
            var local = files[0].TryGetLocalPath();
            if (local == null) { ResponseDataCombo.SelectedIndex = 0; return; }
            SetResponseData(PressureResponseLoader.LoadFromFile(local));
            // Keep the combo on "Upload JSON..." position so user knows we're showing custom data.
        }
        catch
        {
            SetResponseData(null);
            ResponseDataCombo.SelectedIndex = 0;
        }
    }

    // ── Image export ────────────────────────────────────────────

    private async void SaveFullChart_Click(object? sender, RoutedEventArgs e)
        => await SaveChartPngAsync(cropToPlot: false, "pressure-curve.png");

    private async void SavePlotArea_Click(object? sender, RoutedEventArgs e)
        => await SaveChartPngAsync(cropToPlot: true, "pressure-curve-plot.png");

    private async void CopyFullChart_Click(object? sender, RoutedEventArgs e)
        => await CopyChartPngAsync(cropToPlot: false);

    private async void CopyPlotArea_Click(object? sender, RoutedEventArgs e)
        => await CopyChartPngAsync(cropToPlot: true);

    /// <summary>
    /// Render the curve chart to PNG bytes at full display resolution, optionally
    /// cropped to just the plot area (dropping axis labels and titles).
    /// </summary>
    private byte[]? RenderChartPng(bool cropToPlot)
    {
        if (PressureChart.Bounds.Width <= 0 || PressureChart.Bounds.Height <= 0) return null;

        // Same DIP-vs-pixel issue as the stroke surfaces: Bounds are DIPs, so rendering
        // at 96 DPI produces an image at 1/RenderScaling of the on-screen resolution.
        // Sizing the target in physical pixels and tagging it 96 * scale makes
        // RenderTargetBitmap.Render scale the visual to match.
        double scale = RenderScaling;
        if (scale <= 0 || double.IsNaN(scale)) scale = 1;

        using var rtb = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(
                (int)Math.Round(PressureChart.Bounds.Width * scale),
                (int)Math.Round(PressureChart.Bounds.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        rtb.Render(PressureChart);

        using var ms = new MemoryStream();
        rtb.Save(ms);
        if (!cropToPlot) return ms.ToArray();

        // Crop in pixel space: the plot rect is in DIPs, so scale it to match the render.
        ms.Position = 0;
        using var full = SKBitmap.Decode(ms);
        if (full is null) return null;

        var plot = PressureChart.PlotRect;
        int left = Math.Clamp((int)Math.Round(plot.X * scale), 0, full.Width);
        int top = Math.Clamp((int)Math.Round(plot.Y * scale), 0, full.Height);
        int right = Math.Clamp((int)Math.Round((plot.X + plot.Width) * scale), 0, full.Width);
        int bottom = Math.Clamp((int)Math.Round((plot.Y + plot.Height) * scale), 0, full.Height);
        var subset = new SKRectI(left, top, right, bottom);
        if (subset.Width <= 0 || subset.Height <= 0) return null;

        using var cropped = new SKBitmap(subset.Width, subset.Height);
        if (!full.ExtractSubset(cropped, subset)) return null;
        using var image = SKImage.FromBitmap(cropped);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private async Task SaveChartPngAsync(bool cropToPlot, string suggestedName)
    {
        var png = RenderChartPng(cropToPlot);
        if (png is null) return;

        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp is null) return;

        var file = await sp.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save chart as PNG",
            SuggestedFileName = suggestedName,
            DefaultExtension = "png",
            FileTypeChoices = [new global::Avalonia.Platform.Storage.FilePickerFileType("PNG")
                { Patterns = new[] { "*.png" } }],
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(png);
    }

    private async Task CopyChartPngAsync(bool cropToPlot)
    {
        var png = RenderChartPng(cropToPlot);
        if (png is null) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        // Avalonia has no first-class "set image" clipboard API, so the bytes go on under
        // the PNG format name. Apps that accept PNG from the clipboard (browsers, most
        // image editors) will paste it; ones that only read CF_DIB may not see it.
        var data = new global::Avalonia.Input.DataObject();
        data.Set("PNG", png);
        try
        {
            await clipboard.SetDataObjectAsync(data);
            FlashChartStatus("Copied");
        }
        catch
        {
            FlashChartStatus("Copy failed");
        }
    }

    /// <summary>Briefly show a word next to the copy/save buttons, then clear it.</summary>
    private void FlashChartStatus(string text)
    {
        ChartStatusLabel.Text = text;
        DispatcherTimer.RunOnce(() => ChartStatusLabel.Text = "", TimeSpan.FromSeconds(2));
    }

    private async Task SaveSurfaceAsPngAsync(DrawSurface? surface, string suggestedName)
    {
        if (surface is null || surface.Width <= 0 || surface.Height <= 0) return;
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp is null) return;

        var file = await sp.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Save canvas as PNG",
            SuggestedFileName = suggestedName,
            DefaultExtension = "png",
            FileTypeChoices = [new global::Avalonia.Platform.Storage.FilePickerFileType("PNG")
                { Patterns = new[] { "*.png" } }],
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        surface.SavePng(stream);
    }

    // ── Driver warning ──────────────────────────────────────────

    private void DismissDriverWarning_Click(object? sender, RoutedEventArgs e)
        => DriverWarningBanner.IsVisible = false;

    private void RebuildUserPresetList()
    {
        PresetList.Children.Clear();
        PresetEmptyLabel.IsVisible = _presetStore.All.Count == 0;
        foreach (var preset in _presetStore.All.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var name = preset.Name;
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 1, 0, 1) };

            var deleteBtn = new Button { Content = "✕", Width = 24, Padding = new Thickness(0), FontSize = 10 };
            deleteBtn.Click += (_, _) =>
            {
                _presetStore.Delete(name);
                RebuildUserPresetList();
            };
            DockPanel.SetDock(deleteBtn, Dock.Right);

            var loadBtn = new Button { Content = "Load", Margin = new Thickness(4, 0), Padding = new Thickness(6, 1) };
            loadBtn.Click += (_, _) =>
            {
                if (_presetStore.Get(name) is { } p)
                {
                    _curveParams = p.Params;
                    PressureChart.Params = _curveParams;
                    SyncCurveControlsFromParams();
                }
            };
            DockPanel.SetDock(loadBtn, Dock.Right);

            var label = new TextBlock { Text = name, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };

            row.Children.Add(deleteBtn);
            row.Children.Add(loadBtn);
            row.Children.Add(label);
            PresetList.Children.Add(row);
        }
    }

    private void WireSlider(LabeledSlider slider, Func<double, Func<PressureCurveParams, PressureCurveParams>> patch)
    {
        slider.ValueChanged += (_, v) =>
        {
            if (_suppressCurveControlEvents) return;
            UpdateParams(patch(v));
        };
    }

    private static string FormatSmoothingType(SmoothingType st) => st switch
    {
        SmoothingType.Passthrough => "Passthrough",
        SmoothingType.Ema => "EMA",
        _ => st.ToString(),
    };

    private void UpdateParams(Func<PressureCurveParams, PressureCurveParams> patch)
    {
        _curveParams = patch(_curveParams);
        PressureChart.Params = _curveParams;
        ResponseChart.Params = _curveParams;
        UpdateBezierToolbar();
        UpdateCardStatuses();
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
        _smoothedPressure = null;
        PressureChart.LiveRawPressure = null;
        PressureChart.LivePressure = null;
        ResponseChart.LiveRawPressure = null;
        ResponseChart.LivePressure = null;
    }

    // ── Pressure pipeline ───────────────────────────────────────

    private readonly record struct PressurePipelineResult(
        double Raw, double PreCurve, double Output);

    private PressurePipelineResult ProcessPressure(double raw)
    {
        // Passthrough short-circuits to the same path as an amount of 0: no smoothing,
        // and the EMA state still tracks the input so switching back mid-stroke doesn't
        // jump from a stale value.
        double smoothing = _curveParams.SmoothingType == SmoothingType.Passthrough
            ? 0
            : Math.Clamp(_curveParams.EmaSmoothing, 0, EmaConstants.Max);
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

        // Telemetry should keep flowing even if a surface isn't sized yet (e.g., the
        // raw surface is only sized while the Compare tab is visible). Drawing is
        // null-guarded per-surface below.
        EnsureSurfaces();

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

            // Pressure pipeline + telemetry + chart indicators run for every pen point,
            // regardless of whether the pen is over a stroke canvas. This keeps the
            // Pressure response tab's chart live even though it has no canvas.
            double rawPressure = maxP > 0 ? (double)pt.Pressure / maxP : 0;
            var pipeline = ProcessPressure(rawPressure);

            UpdateTelemetry(pt, clientPt, over == ActiveCanvas.None ? null : (Point?)localPt, maxP);
            PressureChart.LiveRawPressure = pipeline.Raw;
            PressureChart.LivePressure = pipeline.PreCurve;
            ResponseChart.LiveRawPressure = pipeline.Raw;
            ResponseChart.LivePressure = pipeline.PreCurve;

            // Drawing requires the pen to be over a stroke canvas.
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
            }

            var drawPos = localPt;

            // A new stroke begins whenever pressure arrives with no segment in progress:
            // at pen-down, and again after the pen crosses to the other canvas. Keying off
            // the canvas change alone missed the common case — hovering over the canvas
            // consumes the change at zero pressure, so pressing down afterwards never
            // picked a colour and every stroke stayed the initial black.
            if (rawPressure > 0 && _lastDrawPos is null) PickStrokeColor();

            if (rawPressure > 0)
            {
                if (_lastDrawPos is { } from)
                {
                    if (_processed?.Canvas is { } pc)
                    {
                        DrawSegment(pc, from, drawPos,
                            SizeFor(pipeline.Output), OpacityFor(pipeline.Output),
                            skipIfZero: !BrushRibbon.DrawZeroPressure && pipeline.Output <= 0);
                        processedDirty = true;
                    }
                    if (_raw?.Canvas is { } rc)
                    {
                        DrawSegment(rc, from, drawPos,
                            SizeFor(rawPressure), OpacityFor(rawPressure),
                            skipIfZero: false);
                        rawDirty = true;
                    }
                }
                _lastDrawPos = drawPos;
            }
            else
            {
                _lastDrawPos = null;
            }
        }

        if (processedDirty) _processed!.Present();
        if (rawDirty) _raw!.Present();

        _lastPointTime = DateTime.UtcNow;
    }

    private (ActiveCanvas, Point) ResolveActiveCanvas(TopLevel topLevel, Point clientPt)
    {
        // Only probe canvases in the currently-visible tab. Stale layout on inactive
        // tab content can otherwise produce wrong local coords (manifests as an X/Y
        // displacement on whichever canvas wins the hit test by accident).
        if (StrokeView.IsEffectivelyVisible &&
            TryHitTest(StrokeView.Host) is { } a) return (ActiveCanvas.Processed, a);
        if (CompareProcessedView.IsEffectivelyVisible &&
            TryHitTest(CompareProcessedView.Host) is { } b) return (ActiveCanvas.Processed, b);
        if (CompareRawView.IsEffectivelyVisible &&
            TryHitTest(CompareRawView.Host) is { } c) return (ActiveCanvas.Raw, c);
        return (ActiveCanvas.None, default);

        Point? TryHitTest(Border host)
        {
            if (host.Bounds.Width <= 0 || host.Bounds.Height <= 0) return null;
            var origin = host.TranslatePoint(new Point(0, 0), topLevel);
            if (origin is null) return null;
            var local = new Point(clientPt.X - origin.Value.X, clientPt.Y - origin.Value.Y);
            if (local.X < 0 || local.X >= host.Bounds.Width ||
                local.Y < 0 || local.Y >= host.Bounds.Height) return null;
            return local;
        }
    }

    private float SizeFor(double pressure)
    {
        if (BrushRibbon.PressureControl == PressureControl.Opacity) return (float)BrushRibbon.BrushSize;
        return (float)Math.Max(1, pressure * BrushRibbon.BrushSize);
    }

    private float OpacityFor(double pressure)
    {
        if (BrushRibbon.PressureControl == PressureControl.Opacity)
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

    private void UpdateTelemetry(PenPoint pt, Point clientPt, Point? canvasLocal, int maxP)
    {
        ProximityDot.Fill = Brushes.LimeGreen;
        ProximityLabel.Text = "Proximity";
        CursorLabel.Text = $"Cursor: {pt.Cursor}";

        RawPosLabel.Text = $"Raw: {pt.RawX},{pt.RawY}";
        ScreenPosLabel.Text = $"Screen: {pt.DesktopX:F0},{pt.DesktopY:F0}";
        AppPosLabel.Text = $"App: {clientPt.X:F0},{clientPt.Y:F0}";
        CanvasPosLabel.Text = canvasLocal is { } cl
            ? $"Canvas: {cl.X:F1},{cl.Y:F1}" : "Canvas: --,--";

        float pct = maxP > 0 ? (float)pt.Pressure / maxP * 100f : 0f;
        RawPressureLabel.Text = $"Raw: {pt.Pressure}";
        NormPressureLabel.Text = $"Norm: {pct:F1}%";

        AzimuthLabel.Text = $"Azimuth: {pt.Azimuth:F1}";
        AltitudeLabel.Text = $"Altitude: {pt.Altitude:F1}";
        TwistLabel.Text = $"Twist: {pt.Twist:F1}";
    }

    // ── Event handlers ───────────────────────────────────────────

    private void ApiCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e) => StartSession();

    private void UpdateBrushRibbonHost()
    {
        // A control can have only one logical parent in Avalonia, so explicitly detach
        // from both slots before assigning to the active one — otherwise reassignment
        // throws.
        StrokeBrushSlot.Content = null;
        CompareBrushSlot.Content = null;

        var sel = RightTabs.SelectedItem;
        if (sel == StrokeTab) StrokeBrushSlot.Content = BrushRibbon;
        else if (sel == StrokeCompareTab) CompareBrushSlot.Content = BrushRibbon;
        // Other tabs (Pressure response): ribbon stays detached.
    }

    private void ClearCanvases()
    {
        _processed?.Clear();
        _raw?.Clear();
        ResetStrokeState();
    }
}
