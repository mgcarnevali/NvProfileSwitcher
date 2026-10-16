namespace NvProfileSwitcher.Platform;

/// <summary>
/// Registers global hotkeys for a window and names keys. <see cref="Win32HotkeyRegistrar"/>
/// uses the Win32 API; tests use a fake.
/// </summary>
internal interface IHotkeyRegistrar
{
    bool Register(nint windowHandle, int id, uint modifiers, uint virtualKey);

    void Unregister(nint windowHandle, int id);

    /// <summary>The keyboard layout name of the key, or an empty string when it has none.</summary>
    string GetKeyName(int virtualKey, bool extended);
}
