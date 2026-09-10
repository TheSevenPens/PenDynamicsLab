using Avalonia;
using Avalonia.Styling;
using PenDynamicsLab.Persistence;

namespace PenDynamicsLab.Theming;

/// <summary>
/// Turns the saved <see cref="AppTheme"/> preference into Avalonia's
/// <see cref="ThemeVariant"/>, and persists the user's choice.
/// </summary>
/// <remarks>
/// <para>
/// The mapping is the whole trick: <see cref="ThemeVariant.Default"/> is not a third
/// palette, it means "take the platform's". So <see cref="AppTheme.System"/> maps to it
/// and Avalonia keeps <c>ActualThemeVariant</c> in step with the Windows app-mode setting
/// on its own, including while the app is running. Nothing here has to poll.
/// </para>
/// <para>
/// Everything visual then follows from `Theming/Palette.axaml`, whose theme dictionaries
/// are keyed on Light and Dark. The one thing that does not follow automatically is code
/// that resolved a brush once and cached it — see <c>ThemeBrushes</c>, which the two chart
/// controls use instead.
/// </para>
/// </remarks>
public sealed class ThemeService
{
    private readonly UiSettings _settings;

    public ThemeService(UiSettings settings)
    {
        _settings = settings;
    }

    public AppTheme Current => _settings.Theme;

    /// <summary>Applies the saved preference. Call once at startup.</summary>
    public void ApplySaved() => Apply(_settings.Theme);

    /// <summary>Applies <paramref name="theme"/> immediately and remembers it.</summary>
    /// <remarks>
    /// There is no OK/Cancel on the options dialog, so this is the commit point: the
    /// window repaints and the preference is written in the same call.
    /// </remarks>
    public void Set(AppTheme theme)
    {
        _settings.Theme = theme;
        Apply(theme);
    }

    private static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app) return;

        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
