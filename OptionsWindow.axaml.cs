using Avalonia.Controls;
using Avalonia.Interactivity;
using PenDynamicsLab.Persistence;
using PenDynamicsLab.Theming;

namespace PenDynamicsLab;

/// <summary>
/// The app's global options, reached from the gear at the right end of the telemetry
/// ribbon.
/// </summary>
/// <remarks>
/// Settings commit as they are made rather than on close, so this window owns no draft
/// state and has nothing to roll back — <see cref="ThemeService.Set"/> both applies and
/// persists, and the curve count writes straight to <see cref="UiSettings"/> and raises
/// <see cref="UseTwoCurvesChanged"/>. That is why the footer carries Close and not
/// OK / Cancel: with the change already live behind the dialog, an OK button would imply
/// a commit that already happened and a Cancel button would promise an undo that does not
/// exist.
/// </remarks>
public partial class OptionsWindow : Window
{
    private readonly ThemeService _theme;
    private readonly UiSettings _settings;
    private bool _suppress;

    /// <summary>Raised when the user changes the curve count. The owner applies the layout.</summary>
    public event EventHandler<bool>? UseTwoCurvesChanged;

    // Parameterless ctor for the XAML previewer only; the app always passes its own.
    public OptionsWindow() : this(new ThemeService(new UiSettings()), new UiSettings()) { }

    public OptionsWindow(ThemeService theme, UiSettings settings)
    {
        _theme = theme;
        _settings = settings;

        // Do NOT define InitializeComponent() here. The Avalonia source generator emits
        // one into the other half of this partial class; a hand-written copy shadows it,
        // the XAML still loads, and every x:Name field stays null — so the first line
        // that touches LightRadio throws. LabeledSlider found this the hard way once
        // already.
        InitializeComponent();

        _suppress = true;
        (_theme.Current switch
        {
            AppTheme.Light => LightRadio,
            AppTheme.Dark => DarkRadio,
            _ => SystemRadio,
        }).IsChecked = true;
        (_settings.UseTwoCurves ? TwoCurveRadio : OneCurveRadio).IsChecked = true;
        _suppress = false;

        LightRadio.IsCheckedChanged += (_, _) => ChooseTheme(LightRadio, AppTheme.Light);
        DarkRadio.IsCheckedChanged += (_, _) => ChooseTheme(DarkRadio, AppTheme.Dark);
        SystemRadio.IsCheckedChanged += (_, _) => ChooseTheme(SystemRadio, AppTheme.System);

        OneCurveRadio.IsCheckedChanged += (_, _) => ChooseCurveCount(OneCurveRadio, false);
        TwoCurveRadio.IsCheckedChanged += (_, _) => ChooseCurveCount(TwoCurveRadio, true);

        AppearanceRail.Click += (_, _) => Select(appearance: true);
        CurvesRail.Click += (_, _) => Select(appearance: false);

        Select(appearance: true);
    }

    private void Select(bool appearance)
    {
        AppearancePane.IsVisible = appearance;
        CurvesPane.IsVisible = !appearance;

        // The tone itself lives in the window's styles, keyed off this class — see the
        // note there for why it cannot be set from here.
        AppearanceRail.Classes.Set("selected", appearance);
        CurvesRail.Classes.Set("selected", !appearance);
    }

    private void ChooseTheme(RadioButton source, AppTheme theme)
    {
        // A group of radios raises the event twice per change — once for the one being
        // cleared, once for the one being set. Only the latter is a choice.
        if (_suppress || source.IsChecked != true) return;
        _theme.Set(theme);
    }

    private void ChooseCurveCount(RadioButton source, bool useTwo)
    {
        if (_suppress || source.IsChecked != true) return;
        if (_settings.UseTwoCurves == useTwo) return;

        _settings.UseTwoCurves = useTwo;
        UseTwoCurvesChanged?.Invoke(this, useTwo);
    }

    /// <summary>Reflects a curve count the owner changed on its own — a preset load, say.</summary>
    public void SyncCurveCount()
    {
        _suppress = true;
        (_settings.UseTwoCurves ? TwoCurveRadio : OneCurveRadio).IsChecked = true;
        _suppress = false;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
