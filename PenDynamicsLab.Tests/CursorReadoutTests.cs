using PenDynamicsLab.Diagnostics;
using WinPenKit;
using Xunit;

namespace PenDynamicsLab.Tests;

public class CursorReadoutTests
{
    [Fact]
    public void The_nib_and_the_eraser_are_named_beside_their_numbers()
    {
        Assert.Equal("13 (pen)", CursorReadout.Text(PenCursorType.PenTip));
        Assert.Equal("14 (eraser)", CursorReadout.Text(PenCursorType.Eraser));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(15u)]
    [InlineData(4294967295u)]
    public void Any_other_number_is_shown_as_it_is_without_a_guessed_name(uint cursor) =>
        Assert.Equal(cursor.ToString(), CursorReadout.Text(cursor));
}
