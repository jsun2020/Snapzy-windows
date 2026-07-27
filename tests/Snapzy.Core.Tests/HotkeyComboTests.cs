using Snapzy.Core.Hotkeys;
using Snapzy.Core.Settings;

public class HotkeyComboTests
{
    [Theory]
    [InlineData("Ctrl+Shift+3", 2u | 4u, 0x33u)]
    [InlineData("Ctrl+Shift+A", 2u | 4u, 0x41u)]
    [InlineData("Alt+F4",       1u,      0x73u)]
    [InlineData("Win+PrintScreen", 8u,   0x2Cu)]
    [InlineData("ctrl+shift+h", 2u | 4u, 0x48u)] // case-insensitive
    public void TryParse_Valid(string gesture, uint mods, uint vk)
    {
        Assert.True(HotkeyCombo.TryParse(gesture, out var c));
        Assert.Equal(mods, c.Modifiers);
        Assert.Equal(vk, c.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Banana+X")]
    [InlineData("Ctrl+Shift")] // no key, only modifiers
    public void TryParse_Invalid(string gesture) =>
        Assert.False(HotkeyCombo.TryParse(gesture, out _));

    [Fact]
    public void ToString_NormalizesOrderAndCase()
    {
        HotkeyCombo.TryParse("shift+ctrl+a", out var c);
        Assert.Equal("Ctrl+Shift+A", c.ToString());
    }

    [Fact]
    public void FindDuplicates_DetectsEnabledClashesOnly()
    {
        var map = new Dictionary<string, HotkeyBinding>
        {
            ["A1"] = new() { Gesture = "Ctrl+Shift+3", Enabled = true },
            ["A2"] = new() { Gesture = "ctrl+shift+3", Enabled = true },
            ["A3"] = new() { Gesture = "Ctrl+Shift+3", Enabled = false },
        };
        var dups = HotkeyConflicts.FindDuplicates(map);
        Assert.Single(dups);
        Assert.Equal(("A1", "A2"), dups[0]);
    }
}
