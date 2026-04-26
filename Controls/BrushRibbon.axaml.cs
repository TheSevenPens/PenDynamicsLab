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

    public ColorMode ColorMode =>
        ColorRandomRadio.IsChecked == true ? ColorMode.Random : ColorMode.Black;

    public PressureControl PressureControl =>
        PressureOpacityRadio.IsChecked == true ? PressureControl.Opacity : PressureControl.Size;

    public bool DrawZeroPressure => DrawZeroPressureCheck.IsChecked == true;

    /// <summary>Fires when the user clicks Clear.</summary>
    public event EventHandler? ClearRequested;

    public BrushRibbon()
    {
        InitializeComponent();
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
