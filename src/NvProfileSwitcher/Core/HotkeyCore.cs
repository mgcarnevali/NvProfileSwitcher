namespace NvProfileSwitcher;

internal static class HotkeyCore
{
    internal const int Shift = 0x01;
    internal const int Control = 0x02;
    internal const int Alt = 0x04;

    internal static bool Conflict(int a, int b) => a != 0 && b != 0 && a == b;

    internal static bool IsCtrlAlt(int hotkey)
    {
        var flags = (hotkey >> 8) & 0xFF;
        return (flags & (Control | Alt)) == (Control | Alt) && (flags & Shift) == 0;
    }

    internal static bool NeedsModifier(int hotkey)
    {
        var vk = hotkey & 0xFF;
        if (vk == 0) return false;
        var flags = (hotkey >> 8) & 0xFF;
        var functionKey = vk >= 0x70 && vk <= 0x87; // F1-F24
        return !functionKey && (flags & (Control | Alt)) == 0;
    }
}
