using Xunit;
namespace NvProfileSwitcher.Core.Tests;

public sealed class HotkeyCoreTests
{
    private static int Hotkey(int key, int flags = 0) => key | (flags << 8);

    [Fact]
    public void EmptyHotkeyIsAllowed() => Assert.False(HotkeyCore.NeedsModifier(0));

    [Fact]
    public void LetterRequiresCtrlOrAlt()
    {
        Assert.True(HotkeyCore.NeedsModifier(Hotkey('J')));
        Assert.True(HotkeyCore.NeedsModifier(Hotkey('J', HotkeyCore.Shift)));
        Assert.False(HotkeyCore.NeedsModifier(Hotkey('J', HotkeyCore.Control)));
        Assert.False(HotkeyCore.NeedsModifier(Hotkey('J', HotkeyCore.Alt)));
    }

    [Fact]
    public void FunctionKeysAreAllowedWithoutModifier()
    {
        Assert.False(HotkeyCore.NeedsModifier(Hotkey(0x70)));
        Assert.False(HotkeyCore.NeedsModifier(Hotkey(0x87)));
        Assert.True(HotkeyCore.NeedsModifier(Hotkey(0x6F)));
        Assert.True(HotkeyCore.NeedsModifier(Hotkey(0x88)));
    }

    [Fact]
    public void CtrlAltWithoutShiftIsBlockedForAltGrSafety()
    {
        Assert.True(HotkeyCore.IsCtrlAlt(Hotkey('J', HotkeyCore.Control | HotkeyCore.Alt)));
        Assert.False(HotkeyCore.IsCtrlAlt(Hotkey('J', HotkeyCore.Control | HotkeyCore.Alt | HotkeyCore.Shift)));
        Assert.False(HotkeyCore.IsCtrlAlt(Hotkey('J', HotkeyCore.Control)));
        Assert.False(HotkeyCore.IsCtrlAlt(Hotkey('J', HotkeyCore.Alt)));
    }

    [Fact]
    public void ConflictRequiresSameNonEmptyHotkey()
    {
        var ctrlJ = Hotkey('J', HotkeyCore.Control);
        Assert.True(HotkeyCore.Conflict(ctrlJ, ctrlJ));
        Assert.False(HotkeyCore.Conflict(ctrlJ, Hotkey('J', HotkeyCore.Alt)));
        Assert.False(HotkeyCore.Conflict(0, 0));
    }
}
