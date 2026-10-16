using NvProfileSwitcher.Platform;

namespace NvProfileSwitcher.Core.Tests;

// Records hotkey registrations instead of calling Win32, and keeps the registrations Win32
// would hold: a combination already registered cannot be registered again, an ID registered
// twice keeps both registrations, and unregistering an ID releases every registration it has.
// Registering also fails for the IDs in FailingIds. Key names are the letter or digit,
// "F<n>" or "VK<hex>", marked when extended.
internal sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
{
    private const uint ModifierMask = 0x0007;

    private readonly List<(int Id, uint Modifiers, uint VirtualKey)> active = [];

    public List<string> Log { get; } = [];
    public HashSet<int> FailingIds { get; } = [];

    /// <summary>Whether any registration call reached the registrar, which needs a window handle.</summary>
    public bool Attached { get; private set; }

    /// <summary>The registrations Win32 would hold now, in registration order.</summary>
    public IReadOnlyList<(int Id, uint Modifiers, uint VirtualKey)> Active => active;

    public bool Register(nint windowHandle, int id, uint modifiers, uint virtualKey)
    {
        Attached = true;
        Log.Add($"register {id} {modifiers:X4} {virtualKey:X2}");
        if (FailingIds.Contains(id) || IdOf(modifiers, virtualKey) is not null)
            return false;

        active.Add((id, modifiers & ModifierMask, virtualKey));
        return true;
    }

    public void Unregister(nint windowHandle, int id)
    {
        Attached = true;
        Log.Add($"unregister {id}");
        active.RemoveAll(registration => registration.Id == id);
    }

    public string GetKeyName(int virtualKey, bool extended)
    {
        var name = virtualKey switch
        {
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
            >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
            _ => $"VK{virtualKey:X2}",
        };
        return extended ? name + " (ext)" : name;
    }

    /// <summary>The ID holding the combination, as Win32 would report it with WM_HOTKEY, or null.</summary>
    public int? IdOf(uint modifiers, uint virtualKey)
    {
        foreach (var registration in active)
            if (registration.Modifiers == (modifiers & ModifierMask) && registration.VirtualKey == virtualKey)
                return registration.Id;
        return null;
    }

    /// <summary>The ID holding a stored hotkey value (flags &lt;&lt; 8 | virtual key), or null.</summary>
    public int? IdOf(int hotkey) => IdOf(Modifiers(hotkey), (uint)(hotkey & 0xFF));

    /// <summary>The stored hotkey values of the registrations Win32 would hold now.</summary>
    public HashSet<int> ActiveHotkeys() =>
        active.Select(registration => StoredValue(registration.Modifiers, registration.VirtualKey)).ToHashSet();

    // The registrations since the last unregistration of every hotkey.
    public List<string> Registered() => Log.Skip(Log.FindLastIndex(entry => entry.StartsWith("unregister", StringComparison.Ordinal)) + 1).ToList();

    // The Win32 modifiers of a stored hotkey: Alt=1, Ctrl=2, Shift=4 (stored Shift=1, Ctrl=2, Alt=4).
    private static uint Modifiers(int hotkey)
    {
        var flags = (hotkey >> 8) & 0xFF;
        var modifiers = 0u;
        if ((flags & 0x04) != 0) modifiers |= 0x0001;
        if ((flags & 0x02) != 0) modifiers |= 0x0002;
        if ((flags & 0x01) != 0) modifiers |= 0x0004;
        return modifiers;
    }

    private static int StoredValue(uint modifiers, uint virtualKey)
    {
        var flags = 0;
        if ((modifiers & 0x0001) != 0) flags |= 0x04;
        if ((modifiers & 0x0002) != 0) flags |= 0x02;
        if ((modifiers & 0x0004) != 0) flags |= 0x01;
        if (NvProfileSwitcher.Core.HotkeyKeyCore.IsExtendedVirtualKey((int)virtualKey)) flags |= 0x08;
        return (flags << 8) | (int)virtualKey;
    }
}
