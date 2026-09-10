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

    // Combo item order mirrors the enum, so the selected index is the enum value.
    public ColorMode ColorMode => (ColorMode)Math.Max(0, ColorModeCombo.SelectedIndex);

    public PressureControl PressureControl =>
        (PressureControl)Math.Max(0, PressureControlCombo.SelectedIndex);

    public bool DrawZeroPressure => DrawZeroPressureCheck.IsChecked == true;

    /// <summary>Fires when the user clicks Clear.</summary>
    public event EventHandler? ClearRequested;

    public BrushRibbon()
    {
        InitializeComponent();

        foreach (var m in Enum.GetValues<ColorMode>()) ColorModeCombo.Items.Add(m.ToString());
        ColorModeCombo.SelectedIndex = 0;

        foreach (var c in Enum.GetValues<PressureControl>()) PressureControlCombo.Items.Add(c.ToString());
        PressureControlCombo.SelectedIndex = 0;
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
