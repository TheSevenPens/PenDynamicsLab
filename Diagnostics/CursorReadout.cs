using WinPenKit;

namespace PenDynamicsLab.Diagnostics;

/// <summary>
/// The text for the ribbon's Cursor line: the cursor number the pen reports and, for the two ends of a pen, which one it is. Pure, so it is tested directly.
/// </summary>
/// <remarks>
/// Wintab numbers the cursors itself and the pen backends follow the same convention (<see cref="PenCursorType"/>: 13 is the nib, 14 the eraser). A number that is not one
/// of those two is shown as it is, with no name, because a guess would read as a measurement.
/// </remarks>
public static class CursorReadout
{
    public static string Text(uint cursor) => cursor switch
    {
        PenCursorType.PenTip => $"{cursor} (pen)",
        PenCursorType.Eraser => $"{cursor} (eraser)",
        _ => cursor.ToString(),
    };
}
