using WinPenKit;

namespace PenDynamicsLab.Diagnostics;

/// <summary>
/// What the pen's barrel switches are doing, as the text the ribbon shows. Pure: no Avalonia, so it is tested directly.
/// </summary>
/// <remarks>
/// <para>
/// The state itself comes from <see cref="PenButtonTracker"/>, which is the supported way to read buttons because the backends encode them differently: Wintab reports
/// one press or release event per packet (<c>(action &lt;&lt; 16) | button</c>) and the state has to be carried between packets, while the pointer backends report the
/// whole state as a bitmask on every point. Feed this every point, in arrival order, including points that are not drawn: a release that is skipped here stays
/// "down" until the next one.
/// </para>
/// <para>
/// What can be said depends on the backend, and the text says so rather than implying a button that cannot be seen. Wintab tells the barrel buttons apart (1, 2, 3);
/// the pointer backends have one barrel flag, so it reads "Barrel" and never "Barrel 2". The tip is not shown here: it is already the pressure readout.
/// </para>
/// </remarks>
public sealed class ButtonReadout
{
    private readonly PenButtonTracker _tracker = new();
    private InputApi? _source;
    private bool _any;

    /// <summary>Feeds one pen point.</summary>
    public void Update(PenPoint pt)
    {
        _tracker.Update(pt);
        _source = pt.Source;
        _any = true;
    }

    /// <summary>Forgets everything (session restart, pen out of range). <see cref="Text"/> reads "--" until the next point.</summary>
    public void Reset()
    {
        _tracker.Reset();
        _source = null;
        _any = false;
    }

    private bool IsWintab => _source is InputApi.WintabSystem or InputApi.WintabDigitizer;

    /// <summary>The barrel switches currently held, e.g. <c>None</c>, <c>Barrel 1</c>, <c>Barrel 1 + 2</c> (Wintab), or <c>Barrel</c> (pointer backends); <c>--</c> before any point.</summary>
    public string Text
    {
        get
        {
            if (!_any) return "--";
            if (!IsWintab) return _tracker.IsBarrelDown(1) ? "Barrel" : "None";
            var down = new List<int>(3);
            for (int b = 1; b <= 3; b++) if (_tracker.IsBarrelDown(b)) down.Add(b);
            return down.Count == 0 ? "None" : "Barrel " + string.Join(" + ", down);
        }
    }

    /// <summary>The longest text <see cref="Text"/> can take, so a layout can reserve the width and not jump.</summary>
    public static string WidestText => "Barrel 1 + 2 + 3";

    /// <summary>A tooltip for the readout: the raw button value of the latest point and what the backend can distinguish.</summary>
    public string Detail
    {
        get
        {
            if (!_any) return "No pen point yet.";
            return IsWintab
                ? $"Raw {_tracker.LastRawButtons:X8} on the latest point (Wintab: the high word is the event, 1 pressed or 2 released, the low word the button). Wintab tells the barrel buttons apart."
                : $"Raw {_tracker.LastRawButtons:X8} on the latest point (bit 0 is the barrel flag). This backend has one barrel flag, so the buttons cannot be told apart.";
        }
    }
}
