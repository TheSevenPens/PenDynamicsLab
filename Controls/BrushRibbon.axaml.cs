using Avalonia.Controls;
using PenDynamicsLab.Drawing;

namespace PenDynamicsLab.Controls;

/// <summary>
/// Top-of-stroke-area toolbar: brush size, colour mode, pressure target, draw-at-zero toggle,
/// and Clear.
/// </summary>
/// <remarks>
/// <para>
/// A <b>view over <see cref="BrushSettings"/></b>, not the place brush state lives. It reads the
/// record into its controls and writes edits back out through <see cref="SettingsChanged"/>;
/// drawing code takes the record and never reaches in here for a value.
/// </para>
/// <para>
/// That distinction is what lets a second consumer exist at all — a stamp engine, a headless
/// replay, a second tool — without one of them having to construct a <c>UserControl</c> to find
/// out how wide the brush is.
/// </para>
/// </remarks>
public partial class BrushRibbon : UserControl
{
    private BrushSettings _settings = BrushSettings.Default;

    // Set while pushing the record into the controls, so the change events that causes are not
    // mistaken for user edits and echoed straight back out.
    private bool _suppress;

    /// <summary>
    /// The brush configuration this ribbon is showing. Assigning pushes it into the controls;
    /// reading returns whatever the user has since edited.
    /// </summary>
    public BrushSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            SyncToControls();
        }
    }

    /// <summary>Fires when the user changes any control, carrying the updated record.</summary>
    public event EventHandler<BrushSettings>? SettingsChanged;

    /// <summary>Fires when the user clicks Clear.</summary>
    public event EventHandler? ClearRequested;

    /// <summary>
    /// Whether tapping stamps the alignment test figure instead of drawing.
    /// </summary>
    /// <remarks>
    /// Kept off <see cref="BrushSettings"/> on purpose. It is not a property of the brush - it
    /// changes what a pen-down <i>means</i> - and folding it in would put a diagnostic mode into
    /// the record that presets are saved from and that drawing code reads on every sample.
    /// </remarks>
    public bool TapTestEnabled => TapTestCheck.IsChecked == true;

    /// <summary>Fires when the user toggles tap test, carrying the new state.</summary>
    public event EventHandler<bool>? TapTestChanged;

    /// <summary>Fires when the user toggles recording, carrying the new state.</summary>
    /// <remarks>
    /// Kept off <see cref="BrushSettings"/>: recording is not a property of the brush, and a
    /// preset should not be able to switch it on.
    /// </remarks>
    public event EventHandler<bool>? RecordChanged;

    /// <summary>Show a short line beside the Record box — sample count, or where a file went.</summary>
    public void SetRecordStatus(string text) => RecordStatus.Text = text;

    public BrushRibbon()
    {
        InitializeComponent();

        // The combos hold the enum values themselves, so selection is read by value rather than
        // by position — inserting a mode in the middle cannot silently repoint the dropdown.
        foreach (var m in Enum.GetValues<ColorMode>()) ColorModeCombo.Items.Add(m);
        foreach (var c in Enum.GetValues<PressureControl>()) PressureControlCombo.Items.Add(c);

        SyncToControls();

        BrushSizeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name != "Value") return;
            UpdateBrushSizeLabel();
            Emit(_settings with { Size = BrushSizeSlider.Value });
        };
        ColorModeCombo.SelectionChanged += (_, _) =>
        {
            if (ColorModeCombo.SelectedItem is ColorMode m) Emit(_settings with { ColorMode = m });
        };
        PressureControlCombo.SelectionChanged += (_, _) =>
        {
            if (PressureControlCombo.SelectedItem is PressureControl c) Emit(_settings with { PressureDrives = c });
        };
        DrawZeroPressureCheck.IsCheckedChanged += (_, _) =>
            Emit(_settings with { DrawAtZeroPressure = DrawZeroPressureCheck.IsChecked == true });

        TapTestCheck.IsCheckedChanged += (_, _) =>
            TapTestChanged?.Invoke(this, TapTestEnabled);

        RecordCheck.IsCheckedChanged += (_, _) =>
            RecordChanged?.Invoke(this, RecordCheck.IsChecked == true);

        ClearButton.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Emit(BrushSettings next)
    {
        if (_suppress || next == _settings) return;
        _settings = next;
        SettingsChanged?.Invoke(this, next);
    }

    /// <summary>Pushes the record into the controls without echoing the resulting events back.</summary>
    private void SyncToControls()
    {
        _suppress = true;
        BrushSizeSlider.Value = _settings.Size;
        ColorModeCombo.SelectedItem = _settings.ColorMode;
        PressureControlCombo.SelectedItem = _settings.PressureDrives;
        DrawZeroPressureCheck.IsChecked = _settings.DrawAtZeroPressure;
        _suppress = false;

        UpdateBrushSizeLabel();
    }

    private void UpdateBrushSizeLabel()
        => BrushSizeLabel.Text = $"{(int)BrushSizeSlider.Value} px";
}
