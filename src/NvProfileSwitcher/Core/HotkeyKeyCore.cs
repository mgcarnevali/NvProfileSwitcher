using Avalonia.Input;

namespace NvProfileSwitcher.Core;

/// <summary>
/// Maps Avalonia keys to Win32 virtual keys and builds and formats the stored hotkey value:
/// <c>(flags &lt;&lt; 8) | virtualKey</c>, with Shift=1, Ctrl=2, Alt=4 and Ext=8.
/// </summary>
internal static class HotkeyKeyCore
{
    public const int ShiftFlag = 0x01;
    public const int ControlFlag = 0x02;
    public const int AltFlag = 0x04;
    public const int ExtendedFlag = 0x08;

    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    public static bool IsExtendedVirtualKey(int virtualKey) => virtualKey is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x6F;

    /// <summary>The virtual key of <paramref name="key"/>, or 0 when it cannot be a hotkey.</summary>
    public static int VirtualKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return 0x41 + ((int)key - (int)Key.A);
        if (key >= Key.D0 && key <= Key.D9) return 0x30 + ((int)key - (int)Key.D0);
        if (key >= Key.F1 && key <= Key.F24) return 0x70 + ((int)key - (int)Key.F1);
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return 0x60 + ((int)key - (int)Key.NumPad0);
        return key switch
        {
            Key.Back => 0x08, Key.Tab => 0x09, Key.Enter => 0x0D, Key.Escape => 0x1B, Key.Space => 0x20,
            Key.PageUp => 0x21, Key.PageDown => 0x22, Key.End => 0x23, Key.Home => 0x24,
            Key.Left => 0x25, Key.Up => 0x26, Key.Right => 0x27, Key.Down => 0x28,
            Key.Insert => 0x2D, Key.Delete => 0x2E, Key.Multiply => 0x6A, Key.Add => 0x6B,
            Key.Subtract => 0x6D, Key.Decimal => 0x6E, Key.Divide => 0x6F,
            Key.OemSemicolon => 0xBA, Key.OemPlus => 0xBB, Key.OemComma => 0xBC, Key.OemMinus => 0xBD,
            Key.OemPeriod => 0xBE, Key.OemQuestion => 0xBF, Key.OemTilde => 0xC0,
            Key.OemOpenBrackets => 0xDB, Key.OemPipe => 0xDC, Key.OemCloseBrackets => 0xDD, Key.OemQuotes => 0xDE,
            _ => 0
        };
    }

    public static int Compose(int virtualKey, KeyModifiers modifiers)
    {
        var flags = 0;
        if (modifiers.HasFlag(KeyModifiers.Shift)) flags |= ShiftFlag;
        if (modifiers.HasFlag(KeyModifiers.Control)) flags |= ControlFlag;
        if (modifiers.HasFlag(KeyModifiers.Alt)) flags |= AltFlag;
        if (IsExtendedVirtualKey(virtualKey)) flags |= ExtendedFlag;
        return (flags << 8) | virtualKey;
    }

    /// <summary>
    /// "None" for no hotkey; otherwise the modifiers followed by the key name that
    /// <paramref name="keyName"/> returns for the virtual key and its extended flag.
    /// </summary>
    public static string Format(int hotkey, Func<int, bool, string> keyName)
    {
        if (hotkey == 0) return "None";

        var flags = (hotkey >> 8) & 0xFF;
        var text = string.Empty;
        if ((flags & ControlFlag) != 0) text += "CTRL + ";
        if ((flags & AltFlag) != 0) text += "ALT + ";
        if ((flags & ShiftFlag) != 0) text += "SHIFT + ";
        return text + keyName(hotkey & 0xFF, (flags & ExtendedFlag) != 0);
    }
}
