using Avalonia;
using Avalonia.Media;
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
/// are keyed on Light, Dark, and the app's own variants in <see cref="AppThemeVariants"/>. The one thing that does not follow automatically is code
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

    /// <summary>
    /// The accent ramp Fluent derives its own accent-coloured parts from — the selected tab's
    /// indicator, the slider's filled track, focus rings, checked radios and boxes.
    /// </summary>
    /// <remarks>
    /// None of that comes from a <c>Pdl.*</c> brush, so a theme that only re-points the palette
    /// leaves those parts Windows blue — on pink chrome that reads as a half-applied theme.
    ///
    /// Two routes do not work and were tried: overriding these in a theme dictionary loses to the
    /// platform, which writes them straight into <c>Application.Resources</c>; and
    /// <c>FluentTheme.Palettes</c>, the documented hook, throws
    /// "only supports Light and Dark variants" for a custom one. Assigning into
    /// <c>Application.Resources</c> is what is left, and it outranks both.
    /// </remarks>
    private static readonly string[] AccentKeys =
    [
        "SystemAccentColor",
        "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    ];

    /// <summary>Sakura's rose, in the light-to-dark ramp Fluent expects.</summary>
    private static readonly Color[] SakuraAccent =
    [
        Color.Parse("#C1466F"),
        Color.Parse("#CC5C80"), Color.Parse("#D67391"), Color.Parse("#E08BA3"),
        Color.Parse("#A63B5F"), Color.Parse("#8B304F"), Color.Parse("#70253F"),
    ];

    /// <summary>
    /// The platform's own accent, captured before anything overrides it so the blue themes can be
    /// put back exactly. Capturing beats hard-coding a blue: the user's Windows accent is theirs,
    /// and Light and Dark should keep honouring it.
    /// </summary>
    private static Dictionary<string, object?>? _platformAccent;

    private static void ApplyAccent(Application app, AppTheme theme)
    {
        _platformAccent ??= AccentKeys.ToDictionary(
            k => k,
            k => app.Resources.TryGetResource(k, null, out var v) ? v : null);

        bool sakura = theme is AppTheme.Sakura;
        for (int i = 0; i < AccentKeys.Length; i++)
        {
            object? value = sakura ? SakuraAccent[i] : _platformAccent[AccentKeys[i]];
            if (value is null) app.Resources.Remove(AccentKeys[i]);
            else app.Resources[AccentKeys[i]] = value;
        }
    }

    private static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app) return;

        ApplyAccent(app, theme);

        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.Sakura => AppThemeVariants.Sakura,
            _ => ThemeVariant.Default,
        };
    }
}
