using Avalonia.Controls;
using Avalonia.Media;
using PenDynamicsLab.Controls;

namespace PenDynamicsLab.Theming;

/// <summary>
/// Resolves the three status-pill tones from the palette.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SectionCard"/> and <c>MainWindow</c>'s effective-chart pill paint the same three
/// tones, and each used to hold its own copy of the six light hex values. Two copies of one
/// table drift the first time a tone is added or adjusted, and in dark mode both copies were
/// simply wrong — <c>static readonly</c> is the one shape a theme switch cannot reach.
/// </para>
/// <para>
/// The seeds below double as the fallback when a key is missing, the same contract
/// <see cref="ThemeInk"/> uses: a palette gap degrades to the old light appearance rather than
/// to a blank pill.
/// </para>
/// </remarks>
public static class StatusInk
{
    private static readonly IBrush NeutralFillSeed = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly IBrush NeutralInkSeed = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61));
    private static readonly IBrush ActiveFillSeed = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFC));
    private static readonly IBrush ActiveInkSeed = new SolidColorBrush(Color.FromRgb(0x11, 0x5E, 0xA3));
    private static readonly IBrush AdvisoryFillSeed = new SolidColorBrush(Color.FromRgb(0xFF, 0xF9, 0xF0));
    private static readonly IBrush AdvisoryInkSeed = new SolidColorBrush(Color.FromRgb(0x7A, 0x5A, 0x16));

    /// <summary>
    /// The pill fill and text ink for <paramref name="tone"/>, read against
    /// <paramref name="host"/>'s active theme variant.
    /// </summary>
    public static (IBrush Fill, IBrush Ink) Resolve(Control host, StatusTone tone) => tone switch
    {
        StatusTone.Active => (
            ThemeInk.Brush(host, "Pdl.ActiveFill", ActiveFillSeed),
            ThemeInk.Brush(host, "Pdl.ActiveInk", ActiveInkSeed)),

        StatusTone.Advisory => (
            ThemeInk.Brush(host, "Pdl.AdvisoryFill", AdvisoryFillSeed),
            ThemeInk.Brush(host, "Pdl.AdvisoryInk", AdvisoryInkSeed)),

        _ => (
            ThemeInk.Brush(host, "Pdl.NeutralFill", NeutralFillSeed),
            ThemeInk.Brush(host, "Pdl.NeutralInk", NeutralInkSeed)),
    };
}
