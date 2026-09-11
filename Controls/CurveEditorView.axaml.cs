using Avalonia;
using Avalonia.Controls;
using PenDynamicsLab.Curves;

namespace PenDynamicsLab.Controls;

/// <summary>
/// The controls for ONE curve: type, the sliders that type uses, the bezier toolbar, and
/// the reset button. Two instances make curve 1 and curve 2.
/// </summary>
/// <remarks>
/// <para>
/// This exists so a second curve costs one more instance rather than a second copy of ten
/// named controls and their handlers. Everything that was per-curve moved here; what stayed
/// in <c>MainWindow</c> is what is genuinely global — smoothing, processing order, presets.
/// </para>
/// <para>
/// The view never mutates <see cref="Curve"/> from its own <see cref="CurveChanged"/>
/// handler chain. It raises the event and the owner writes back, so there is exactly one
/// place that decides what the current parameters are.
/// </para>
/// </remarks>
public partial class CurveEditorView : UserControl
{
    public static readonly StyledProperty<CurveSettings> CurveProperty =
        AvaloniaProperty.Register<CurveEditorView, CurveSettings>(
            nameof(Curve), defaultValue: CurveSettings.Default);

    public CurveSettings Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    /// <summary>Raised when the user changes something. Not raised by <see cref="SyncFromCurve"/>.</summary>
    public event EventHandler<CurveSettings>? CurveChanged;

    /// <summary>Raised by the bezier toolbar's Add / Remove buttons.</summary>
    public event EventHandler? BezierAddRequested;
    public event EventHandler? BezierRemoveRequested;

    private bool _suppress;

