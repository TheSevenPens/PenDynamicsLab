using Avalonia.Controls;
using PenDynamicsLab.Curves;

namespace PenDynamicsLab.Controls;

/// <summary>
/// Top-of-stroke-area toolbar: brush size, color mode, pressure target,
/// draw-at-zero toggle, and Clear. Surfaces the current values via plain
/// properties (read on demand by the drawing pipeline) and a Clear event.
/// </summary>
public partial class BrushRibbon : UserControl
{
    public double BrushSize => BrushSizeSlider.Value;

    // The combos hold the enum values themselves, so selection is read by value rather than
    // by position — inserting a mode in the middle cannot silently repoint the dropdown.
    // The fallbacks cover the brief window before the initial selection is applied.
    public ColorMode ColorMode =>
        ColorModeCombo.SelectedItem is ColorMode m ? m : ColorMode.Black;

    public PressureControl PressureControl =>
        PressureControlCombo.SelectedItem is PressureControl c ? c : PressureControl.Size;

    public bool DrawZeroPressure => DrawZeroPressureCheck.IsChecked == true;

    /// <summary>Fires when the user clicks Clear.</summary>
    public event EventHandler? ClearRequested;

    public BrushRibbon()
    {
        InitializeComponent();

        foreach (var m in Enum.GetValues<ColorMode>()) ColorModeCombo.Items.Add(m);
        ColorModeCombo.SelectedItem = ColorMode.Black;

        foreach (var c in Enum.GetValues<PressureControl>()) PressureControlCombo.Items.Add(c);
        PressureControlCombo.SelectedItem = PressureControl.Size;
        BrushSizeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != "Value") return;
            UpdateBrushSizeLabel();
        };
        UpdateBrushSizeLabel();
        ClearButton.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateBrushSizeLabel()
        => BrushSizeLabel.Text = $"{(int)BrushSizeSlider.Value} px";
}
