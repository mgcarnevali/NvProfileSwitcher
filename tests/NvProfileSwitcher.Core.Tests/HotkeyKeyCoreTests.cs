using Avalonia.Input;
using NvProfileSwitcher.Core;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// The key mapping, value composition and text of the original hotkey code.
public sealed class HotkeyKeyCoreTests
{
    [Theory]
    [InlineData("A", 0x41)]
    [InlineData("Z", 0x5A)]
    [InlineData("D0", 0x30)]
    [InlineData("D9", 0x39)]
    [InlineData("F1", 0x70)]
    [InlineData("F24", 0x87)]
    [InlineData("NumPad0", 0x60)]
    [InlineData("NumPad9", 0x69)]
    [InlineData("Back", 0x08)]
    [InlineData("Tab", 0x09)]
    [InlineData("Enter", 0x0D)]
    [InlineData("Escape", 0x1B)]
    [InlineData("Space", 0x20)]
    [InlineData("PageUp", 0x21)]
    [InlineData("PageDown", 0x22)]
    [InlineData("End", 0x23)]
    [InlineData("Home", 0x24)]
    [InlineData("Left", 0x25)]
    [InlineData("Up", 0x26)]
    [InlineData("Right", 0x27)]
    [InlineData("Down", 0x28)]
    [InlineData("Insert", 0x2D)]
    [InlineData("Delete", 0x2E)]
    [InlineData("Multiply", 0x6A)]
    [InlineData("Add", 0x6B)]
    [InlineData("Subtract", 0x6D)]
    [InlineData("Decimal", 0x6E)]
    [InlineData("Divide", 0x6F)]
    [InlineData("OemSemicolon", 0xBA)]
    [InlineData("OemPlus", 0xBB)]
    [InlineData("OemComma", 0xBC)]
    [InlineData("OemMinus", 0xBD)]
    [InlineData("OemPeriod", 0xBE)]
    [InlineData("OemQuestion", 0xBF)]
    [InlineData("OemTilde", 0xC0)]
    [InlineData("OemOpenBrackets", 0xDB)]
    [InlineData("OemPipe", 0xDC)]
    [InlineData("OemCloseBrackets", 0xDD)]
    [InlineData("OemQuotes", 0xDE)]
    [InlineData("CapsLock", 0)]
    [InlineData("PrintScreen", 0)]
    public void MapsKeysToVirtualKeys(string key, int virtualKey) =>
        Assert.Equal(virtualKey, HotkeyKeyCore.VirtualKey(Enum.Parse<Key>(key)));

    [Theory]
    [InlineData("LeftCtrl", true)]
    [InlineData("RightCtrl", true)]
    [InlineData("LeftShift", true)]
    [InlineData("RightShift", true)]
    [InlineData("LeftAlt", true)]
    [InlineData("RightAlt", true)]
    [InlineData("LWin", true)]
    [InlineData("RWin", true)]
    [InlineData("A", false)]
    [InlineData("F1", false)]
    public void RecognizesModifierKeys(string key, bool modifier) =>
        Assert.Equal(modifier, HotkeyKeyCore.IsModifierKey(Enum.Parse<Key>(key)));

    [Fact]
    public void ExtendedKeysAreTheNavigationKeysAndDivide()
    {
        int[] extended = [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x6F];
        for (var virtualKey = 0; virtualKey <= 0xFF; virtualKey++)
            Assert.Equal(extended.Contains(virtualKey), HotkeyKeyCore.IsExtendedVirtualKey(virtualKey));
    }

    [Fact]
    public void ComposesFlagsAboveTheVirtualKey()
    {
        Assert.Equal(0x0141, HotkeyKeyCore.Compose(0x41, KeyModifiers.Shift));
        Assert.Equal(0x0241, HotkeyKeyCore.Compose(0x41, KeyModifiers.Control));
        Assert.Equal(0x0441, HotkeyKeyCore.Compose(0x41, KeyModifiers.Alt));
        Assert.Equal(0x0741, HotkeyKeyCore.Compose(0x41, KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt));
        Assert.Equal(0x0A2E, HotkeyKeyCore.Compose(0x2E, KeyModifiers.Control));
        Assert.Equal(0x0070, HotkeyKeyCore.Compose(0x70, KeyModifiers.Meta));
    }

    [Theory]
    [InlineData(0, "None")]
    [InlineData(0x0270, "CTRL + F1")]
    [InlineData(0x0441, "ALT + A")]
    [InlineData(0x0141, "SHIFT + A")]
    [InlineData(0x0741, "CTRL + ALT + SHIFT + A")]
    [InlineData(0x0A2E, "CTRL + VK2E (ext)")]
    [InlineData(0x0070, "F1")]
    public void FormatsModifiersBeforeTheKeyName(int hotkey, string text) =>
        Assert.Equal(text, HotkeyKeyCore.Format(hotkey, new FakeHotkeyRegistrar().GetKeyName));

    [Fact]
    public void KeyWithoutNameKeepsOnlyTheModifiers() =>
        Assert.Equal("CTRL + ", HotkeyKeyCore.Format(0x02FF, (_, _) => string.Empty));
}
