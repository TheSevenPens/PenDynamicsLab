using PenDynamicsLab.Diagnostics;
using Xunit;

namespace PenDynamicsLab.Tests;

public class TabletGesturesTests
{
    // Values restated on purpose: the tests must fail if the constants drift from Microsoft's documented ones (tpcshrd.h).
    [Fact]
    public void The_message_and_flag_values_are_microsofts()
    {
        Assert.Equal(0x02C0u + 12, TabletGestures.QueryMessage);
        Assert.Equal(0x1u, TabletGestures.DisablePressAndHold);
        Assert.Equal(0x8u, TabletGestures.DisablePenTapFeedback);
        Assert.Equal(0x10u, TabletGestures.DisablePenBarrelFeedback);
        Assert.Equal(0x19u, TabletGestures.Default);
    }

    [Fact]
    public void The_query_is_answered_with_the_mask_and_marked_handled()
    {
        bool handled = false;
        var before = TabletGestures.Queries;
        var r = TabletGestures.Answer(TabletGestures.QueryMessage, 0x19, ref handled);
        Assert.True(handled);
        Assert.Equal((IntPtr)0x19, r);
        Assert.Equal(before + 1, TabletGestures.Queries);
    }

    [Fact]
    public void A_mask_of_zero_is_still_an_answer_it_means_disable_nothing()
    {
        bool handled = false;
        Assert.Equal(IntPtr.Zero, TabletGestures.Answer(TabletGestures.QueryMessage, 0, ref handled));
        Assert.True(handled);
    }

    [Theory]
    [InlineData(0x0200u)]
    [InlineData(0x02CBu)]
    [InlineData(0x02CDu)]
    [InlineData(0x0000u)]
    public void Every_other_message_is_left_alone(uint msg)
    {
        bool handled = false;
        var before = TabletGestures.Queries;
        Assert.Equal(IntPtr.Zero, TabletGestures.Answer(msg, 0x19, ref handled));
        Assert.False(handled);
        Assert.Equal(before, TabletGestures.Queries);
    }

    [Theory]
    [InlineData("0x10", 0x10u)]
    [InlineData("0X10", 0x10u)]
    [InlineData("10", 0x10u)]
    [InlineData(" 19 ", 0x19u)]
    [InlineData("0", 0u)]
    [InlineData("0x00000001", 1u)]
    [InlineData("FFFFFFFF", 0xFFFFFFFFu)]
    public void The_override_is_hexadecimal(string text, uint expected) => Assert.Equal(expected, TabletGestures.ParseOverride(text, 0x19));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("banana")]
    [InlineData("0x")]
    [InlineData("-1")]
    [InlineData("1FFFFFFFF")]
    public void A_missing_or_bad_override_keeps_the_default_so_a_typo_does_not_switch_flags_off(string? text) =>
        Assert.Equal(0x19u, TabletGestures.ParseOverride(text, 0x19));

    [Fact]
    public void The_description_names_the_flags_and_the_count()
    {
        Assert.Contains("0x1 press and hold", TabletGestures.Describe());
        Assert.Contains("0x10 pen button circle", TabletGestures.Describe());
        Assert.Contains("time(s)", TabletGestures.Describe());
    }
}
