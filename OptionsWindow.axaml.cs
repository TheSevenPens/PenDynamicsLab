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
/// persists. That is why the footer carries Close and not OK / Cancel: with the change
/// already live behind the dialog, an OK button would imply a commit that already
/// happened and a Cancel button would promise an undo that does not exist.
/// </remarks>
public partial class OptionsWindow : Window
{
    private readonly ThemeService _theme;
    private bool _suppressThemeEvents;

    // Parameterless ctor for the XAML previewer only; the app always passes a service.
    public OptionsWindow() : this(new ThemeService(new UiSettings())) { }

    public OptionsWindow(ThemeService theme)
    {
        _theme = theme;

        // Do NOT define InitializeComponent() here. The Avalonia source generator emits
        // one into the other half of this partial class; a hand-written copy shadows it,
        // the XAML still loads, and every x:Name field stays null — so the first line
        // that touches LightRadio throws. LabeledSlider found this the hard way once
        // already.
        InitializeComponent();

        _suppressThemeEvents = true;
        (_theme.Current switch
        {
            AppTheme.Light => LightRadio,
            AppTheme.Dark => DarkRadio,
            _ => SystemRadio,
        }).IsChecked = true;
        _suppressThemeEvents = false;

        LightRadio.IsCheckedChanged += (_, _) => Choose(LightRadio, AppTheme.Light);
        DarkRadio.IsCheckedChanged += (_, _) => Choose(DarkRadio, AppTheme.Dark);
        SystemRadio.IsCheckedChanged += (_, _) => Choose(SystemRadio, AppTheme.System);
    }

    private void Choose(RadioButton source, AppTheme theme)
    {
        // A group of radios raises the event twice per change — once for the one being
        // cleared, once for the one being set. Only the latter is a choice.
        if (_suppressThemeEvents || source.IsChecked != true) return;
        _theme.Set(theme);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