    public CurveEditorView()
    {
        InitializeComponent();

        // Radio groups are matched by name across the whole window, so two instances
        // sharing one name would let curve 2's Cut clear curve 1's. Give each instance
        // its own group.
        string group = "MinApproach_" + GetHashCode().ToString("X");
        MinApproachClampRadio.GroupName = group;
        MinApproachCutRadio.GroupName = group;

        // Hold the enum values themselves rather than their names, so selection is by value
        // and not by position. Inserting a CurveType in the middle would otherwise repoint
        // every dropdown entry after it. (Presets serialize enum names, so saved files were
        // always safe; it was the UI contract that was not.) The combo still shows the name,
        // because that is what ToString gives it.
        foreach (var ct in Enum.GetValues<CurveType>())
            TypeCombo.Items.Add(ct);

        foreach (var preset in BezierPresets.All)
            BezierPresetCombo.Items.Add(preset.Name);

        TypeCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress || TypeCombo.SelectedItem is not CurveType selected) return;
            Emit(Curve with { CurveType = selected });
        };

        BezierPresetCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress || BezierPresetCombo.SelectedIndex < 0) return;
            var preset = BezierPresets.All[BezierPresetCombo.SelectedIndex];
            Emit(Curve with { BezierPoints = preset.Points });

            // Reset the selection so the same preset can be re-applied later.
            _suppress = true;
            BezierPresetCombo.SelectedIndex = -1;
            _suppress = false;
        };

        WireSlider(SoftnessSlider, (c, v) => c with { Softness = v });
        WireSlider(InputMinSlider, (c, v) => c with { InputMinimum = v });
        WireSlider(InputMaxSlider, (c, v) => c with { InputMaximum = v });
        WireSlider(OutputMinSlider, (c, v) => c with { Minimum = v });
        WireSlider(OutputMaxSlider, (c, v) => c with { Maximum = v });
        WireSlider(FlatLevelSlider, (c, v) => c with { FlatLevel = v });

        MinApproachClampRadio.IsCheckedChanged += (_, _) =>
        {
            if (_suppress || MinApproachClampRadio.IsChecked != true) return;
            Emit(Curve with { MinApproach = MinApproach.Clamp });
        };
        MinApproachCutRadio.IsCheckedChanged += (_, _) =>
        {
            if (_suppress || MinApproachCutRadio.IsChecked != true) return;
            Emit(Curve with { MinApproach = MinApproach.Cut });
        };

        ResetButton.Click += (_, _) => Emit(CurveDefaults.ResetCurve(Curve));
        BezierAddButton.Click += (_, _) => BezierAddRequested?.Invoke(this, EventArgs.Empty);
        BezierRemoveButton.Click += (_, _) => BezierRemoveRequested?.Invoke(this, EventArgs.Empty);

        PropertyChanged += (_, e) =>
        {
            if (e.Property == CurveProperty) SyncFromCurve();
        };

        SyncFromCurve();
    }

    private void WireSlider(LabeledSlider slider, Func<CurveSettings, double, CurveSettings> patch)
        => slider.ValueChanged += (_, v) =>
        {
            if (_suppress) return;
            Emit(patch(Curve, v));
        };

    private void Emit(CurveSettings next) => CurveChanged?.Invoke(this, next);

    /// <summary>Pushes <see cref="Curve"/> into the controls without raising events.</summary>
    public void SyncFromCurve()
    {
        var c = Curve;

        _suppress = true;
        TypeCombo.SelectedItem = c.CurveType;
        SoftnessSlider.Value = c.Softness;
        InputMinSlider.Value = c.InputMinimum;
        InputMaxSlider.Value = c.InputMaximum;
        OutputMinSlider.Value = c.Minimum;
        OutputMaxSlider.Value = c.Maximum;
        FlatLevelSlider.Value = c.FlatLevel;
        MinApproachClampRadio.IsChecked = c.MinApproach == MinApproach.Clamp;
        MinApproachCutRadio.IsChecked = c.MinApproach == MinApproach.Cut;
        _suppress = false;

        UpdateVisibility();
    }

    /// <summary>
    /// Per-curve-type control visibility, and the softness range clamp for Sigmoid.
    /// </summary>
    /// <remarks>
    /// Returns the curve possibly adjusted: Sigmoid restricts softness to [0, 0.95]
    /// (steepness is <c>softness * 14</c>, and the top of the range is numerically
    /// unstable), so switching to it from a negative softness has to move the value.
    /// </remarks>
    private void UpdateVisibility()
    {
        var ct = Curve.CurveType;

        bool hasSoftness = ct is CurveType.Basic or CurveType.Extended or CurveType.Sigmoid;
        bool hasRangeControls = CurveMath.UsesRangeControls(ct);
        bool isBezier = ct == CurveType.Bezier;
        bool isFlat = ct == CurveType.Flat;

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

        if (Curve.Softness < SoftnessSlider.Minimum || Curve.Softness > SoftnessSlider.Maximum)
        {
            double clamped = Math.Clamp(Curve.Softness, SoftnessSlider.Minimum, SoftnessSlider.Maximum);
            _suppress = true;
            SoftnessSlider.Value = clamped;
            _suppress = false;
            Emit(Curve with { Softness = clamped });
        }

        SoftnessSlider.IsVisible = hasSoftness;
        InputMinSlider.IsVisible = hasRangeControls;
        InputMaxSlider.IsVisible = hasRangeControls;
        OutputMinSlider.IsVisible = hasRangeControls;
        OutputMaxSlider.IsVisible = hasRangeControls;
        MinApproachPanel.IsVisible = hasRangeControls;
        FlatLevelSlider.IsVisible = isFlat;
        BezierToolbar.IsVisible = isBezier;

        // Reset is type-scoped, so for a type with no settings of its own it has nothing
        // to restore — grey it out rather than leave a button that silently does nothing.
        ResetButton.IsEnabled = CurveDefaults.CurveHasSettings(ct);

        // Range values are driven by dragging the pink/cyan nodes on the chart, so the
        // slider track would be redundant — show only label + value.
        InputMinSlider.ShowSlider = false;
        InputMaxSlider.ShowSlider = false;
        OutputMinSlider.ShowSlider = false;
        OutputMaxSlider.ShowSlider = false;

        if (isBezier)
        {
            int count = CurveMath.NormalizeBezierPoints(Curve.BezierPoints).Length;
            BezierCountLabel.Text = $"{count}/16";
            BezierAddButton.IsEnabled = count < 16;
            BezierRemoveButton.IsEnabled = count > 2;
        }
    }
}
