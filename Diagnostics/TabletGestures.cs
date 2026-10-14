using System.Globalization;

namespace PenDynamicsLab.Diagnostics;

/// <summary>
/// Which Windows system gestures and pen feedback this window opts out of. Windows asks a window with <c>WM_TABLET_QUERYSYSTEMGESTURESTATUS</c> and the window answers with a mask of
/// the things to switch off (Microsoft: "Sent when the system asks a window which system gestures it would like to receive"; by default a window receives all of them).
/// </summary>
/// <remarks>
/// <para>
/// Drawing in this app should not trigger a right click or draw a ring under the pen. Three of the flags matter: the press-and-hold gesture (a held pen becomes a right click, after a
/// ring), the circle Windows draws when a pen button goes down, and the "wave" it draws on pen up. Values and descriptions are from Microsoft's page for the message
/// (<c>tpcshrd.h</c>): press and hold 0x1 ("disables press and hold (right-click) gesture"), pen tap feedback 0x8 ("disables UI feedback on pen up (waves)"), pen barrel feedback 0x10
/// ("disables UI feedback on pen button down (circle)").
/// </para>
/// <para>
/// The answer is a mask that can be overridden from the environment (<see cref="OverrideVariable"/>, hexadecimal) so each flag can be tried on its own without a rebuild; <c>0</c>
/// answers "disable nothing", which is Windows' default. The number of times Windows has asked is kept, because whether the window is asked at all is the first thing to know when
/// a ring does not go away.
/// </para>
/// </remarks>
public static class TabletGestures
{
    /// <summary><c>WM_TABLET_QUERYSYSTEMGESTURESTATUS</c> = <c>WM_TABLET_DEFBASE</c> (0x02C0) + 12.</summary>
    public const uint QueryMessage = 0x02CC;

    public const uint DisablePressAndHold = 0x00000001;
    public const uint DisablePenTapFeedback = 0x00000008;
    public const uint DisablePenBarrelFeedback = 0x00000010;

    /// <summary>What the window answers unless overridden: no press-and-hold right click, no tap wave, no barrel-button circle.</summary>
    public const uint Default = DisablePressAndHold | DisablePenTapFeedback | DisablePenBarrelFeedback;

    /// <summary>Environment variable holding the mask as hexadecimal (<c>0x</c> optional), for trying flags one at a time.</summary>
    public const string OverrideVariable = "PDL_TABLET_GESTURE_FLAGS";

    private static long _queries;

    /// <summary>The mask this window answers with.</summary>
    public static uint Flags { get; } = ParseOverride(Environment.GetEnvironmentVariable(OverrideVariable), Default);

    /// <summary>How many times Windows has asked this window which gestures it wants.</summary>
    public static long Queries => Interlocked.Read(ref _queries);

    /// <summary>Hexadecimal text to a mask; anything that is not hexadecimal (or blank) gives <paramref name="fallback"/>, so a typo never silently switches flags off.</summary>
    public static uint ParseOverride(string? text, uint fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    /// <summary>
    /// The window procedure hook (Avalonia's <c>Win32Properties.AddWndProcHookCallback</c> shape): answers the gesture query with <see cref="Flags"/> and leaves every other message alone.
    /// Windows sends the query with <c>SendMessageTimeout</c>, so this does nothing but count and answer.
    /// </summary>
    public static IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled) => Answer(msg, Flags, ref handled);

    /// <summary>The hook's decision, with the mask as a parameter so it can be tested.</summary>
    public static IntPtr Answer(uint msg, uint flags, ref bool handled)
    {
        if (msg != QueryMessage) return IntPtr.Zero;
        Interlocked.Increment(ref _queries);
        handled = true;
        return (IntPtr)flags;
    }

    /// <summary>One sentence for a tooltip: what the window answers and how often it has been asked.</summary>
    public static string Describe() =>
        $"Windows has asked this window about system gestures {Queries} time(s); it answers 0x{Flags:X} (0x1 press and hold, 0x8 pen tap wave, 0x10 pen button circle).";
}
