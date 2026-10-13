using PenDynamicsLab.Diagnostics;
using WinPenKit;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// The buttons readout: what it says for each backend's encoding. Wintab reports one press or release per packet and the state has to be carried between packets;
/// the pointer backends repeat the whole state as a bitmask on every point.
/// </summary>
public class ButtonReadoutTests
{
    private static PenPoint Point(InputApi source, uint buttons = 0, uint pressure = 0, uint cursor = 0) =>
        new(DesktopX: 100, DesktopY: 100, RawX: 0, RawY: 0, Pressure: pressure, Azimuth: 0, Altitude: 90, Twist: 0, TiltX: 0, TiltY: 0, Z: 0,
            Status: 1, Buttons: buttons, Cursor: cursor, Source: source, TimestampMicroseconds: 0);

    private static uint Wintab(PenButtonAction action, int button) => ((uint)action << 16) | (uint)button;

    // ---- before any point --------------------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_is_shown_before_a_point_arrives()
    {
        var r = new ButtonReadout();
        Assert.Equal("--", r.Text);
        Assert.Contains("No pen point", r.Detail);
    }

    // ---- Wintab: events carried between packets ------------------------------------------------------------------------------

    [Theory]
    [InlineData(InputApi.WintabSystem)]
    [InlineData(InputApi.WintabDigitizer)]
    public void Wintab_carries_each_barrel_button_between_packets(InputApi api)
    {
        var r = new ButtonReadout();
        r.Update(Point(api));
        Assert.Equal("None", r.Text);

        r.Update(Point(api, Wintab(PenButtonAction.Pressed, 1)));
        Assert.Equal("Barrel 1", r.Text);

        r.Update(Point(api));                                              // a packet with no button event keeps the state
        r.Update(Point(api));
        Assert.Equal("Barrel 1", r.Text);

        r.Update(Point(api, Wintab(PenButtonAction.Pressed, 2)));
        Assert.Equal("Barrel 1 + 2", r.Text);

        r.Update(Point(api, Wintab(PenButtonAction.Released, 1)));
        Assert.Equal("Barrel 2", r.Text);

        r.Update(Point(api, Wintab(PenButtonAction.Released, 2)));
        Assert.Equal("None", r.Text);
    }

    [Fact]
    public void Wintab_can_show_all_three_buttons_and_the_widest_text_is_the_longest()
    {
        var r = new ButtonReadout();
        foreach (int b in new[] { 1, 2, 3 }) r.Update(Point(InputApi.WintabSystem, Wintab(PenButtonAction.Pressed, b)));
        Assert.Equal("Barrel 1 + 2 + 3", r.Text);
        Assert.Equal(ButtonReadout.WidestText, r.Text);
    }

    [Fact]
    public void The_tip_is_not_a_barrel_button()
    {
        var r = new ButtonReadout();
        r.Update(Point(InputApi.WintabSystem, Wintab(PenButtonAction.Pressed, PenButtonNumber.Tip), pressure: 500));
        Assert.Equal("None", r.Text);
    }

    [Fact]
    public void Reset_forgets_a_held_button_so_a_release_that_never_arrived_is_not_shown()
    {
        var r = new ButtonReadout();
        r.Update(Point(InputApi.WintabSystem, Wintab(PenButtonAction.Pressed, 1)));
        r.Reset();                                                         // the pen left range with the button down
        Assert.Equal("--", r.Text);
        r.Update(Point(InputApi.WintabSystem));
        Assert.Equal("None", r.Text);
    }

    // ---- pointer backends: the whole state on every point --------------------------------------------------------------------

    [Theory]
    [InlineData(InputApi.AvaloniaPointer)]
    [InlineData(InputApi.WmPointer)]
    [InlineData(InputApi.WpfStylus)]
    [InlineData(InputApi.WinUiPointer)]
    [InlineData(InputApi.WinFormsPointer)]
    public void Pointer_backends_show_one_barrel_that_follows_the_flag_on_each_point(InputApi api)
    {
        var r = new ButtonReadout();
        r.Update(Point(api, buttons: 0));
        Assert.Equal("None", r.Text);
        r.Update(Point(api, buttons: 1));
        Assert.Equal("Barrel", r.Text);
        r.Update(Point(api, buttons: 1));
        Assert.Equal("Barrel", r.Text);
        r.Update(Point(api, buttons: 0));
        Assert.Equal("None", r.Text);
    }

    [Fact]
    public void The_eraser_flag_on_a_pointer_backend_is_not_a_button()
    {
        var r = new ButtonReadout();
        r.Update(Point(InputApi.AvaloniaPointer, buttons: 2));             // bit 1 is the eraser flag
        Assert.Equal("None", r.Text);
    }

    [Fact]
    public void Pointer_backends_never_claim_to_tell_the_buttons_apart()
    {
        var r = new ButtonReadout();
        r.Update(Point(InputApi.AvaloniaPointer, buttons: 1));
        Assert.DoesNotContain("1", r.Text);
        Assert.Contains("cannot be told apart", r.Detail);
    }

    // ---- switching backend ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Changing_to_the_other_encoding_does_not_carry_the_old_state_over()
    {
        var r = new ButtonReadout();
        r.Update(Point(InputApi.WintabSystem, Wintab(PenButtonAction.Pressed, 1)));
        Assert.Equal("Barrel 1", r.Text);
        r.Update(Point(InputApi.AvaloniaPointer, buttons: 0));
        Assert.Equal("None", r.Text);
        r.Update(Point(InputApi.WintabSystem));
        Assert.Equal("None", r.Text);                                      // the Wintab press from before is gone
    }

    // ---- the detail text --------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_detail_shows_the_raw_value_of_the_latest_point()
    {
        var r = new ButtonReadout();
        r.Update(Point(InputApi.WintabSystem, Wintab(PenButtonAction.Pressed, 2)));
        Assert.Contains("00020002", r.Detail);
        r.Update(Point(InputApi.AvaloniaPointer, buttons: 3));
        Assert.Contains("00000003", r.Detail);
    }
}
