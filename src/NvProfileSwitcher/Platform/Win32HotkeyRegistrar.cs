using System.Runtime.InteropServices;
using System.Text;

namespace NvProfileSwitcher.Platform;

internal sealed class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    public bool Register(nint windowHandle, int id, uint modifiers, uint virtualKey) =>
        RegisterHotKey(windowHandle, id, modifiers, virtualKey);

    public void Unregister(nint windowHandle, int id) => UnregisterHotKey(windowHandle, id);

    public string GetKeyName(int virtualKey, bool extended)
    {
        var scan = MapVirtualKey((uint)virtualKey, 0) << 16;
        if (extended) scan |= 1u << 24;
        var keyName = new StringBuilder(64);
        return GetKeyNameText(unchecked((int)scan), keyName, keyName.Capacity) > 0 ? keyName.ToString() : string.Empty;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetKeyNameText(int lParam, StringBuilder lpString, int cchSize);
}
