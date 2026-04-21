using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Globalization;

namespace PenDynamicsLab.Controls;

/// <summary>
/// Composite slider with a clickable value label (click to type a precise value) and a
/// right-click context menu offering Min / Max / Reset. Mirrors WebPressureExplorer's
/// NamedSlider.svelte UX so users can fine-tune parameters that are awkward to drag exactly.
/// </summary>
public partial class LabeledSlider : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<LabeledSlider, string>(nameof(Label), defaultValue: "");

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(Minimum), defaultValue: 0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(Maximum), defaultValue: 1);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(Value), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(DefaultValue), defaultValue: 0);

    public static readonly StyledProperty<double> SmallChangeProperty =
        AvaloniaProperty.Register<LabeledSlider, double>(nameof(SmallChange), defaultValue: 0.01);

    public static readonly StyledProperty<int> DecimalsProperty =
        AvaloniaProperty.Register<LabeledSlider, int>(nameof(Decimals), defaultValue: 2);

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double DefaultValue { get => GetValue(DefaultValueProperty); set => SetValue(DefaultValueProperty, value); }
    public double SmallChange { get => GetValue(SmallChangeProperty); set => SetValue(SmallChangeProperty, value); }
    public int Decimals { get => GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }

    /// <summary>Fires when the value changes from any source (slider drag, edit, context menu).</summary>
    public event EventHandler<double>? ValueChanged;

    private bool _suppressSliderEvent;

    public LabeledSlider()
    {
        InitializeComponent();
        LabelText.Text = Label;
        UpdateValueText();
        SyncSliderRange();
        SyncSliderValue();

        // React to externally-set property changes.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == LabelProperty) LabelText.Text = (string?)e.NewValue ?? "";
            else if (e.Property == MinimumProperty || e.Property == MaximumProperty) SyncSliderRange();
            else if (e.Property == SmallChangeProperty) MainSlider.SmallChange = (double)e.NewValue!;
            else if (e.Property == ValueProperty) { SyncSliderValue(); UpdateValueText(); }
        };

        MainSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty || _suppressSliderEvent) return;
            ApplyValue(MainSlider.Value);
        };

        // Click-to-edit on the value label.
        ValueText.PointerPressed += (_, _) => BeginEdit();
        ValueEdit.LostFocus += (_, _) => CommitEdit();
        ValueEdit.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitEdit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelEdit(); e.Handled = true; }
        };

        // Right-click context menu on the slider.
        MainSlider.ContextMenu = new ContextMenu();
        MainSlider.ContextMenu.Opening += (_, _) => RebuildContextMenu();
    }


    // ── Value & UI sync ─────────────────────────────────────────

    private void SyncSliderRange()
    {
        _suppressSliderEvent = true;
        MainSlider.Minimum = Minimum;
        MainSlider.Maximum = Maximum;
        _suppressSliderEvent = false;
    }

    private void SyncSliderValue()
    {
        _suppressSliderEvent = true;
        MainSlider.Value = Math.Clamp(Value, Minimum, Maximum);
        _suppressSliderEvent = false;
    }

    private void UpdateValueText()
    {
        ValueText.Text = Value.ToString($"F{Decimals}", CultureInfo.InvariantCulture);
    }

    private void ApplyValue(double v)
    {
        v = Math.Clamp(v, Minimum, Maximum);
        if (Value == v)
        {
            UpdateValueText();
            return;
        }
        Value = v;
        UpdateValueText();
        ValueChanged?.Invoke(this, v);
    }

    // ── Click-to-edit ───────────────────────────────────────────

    private void BeginEdit()
    {
        ValueEdit.Text = Value.ToString($"F{Decimals}", CultureInfo.InvariantCulture);
        ValueText.IsVisible = false;
        ValueEdit.IsVisible = true;
        ValueEdit.Focus();
        ValueEdit.SelectAll();
    }

    private void CommitEdit()
    {
        if (!ValueEdit.IsVisible) return;
        ValueEdit.IsVisible = false;
        ValueText.IsVisible = true;
        if (double.TryParse(ValueEdit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            ApplyValue(parsed);
        else
            UpdateValueText();
    }

    private void CancelEdit()
    {
        ValueEdit.IsVisible = false;
        ValueText.IsVisible = true;
        UpdateValueText();
    }

    // ── Context menu ────────────────────────────────────────────

    private void RebuildContextMenu()
    {
        var menu = MainSlider.ContextMenu!;
        menu.Items.Clear();

        var minItem = new MenuItem { Header = $"Min ({Minimum.ToString($"F{Decimals}", CultureInfo.InvariantCulture)})" };
        minItem.Click += (_, _) => ApplyValue(Minimum);
        menu.Items.Add(minItem);

        var maxItem = new MenuItem { Header = $"Max ({Maximum.ToString($"F{Decimals}", CultureInfo.InvariantCulture)})" };
        maxItem.Click += (_, _) => ApplyValue(Maximum);
        menu.Items.Add(maxItem);

        var resetItem = new MenuItem { Header = $"Reset ({DefaultValue.ToString($"F{Decimals}", CultureInfo.InvariantCulture)})" };
        resetItem.Click += (_, _) => ApplyValue(DefaultValue);
        menu.Items.Add(resetItem);
    }
}
