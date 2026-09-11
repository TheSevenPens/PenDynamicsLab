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
using PenDynamicsLab.Theming;
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
    private static readonly SKColor RedStrokeColor = new(0xC4, 0x1E, 0x3A);

    // Telemetry chrome. Named here so the ribbon's live states use the same tokens as
    // the markup rather than Brushes.Gray / Brushes.LimeGreen.
    // Fallback seeds for the telemetry ribbon's code-set colours. These are read through
    // ThemeInk so they follow the palette; the seed is only reached if a key is missing,
    // and it matches that token's light value so a gap degrades to the old appearance.
    //
    // In-range green was its own one-off #16A34A before this. It now uses Pdl.Processed,
    // which is #14A050 in light — a deliberate, agreed shift, so that the app has one green
    // rather than two nearly-identical ones.
    private static readonly IBrush ProcessedSeed = new SolidColorBrush(Color.FromRgb(0x14, 0xA0, 0x50));
    private static readonly IBrush ProximityIdleSeed = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
    private static readonly IBrush TextSeed = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24));
    private static readonly IBrush MutedSeed = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61));

    /// <summary>
    /// Whether the ribbon is currently showing "In range". Held because the proximity chrome
    /// is painted from code: with the pen idle nothing repaints it, so a theme switch has to
    /// re-apply the current state rather than wait for the next pen point.
    /// </summary>
    private bool _penInRange;

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
    private readonly UiSettings _uiSettings = new();
    private readonly ThemeService _theme;

    // Non-null while the preset name box is open for a rename.
    private string? _renamingPreset;

    public MainWindow()
    {
        // Before InitializeComponent: the palette is resolved as the tree is built, so
        // applying the saved theme first means the window paints correct on the first
        // frame instead of flashing light and then correcting itself.
        _theme = new ThemeService(_uiSettings);
        _theme.ApplySaved();

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

        // The effective-chart pill and the proximity chrome are painted from code, so they
        // cannot follow {DynamicResource}. The cards look after themselves — SectionCard has
        // its own handler — but these two have to be re-applied here, and the proximity state
        // in particular will not repaint on its own while the pen is idle.
        ActualThemeVariantChanged += (_, _) =>
        {
            UpdateCardStatuses();
            ApplyProximity(_penInRange);
        };

        // Export menu on each canvas view.
        StrokeView.SaveRequested += async (_, _) => await SaveSurfaceAsPngAsync(_processed, "stroke.png");
        CompareProcessedView.SaveRequested += async (_, _) => await SaveSurfaceAsPngAsync(_processed, "processed.png");
        CompareRawView.SaveRequested += async (_, _) => await SaveSurfaceAsPngAsync(_raw, "unprocessed.png");
        StrokeView.CopyRequested += async (_, _) => await CopySurfaceAsync(_processed);
        CompareProcessedView.CopyRequested += async (_, _) => await CopySurfaceAsync(_processed);
        CompareRawView.CopyRequested += async (_, _) => await CopySurfaceAsync(_raw);
        // Clear always clears both surfaces: they are two views of one stroke, so wiping
        // only the half you right-clicked would leave the comparison mismatched.
        StrokeView.ClearRequested += (_, _) => ClearCanvases();
        CompareProcessedView.ClearRequested += (_, _) => ClearCanvases();
        CompareRawView.ClearRequested += (_, _) => ClearCanvases();

        PressureChart.BuildExportMenuItems = BuildChartExportMenuItems;
        DriverTipChip.IsVisible = !_uiSettings.DriverTipDismissed;

        BrushRibbon.ClearRequested += (_, _) => ClearCanvases();
        InitializeCurveControls();
        ApplyCurveCount(_uiSettings.UseTwoCurves);
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
        if (BrushRibbon.ColorMode == ColorMode.Red)
        {
            _strokeColor = RedStrokeColor;
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
        foreach (var level in Quantization.Levels)
            QuantizationCombo.Items.Add(Quantization.Format(level));

        foreach (var st in Enum.GetValues<SmoothingType>())
            SmoothingTypeCombo.Items.Add(FormatSmoothingType(st));

        _suppressCurveControlEvents = true;
        SmoothingTypeCombo.SelectedIndex = (int)_curveParams.SmoothingType;
        PressureEmaSlider.Value = _curveParams.EmaSmoothing;
        _suppressCurveControlEvents = false;

        QuantizationCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressCurveControlEvents || QuantizationCombo.SelectedIndex < 0) return;
            UpdateParams(p => p with { QuantizationLevels = Quantization.Levels[QuantizationCombo.SelectedIndex] });
        };
        SmoothingTypeCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressCurveControlEvents || SmoothingTypeCombo.SelectedIndex < 0) return;
            UpdateParams(p => p with { SmoothingType = (SmoothingType)SmoothingTypeCombo.SelectedIndex });
        };

        WireSlider(PressureEmaSlider, v => p => p with { EmaSmoothing = v });

        // The two curve editors and the two editable charts are two views of the same
        // curve each. Both write back here, and this is the only place that decides what
        // the current parameters are.
        Curve1Editor.CurveChanged += (_, c) => UpdateParams(p => p with { Curve1 = c });
        Curve2Editor.CurveChanged += (_, c) => UpdateParams(p => p with { Curve2 = c });
        Curve1Editor.BezierAddRequested += (_, _) => PressureChart.AddBezierPointAtLargestGap();
        Curve1Editor.BezierRemoveRequested += (_, _) => PressureChart.RemoveSelectedBezierPoint();
        Curve2Editor.BezierAddRequested += (_, _) => PressureChart2.AddBezierPointAtLargestGap();
        Curve2Editor.BezierRemoveRequested += (_, _) => PressureChart2.RemoveSelectedBezierPoint();

        WireChart(PressureChart, c => p => p with { Curve1 = c });
        WireChart(PressureChart2, c => p => p with { Curve2 = c });

        SyncCurveControlsFromParams();
    }

    /// <summary>
    /// Mirrors a chart's own edits — dragging nodes, handles, or its context menu — back
    /// into the owning parameters and then into the controls.
    /// </summary>
    private void WireChart(PressureChartControl chart, Func<CurveSettings, Func<PressureCurveParams, PressureCurveParams>> patch)
        => chart.PropertyChanged += (_, e) =>
        {
            if (e.Property != PressureChartControl.CurveProperty) return;
            if (e.NewValue is not CurveSettings edited) return;
            if (_suppressCurveControlEvents) return;

            UpdateParams(patch(edited));
        };

    /// <summary>
    /// Pushes the current parameters into the controls this window owns directly, then
    /// into every view. For a wholesale change — a preset load, the curve count, startup.
    /// </summary>
    private void SyncCurveControlsFromParams()
    {
        _suppressCurveControlEvents = true;
        SmoothingTypeCombo.SelectedIndex = (int)_curveParams.SmoothingType;
        PressureEmaSlider.Value = _curveParams.EmaSmoothing;
        QuantizationCombo.SelectedIndex = Math.Max(0, Array.IndexOf(Quantization.Levels, _curveParams.QuantizationLevels));
        _suppressCurveControlEvents = false;

        PushParamsToViews();
        UpdateDerivedControlState();
        ApplyCardOrder();
        UpdateCardStatuses();
    }

    /// <summary>Hands the current parameters to both editors and all four charts.</summary>
    private void PushParamsToViews()
    {
        // Writing a chart's Curve raises its PropertyChanged, which WireChart reads as a
        // user edit — so the app's own push has to be marked as not one. Without this the
        // push bounces straight back in as an edit.
        bool previous = _suppressCurveControlEvents;
        _suppressCurveControlEvents = true;
        PressureChart.Curve = _curveParams.Curve1;
        PressureChart2.Curve = _curveParams.Curve2;
        EffectiveChart.Params = _curveParams;
        ResponseChart.Params = _curveParams;
        _suppressCurveControlEvents = previous;

        // The editors are pushed OUTSIDE that guard on purpose. Switching to Sigmoid from
        // a negative softness makes the editor clamp the value and emit the correction,
        // and that correction has to reach back here rather than be swallowed.
        Curve1Editor.Curve = _curveParams.Curve1;
        Curve2Editor.Curve = _curveParams.Curve2;
    }

    /// <summary>
    /// Control state that is derived from the parameters rather than set by the user.
    /// </summary>
    /// <remarks>
    /// This has to run on EVERY parameter change, not just a wholesale sync. It used to
    /// live inside <see cref="SyncCurveControlsFromParams"/> alone, so switching smoothing
    /// to EMA left its amount slider hidden until some unrelated edit happened to trigger
    /// a sync.
    /// </remarks>
    private void UpdateDerivedControlState()
    {
        // Passthrough smoothing ignores the amount, so hide it — same convention as the
        // curve cards, where Passthrough hides softness and the range controls.
        bool smoothing = _curveParams.SmoothingType != SmoothingType.Passthrough;
        PressureEmaSlider.IsVisible = smoothing;
        SmoothingResetButton.IsEnabled = smoothing;
    }

    // ── Card headers ────────────────────────────────────────────

    /// <summary>
    /// Refresh the card header pills, so each card's state is readable while collapsed.
    /// Curve and Smoothing carry the three states from <see cref="StageStatus"/> — Off
    /// when bypassed, "On · no effect" when running but configured to change nothing,
    /// On otherwise. Processing names the order and greys it out while the order is moot.
    /// </summary>
    /// <summary>
    /// Paint the proximity dot and label for the current pen state, from the palette.
    /// </summary>
    /// <remarks>
    /// The colour genuinely depends on state, so unlike the static labels in the ribbon this
    /// cannot simply be left to <c>{DynamicResource}</c> in XAML — but it still has to be
    /// read against the active variant rather than baked in at startup.
    /// </remarks>
    private void ApplyProximity(bool inRange)
    {
        _penInRange = inRange;
        ProximityDot.Fill = inRange
            ? ThemeInk.Brush(this, "Pdl.Processed", ProcessedSeed)
            : ThemeInk.Brush(this, "Pdl.ProximityIdle", ProximityIdleSeed);
        ProximityLabel.Text = inRange ? "In range" : "Out of range";
        ProximityLabel.Foreground = inRange
            ? ThemeInk.Brush(this, "Pdl.Text", TextSeed)
            : ThemeInk.Brush(this, "Pdl.Muted", MutedSeed);
    }

    private void UpdateCardStatuses()
    {
        ApplyStageStatus(QuantizationCard,
            Quantization.IsActive(_curveParams.QuantizationLevels) ? StageState.On : StageState.Off);
        ApplyStageStatus(Curve1Card, StageStatus.Curve(_curveParams.Curve1));
        ApplyStageStatus(Curve2Card, StageStatus.Curve(_curveParams.Curve2));
        ApplyStageStatus(SmoothingCard, StageStatus.Smoothing(_curveParams));

        // The effective chart carries a pill too, because "both curves are on" and "the
        // pair does something" are not the same claim. See StageStatus.Effective.
        var (effText, effTone) = StatusText(StageStatus.Effective(_curveParams));
        EffectivePillText.Text = effText;
        var (effFill, effInk) = StatusInk.Resolve(this, effTone);
        EffectivePillText.Foreground = effInk;
        EffectivePill.Background = effFill;
    }

    private static (string, StatusTone) StatusText(StageState state) => state switch
    {
        StageState.Off => ("Off", StatusTone.Neutral),
        StageState.NoEffect => ("On · no effect", StatusTone.Advisory),
        _ => ("On", StatusTone.Active),
    };

    private static void ApplyStageStatus(SectionCard card, StageState state)
        => (card.Status, card.StatusKind) = StatusText(state);

    // ── Section resets ──────────────────────────────────────────

    // Type-scoped: these put the settings of the currently selected type back to their
    // defaults and leave the type alone. Switching to Passthrough is the dropdown's job.
    // See CurveDefaults for why reset does not touch the fields other types own.

    private void SmoothingReset_Click(object? sender, RoutedEventArgs e)
    {
        UpdateParams(CurveDefaults.ResetSmoothing);
        SyncCurveControlsFromParams();
    }

    // ── User presets ────────────────────────────────────────────

    // "Save settings" reveals an inline name box rather than opening a dialog, keeping
    // the card compact when it isn't being used.
    /// <summary>
    /// Saving takes a generated name so it stays one click. Naming is a separate,
    /// deliberate act via the row's Rename, which is where a name is actually worth
    /// thinking about — by then you know what the preset turned out to be.
    /// </summary>
    private void PresetSave_Click(object? sender, RoutedEventArgs e)
    {
        _presetStore.Save(_presetStore.NextAvailableName(), _curveParams);
        RebuildUserPresetList();
    }

    private void BeginRenamePreset(string name)
    {
        _renamingPreset = name;
        PresetNameRow.IsVisible = true;
        PresetSaveButton.IsVisible = false;
        PresetNameInput.Text = name;
        PresetNameInput.SelectAll();
        PresetNameInput.Focus();
    }

    private void PresetCancel_Click(object? sender, RoutedEventArgs e) => HidePresetNameRow();

    private void PresetConfirm_Click(object? sender, RoutedEventArgs e)
    {
        var name = PresetNameInput.Text?.Trim() ?? "";
        if (name.Length > 0 && _renamingPreset is { } original)
            _presetStore.Rename(original, name);

        HidePresetNameRow();
        RebuildUserPresetList();
    }

    private void HidePresetNameRow()
    {
        _renamingPreset = null;
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

    /// <summary>
    /// Build the chart's export entries. Called fresh on every right-click, because a menu
    /// item can belong to only one parent and the chart rebuilds its ContextMenu each time.
    /// </summary>
    private IEnumerable<Control> BuildChartExportMenuItems()
    {
        var copyFull = new MenuItem { Header = "Copy full chart" };
        copyFull.Click += async (_, _) => await CopyChartPngAsync(cropToPlot: false);

        var copyPlot = new MenuItem { Header = "Copy plot area only" };
        copyPlot.Click += async (_, _) => await CopyChartPngAsync(cropToPlot: true);

        var saveFull = new MenuItem { Header = "Save full chart as PNG..." };
        saveFull.Click += async (_, _) => await SaveChartPngAsync(cropToPlot: false, "pressure-curve.png");

        var savePlot = new MenuItem { Header = "Save plot area as PNG..." };
        savePlot.Click += async (_, _) => await SaveChartPngAsync(cropToPlot: true, "pressure-curve-plot.png");

        return [copyFull, copyPlot, new Separator(), saveFull, savePlot];
    }

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
        => await CopyPngToClipboardAsync(RenderChartPng(cropToPlot));

    /// <summary>Copy a rendered PNG to the clipboard, shared by the chart and the canvases.</summary>
    private async Task CopyPngToClipboardAsync(byte[]? png)
    {
        if (png is null || png.Length == 0) return;

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

    /// <summary>Copy a stroke canvas to the clipboard at its full physical resolution.</summary>
    private async Task CopySurfaceAsync(DrawSurface? surface)
    {
        if (surface is null || surface.Width <= 0 || surface.Height <= 0) return;
        using var ms = new MemoryStream();
        surface.SavePng(ms);
        await CopyPngToClipboardAsync(ms.ToArray());
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

    // ── Driver tip ──────────────────────────────────────────────
    //
    // The advice matters once per tablet, so it lives as a chip in the ribbon's spare
    // right-hand space rather than a band across the window. Two levels of dismissal: the
    // × hides it for this session, "Don't show again" remembers the choice.

    // ── Ribbon ──────────────────────────────────────────────────

    private bool _ribbonCollapsed;

    /// <summary>
    /// Folds the telemetry ribbon down to a slim strip, and back.
    /// </summary>
    /// <remarks>
    /// For demonstrating the curve pipeline the telemetry is the least interesting band on
    /// screen and the tallest thing between the audience and the charts, so collapsing it
    /// hands 56px straight to the chart column.
    ///
    /// The gear stays reachable while collapsed — it is the way back to anything else —
    /// and the driver tip goes with the telemetry, since a hint about calibration is not
    /// what a slim strip is for. This is session state on purpose: it is a presenting
    /// mode, not a preference.
    /// </remarks>
    private void RibbonToggle_Click(object? sender, RoutedEventArgs e)
    {
        _ribbonCollapsed = !_ribbonCollapsed;

        RibbonBody.IsVisible = !_ribbonCollapsed;
        DriverTipChip.IsVisible = !_ribbonCollapsed && !_uiSettings.DriverTipDismissed;
        RibbonPanel.Height = _ribbonCollapsed ? 36 : 92;

        // Segoe Fluent Icons: ChevronUp / ChevronDown, the same pair the cards use.
        RibbonToggle.Content = _ribbonCollapsed ? "" : "";
        ToolTip.SetTip(RibbonToggle,
            _ribbonCollapsed ? "Show the telemetry ribbon" : "Collapse the telemetry ribbon");
    }

    /// <summary>
    /// Opens the global options. Modal to the main window so the theme change repaints
    /// visibly behind it, which is the whole preview.
    /// </summary>
    private async void Options_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OptionsWindow(_theme, _uiSettings, FormatSmoothingOrder);
        dialog.UseTwoCurvesChanged += (_, useTwo) =>
        {
            ApplyCurveCount(useTwo);
            dialog.RefreshOrderLabels(FormatSmoothingOrder);
        };
        dialog.SmoothingOrderChanged += (_, _) => ApplyCardOrder();
        await dialog.ShowDialog(this);
    }

    // ── Curve count ─────────────────────────────────────────────

    /// <summary>Curve 2's settings while it is switched off, so switching back restores them.</summary>
    private CurveSettings _parkedCurve2 = CurveSettings.Default;

    /// <summary>
    /// Shows or hides the second curve — its card, its chart, and the effective chart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Switching to one curve sets <c>Curve2</c> to Passthrough rather than merely hiding
    /// it. A setting that is not on screen but still shapes the output is exactly the bug
    /// removed from Basic, and the rule the whole pipeline is held to: what you cannot see
    /// is not applied. Its settings are parked so the way back is lossless.
    /// </para>
    /// <para>
    /// The effective chart goes with it. With one curve the effective curve <em>is</em>
    /// curve 1 — the same line drawn twice — and the numbering comes off the remaining card
    /// and chart, because a number with nothing to distinguish it from is noise.
    /// </para>
    /// </remarks>
    private void ApplyCurveCount(bool useTwo)
    {
        if (useTwo)
        {
            UpdateParams(p => p with { Curve2 = _parkedCurve2 });
        }
        else
        {
            _parkedCurve2 = _curveParams.Curve2;
            UpdateParams(p => p with { Curve2 = CurveSettings.Default });
        }

        Curve2Card.IsVisible = useTwo;
        Chart2Card.IsVisible = useTwo;
        EffectiveCard.IsVisible = useTwo;

        Curve1Card.Title = useTwo ? "Curve 1" : "Curve";
        Chart1Title.Text = useTwo ? "Pressure curve 1" : "Pressure curve";

        ApplyCardOrder();
        SyncCurveControlsFromParams();
    }

    // ── Card order ──────────────────────────────────────────────

    /// <summary>
    /// Lays the settings cards out in pipeline order, so the column reads top to bottom in
    /// the order the stages actually run.
    /// </summary>
    /// <remarks>
    /// This is what replaced the Processing card's <c>S → C1 → C2</c> pill: rather than a
    /// card describing the order in shorthand, the cards themselves are in it. Quantization
    /// is always first because it always runs first; smoothing and the curves swap.
    ///
    /// Presets carry <c>SmoothingOrder</c>, so loading one can reorder the column — which
    /// is why this is called from the sync path and not only from the options dialog.
    /// </remarks>
    private void ApplyCardOrder()
    {
        Control[] order = _uiSettings.SmoothingOrder == SmoothingOrder.SmoothThenCurve
            ? [QuantizationCard, SmoothingCard, Curve1Card, Curve2Card]
            : [QuantizationCard, Curve1Card, Curve2Card, SmoothingCard];

        // Already in this order? Leave the tree alone rather than detaching and
        // reattaching live controls for nothing.
        if (SettingsStack.Children.Count == order.Length
            && SettingsStack.Children.Zip(order).All(pair => ReferenceEquals(pair.First, pair.Second)))
            return;

        SettingsStack.Children.Clear();
        foreach (var card in order) SettingsStack.Children.Add(card);
    }

    /// <summary>Hide for this session only.</summary>
    private void DriverTipDismiss_Click(object? sender, RoutedEventArgs e)
        => DriverTipChip.IsVisible = false;

    private void DriverTipGotIt_Click(object? sender, RoutedEventArgs e)
        => DriverTipButton.Flyout?.Hide();

    private void DriverTipNeverShow_Click(object? sender, RoutedEventArgs e)
    {
        _uiSettings.DriverTipDismissed = true;
        DriverTipButton.Flyout?.Hide();
        DriverTipChip.IsVisible = false;
    }

    private void RebuildUserPresetList()
    {
        PresetList.Children.Clear();
        PresetEmptyLabel.IsVisible = _presetStore.All.Count == 0;
        foreach (var preset in _presetStore.All.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var name = preset.Name;
            var row = new DockPanel { LastChildFill = true, Height = 32 };

            // One overflow menu rather than a row of differently-sized buttons: a Load
            // button beside a tiny glyph button never lines up, and rename needs a home.
            var loadItem = new MenuItem { Header = "Load" };
            loadItem.Click += (_, _) =>
            {
                if (_presetStore.Get(name) is { } p)
                {
                    _curveParams = p.Params;

                    // A preset holding a real curve 2 has to switch the second curve on,
                    // or half of it would apply with nothing on screen to show it.
                    if (p.Params.Curve2.CurveType != CurveType.Passthrough && !_uiSettings.UseTwoCurves)
                    {
                        _uiSettings.UseTwoCurves = true;
                        _parkedCurve2 = p.Params.Curve2;
                        ApplyCurveCount(true);
                    }
                    else
                    {
                        SyncCurveControlsFromParams();
                    }
                }
            };
            var renameItem = new MenuItem { Header = "Rename..." };
            renameItem.Click += (_, _) => BeginRenamePreset(name);
            var deleteItem = new MenuItem { Header = "Delete" };
            deleteItem.Click += (_, _) =>
            {
                _presetStore.Delete(name);
                RebuildUserPresetList();
            };

            var menuBtn = new Button
            {
                Content = "···",
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                Flyout = new MenuFlyout { ItemsSource = new[] { loadItem, renameItem, deleteItem } },
            };
            ToolTip.SetTip(menuBtn, $"Options for \"{name}\"");
            DockPanel.SetDock(menuBtn, Dock.Right);

            var label = new TextBlock
            {
                Text = name,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            row.Children.Add(menuBtn);
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

    // Plural only when there are two: smoothing runs before or after the PAIR, never
    // between them. Smoothing between curve 1 and curve 2 would be a third order and is
    // not offered.
    private string FormatSmoothingOrder(SmoothingOrder so) => so switch
    {
        SmoothingOrder.SmoothThenCurve => _uiSettings.UseTwoCurves ? "Smooth then curves" : "Smooth then curve",
        SmoothingOrder.CurveThenSmooth => _uiSettings.UseTwoCurves ? "Curves then smooth" : "Curve then smooth",
        _ => so.ToString(),
    };

    private static string FormatSmoothingType(SmoothingType st) => st switch
    {
        SmoothingType.Passthrough => "Passthrough",
        SmoothingType.Ema => "EMA",
        _ => st.ToString(),
    };

    /// <summary>
    /// The single place the current parameters change. Every editor, chart and preset load
    /// funnels through here, which is what keeps two curve editors and three charts from
    /// disagreeing about what the pipeline currently is.
    /// </summary>
    private void UpdateParams(Func<PressureCurveParams, PressureCurveParams> patch)
    {
        _curveParams = patch(_curveParams);

        PushParamsToViews();
        UpdateDerivedControlState();
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
        PressureChart2.LiveRawPressure = null;
        PressureChart2.LivePressure = null;
        EffectiveChart.LiveRawPressure = null;
        EffectiveChart.LivePressure = null;
        ResponseChart.LiveRawPressure = null;
        ResponseChart.LivePressure = null;
    }

    // ── Pressure pipeline ───────────────────────────────────────

    private readonly record struct PressurePipelineResult(
        double Raw, double PreCurve, double Output);

    private PressurePipelineResult ProcessPressure(double rawInput)
    {
        // Quantization is first and unconditional: it models the resolution the pressure
        // arrived at, and no later stage can restore detail it has discarded. Everything
        // downstream — including the value the charts show as "raw" — sees the coarsened
        // signal, because that is what the pen effectively gave us.
        double raw = Quantization.Apply(rawInput, _curveParams.QuantizationLevels);

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

        if (_uiSettings.SmoothingOrder == SmoothingOrder.CurveThenSmooth)
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
                ApplyProximity(inRange: false);
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

            UpdateTelemetry(pt, clientPt, over == ActiveCanvas.None ? null : (Point?)localPt, maxP, pipeline.Output);
            // Each chart's x axis is a different quantity, so the indicators cannot all
            // carry the same number. Curve 1 and the effective chart are both read against
            // pen pressure; curve 2's axis is curve 1's OUTPUT, so it gets that instead —
            // and no raw dot at all, because raw pen pressure does not live on that axis.
            PressureChart.LiveRawPressure = pipeline.Raw;
            PressureChart.LivePressure = pipeline.PreCurve;

            PressureChart2.LiveRawPressure = null;
            PressureChart2.LivePressure = CurveMath.ApplyCurve(pipeline.PreCurve, _curveParams.Curve1);

            EffectiveChart.LiveRawPressure = pipeline.Raw;
            EffectiveChart.LivePressure = pipeline.PreCurve;

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

    /// <param name="processed">
    /// The pipeline's final output — raw pressure after smoothing and the curve. With both
    /// stages on Passthrough this equals the normalized value, which is the point: the row
    /// always shows something, and a difference means a stage is actually doing work.
    /// </param>
    private void UpdateTelemetry(PenPoint pt, Point clientPt, Point? canvasLocal, int maxP, double processed)
    {
        // The captions are static markup now, so these carry the value alone.
        ApplyProximity(inRange: true);
        CursorLabel.Text = pt.Cursor.ToString();

        RawPosLabel.Text = $"{pt.RawX}, {pt.RawY}";
        ScreenPosLabel.Text = $"{pt.DesktopX:F0}, {pt.DesktopY:F0}";
        AppPosLabel.Text = $"{clientPt.X:F0}, {clientPt.Y:F0}";
        CanvasPosLabel.Text = canvasLocal is { } cl ? $"{cl.X:F1}, {cl.Y:F1}" : "--";

        float pct = maxP > 0 ? (float)pt.Pressure / maxP * 100f : 0f;
        RawPressureLabel.Text = pt.Pressure.ToString();
        NormPressureLabel.Text = $"{pct:F1}%";
        ProcessedPressureLabel.Text = $"{processed * 100:F1}%";

        AzimuthLabel.Text = $"{pt.Azimuth:F1}°";
        AltitudeLabel.Text = $"{pt.Altitude:F1}°";
        TwistLabel.Text = $"{pt.Twist:F1}°";
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
