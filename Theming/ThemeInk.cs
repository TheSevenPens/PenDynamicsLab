using Avalonia.Controls;
using Avalonia.Media;

namespace PenDynamicsLab.Theming;

/// <summary>
/// Resolves palette brushes for controls that paint with <c>DrawingContext</c> rather than
/// XAML, where <c>{DynamicResource}</c> is not available.
/// </summary>
/// <remarks>
/// <para>
/// The two chart controls held their colours in <c>static readonly</c> fields, which is
/// exactly the shape a theme switch cannot reach. They now hold instance fields seeded
/// with the light values and refreshed from the palette whenever the theme changes; the
/// seed doubles as the fallback here, so a missing key degrades to the old appearance
/// rather than to a blank chart.
/// </para>
/// <para>
/// Only chart <em>chrome</em> goes through this. The four data colours — raw purple,
/// effective green, min pink, max cyan — are deliberately outside the palette: they carry
/// meaning, they match WebPressureExplorer, and they are louder than the chrome on
/// purpose. Chrome recedes in either theme; data should not.
/// </para>
/// </remarks>
public static class ThemeInk
{
    public static IBrush Brush(Control host, string key, IBrush fallback)
        => host.TryFindResource(key, host.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : fallback;

    public static IPen Pen(Control host, string key, IPen fallback, double thickness, DashStyle? dash = null)
    {
        if (!host.TryFindResource(key, host.ActualThemeVariant, out var value) || value is not IBrush brush)
            return fallback;

        return new Pen(brush, thickness) { DashStyle = dash };
    }
}
