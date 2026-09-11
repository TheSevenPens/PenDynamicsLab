using Avalonia;
using Avalonia.Controls;

namespace PenDynamicsLab.Controls;

/// <summary>
/// A chart that can be exported to PNG from its own context menu.
/// </summary>
/// <remarks>
/// <para>
/// The export helpers used to name <c>PressureChart</c> directly, so only curve 1 could be
/// copied or saved. Parameterising them is most of the fix, but the charts are not one type:
/// curve 1 and curve 2 are <see cref="PressureChartControl"/> while the effective chart is
/// <see cref="EffectiveCurveChartControl"/>, and they share no base beyond <c>Control</c>.
/// </para>
/// <para>
/// This is the small shared surface the helpers need, so they can render any chart without
/// switching on concrete types — and so a chart added later only has to satisfy the
/// interface to become exportable.
/// </para>
/// </remarks>
public interface IExportableChart
{
    /// <summary>
    /// The plot area in control-local DIP coordinates — the gridded square only, excluding
    /// axis titles. This is what "Plot area only" crops to.
    /// </summary>
    Rect PlotRect { get; }

    /// <summary>
    /// Supplies the export entries for this chart's context menu. The window sets it; the
    /// chart calls it when the menu opens, so the items are built against current state.
    /// </summary>
    Func<IEnumerable<Control>>? BuildExportMenuItems { get; set; }
}
