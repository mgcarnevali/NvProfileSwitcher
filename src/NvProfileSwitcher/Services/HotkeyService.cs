using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Platform;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Services;

internal enum HotkeyAction
{
    ShowHide,
    WindowsOverride,
    ResumeAutomatic,
    GlobalSwitching,
    Profile,
}

/// <summary>
/// Registers the saved hotkeys on the main window, dispatches WM_HOTKEY as
/// <see cref="Triggered"/> and shows the hotkey messages. IDs: 3001–3004 for the global
/// hotkeys, 3005 for the test registration and, from 4000, one per application profile hotkey.
/// A profile hotkey stays with its profile: the profile's position is looked up when the key is
/// pressed, so changing the list never makes a hotkey trigger another profile. Releasing frees
/// exactly what was registered. After a change of what should be registered (a profile enabled
/// or disabled, a profile hotkey saved, profiles removed or replaced), the owner of the change
/// calls <see cref="Refresh"/>.
/// </summary>
internal sealed class HotkeyService(IHotkeyRegistrar registrar, AppSettings settings, ProfileStore profiles, IDialogService dialogs)
{
    public const int ShowHideId = 3001;
    public const int WindowsOverrideId = 3002;
    public const int ResumeAutomaticId = 3003;
    public const int GlobalSwitchingId = 3004;
    public const int TestId = 3005;
    public const int ProfileBaseId = 4000;
    private const int LastProfileId = 0xBFFF;
    private const uint WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const string MessageTitle = "NvProfileSwitcher";

    private nint windowHandle;
    private bool registered;

    // What is registered now: every registered ID, and the profile of each profile hotkey ID.
    private readonly List<int> registeredIds = [];
    private readonly Dictionary<int, ProfileListItem> profileHotkeys = [];

    /// <summary>The application profile index is set for <see cref="HotkeyAction.Profile"/>.</summary>
    public event Action<HotkeyAction, int>? Triggered;

    /// <summary>True while a hotkey message is shown; capture does not end meanwhile.</summary>
    public bool IsMessageDialogOpen { get; private set; }

    /// <summary>
    /// Takes the main window handle and registers the saved hotkeys the first time a handle
    /// is available. Later calls do nothing, as the window is shown again but never recreated.
    /// </summary>
    public void Attach(nint handle)
    {
        if (windowHandle != 0 || handle == 0) return;

        windowHandle = handle;
        RegisterAll(showAggregateMessage: true);
    }

    public string Format(int hotkey) => HotkeyKeyCore.Format(hotkey, registrar.GetKeyName);

    public void RegisterAll(bool showAggregateMessage)
    {
        if (windowHandle == 0 || registered) return;

        var unavailable = false;
        if (!RegisterTracked(ShowHideId, settings.ShowHideHotkey)) unavailable = true;
        if (!RegisterTracked(WindowsOverrideId, settings.WindowsOverrideHotkey)) unavailable = true;
        if (!RegisterTracked(ResumeAutomaticId, settings.ResumeAutomaticHotkey)) unavailable = true;
        if (!RegisterTracked(GlobalSwitchingId, settings.GlobalSwitchingHotkey)) unavailable = true;
        var applicationProfiles = profiles.ApplicationProfiles;
        for (var i = 0; i < applicationProfiles.Count; i++)
        {
            var profile = applicationProfiles[i];
            if (!profile.Enabled) continue;
            var id = ProfileBaseId + i;
            if (id > LastProfileId) { unavailable = true; break; }
            if (!RegisterTracked(id, profile.Hotkey)) unavailable = true;
            else if (profile.Hotkey != 0) profileHotkeys[id] = profile;
        }

        registered = true;
        if (unavailable && showAggregateMessage && !IsMessageDialogOpen)
            _ = ShowMessageAsync("One or more saved hotkeys could not be enabled.\n\nChoose a supported, unused combination in Hotkeys.");
    }

    /// <summary>Releases every registered hotkey, and the test registration in case it was left.</summary>
    public void UnregisterAll()
    {
        if (windowHandle == 0) return;

        foreach (var id in registeredIds)
            registrar.Unregister(windowHandle, id);
        registrar.Unregister(windowHandle, TestId);
        registeredIds.Clear();
        profileHotkeys.Clear();
        registered = false;
    }

    /// <summary>Registers the saved hotkeys again, after a change of what should be registered.</summary>
    public void Refresh(bool showAggregateMessage)
    {
        UnregisterAll();
        RegisterAll(showAggregateMessage);
    }

    /// <summary>True when no other application uses the hotkey: registers it with the test ID and releases it.</summary>
    public bool TryTest(int hotkey)
    {
        if (!Register(TestId, hotkey)) return false;

        registrar.Unregister(windowHandle, TestId);
        return true;
    }

    /// <summary>Window message handler: returns true when the message was one of our hotkeys.</summary>
    public bool HandleMessage(uint msg, nint wParam)
    {
        if (msg != WmHotkey) return false;

        var id = (int)wParam;
        if (id == ShowHideId) Triggered?.Invoke(HotkeyAction.ShowHide, -1);
        else if (id == WindowsOverrideId) Triggered?.Invoke(HotkeyAction.WindowsOverride, -1);
        else if (id == ResumeAutomaticId) Triggered?.Invoke(HotkeyAction.ResumeAutomatic, -1);
        else if (id == GlobalSwitchingId) Triggered?.Invoke(HotkeyAction.GlobalSwitching, -1);
        else if (id >= ProfileBaseId)
        {
            // A profile removed since the registration is no longer in the list and is ignored.
            if (profileHotkeys.GetValueOrDefault(id) is { } profile &&
                profiles.ApplicationProfiles.ToList().IndexOf(profile) is var index and >= 0)
                Triggered?.Invoke(HotkeyAction.Profile, index);
        }
        else return false;

        return true;
    }

    /// <summary>
    /// Shows a hotkey message: modal over the main window, or standalone while it is hidden
    /// or minimized (deliberate deviation: the original was always modal).
    /// </summary>
    public async Task ShowMessageAsync(string message)
    {
        IsMessageDialogOpen = true;
        try
        {
            await dialogs.ShowMessageAsync(MessageTitle, message);
        }
        finally
        {
            IsMessageDialogOpen = false;
        }
    }

    // Registers the hotkey and remembers the ID when something was registered.
    private bool RegisterTracked(int id, int hotkey)
    {
        if (!Register(id, hotkey)) return false;
        if (hotkey != 0) registeredIds.Add(id);
        return true;
    }

    private bool Register(int id, int hotkey)
    {
        if (hotkey == 0) return true;
        if (HotkeyCore.IsCtrlAlt(hotkey) || windowHandle == 0) return false;

        var flags = (hotkey >> 8) & 0xFF;
        var modifiers = ModNoRepeat;
        if ((flags & HotkeyKeyCore.AltFlag) != 0) modifiers |= ModAlt;
        if ((flags & HotkeyKeyCore.ControlFlag) != 0) modifiers |= ModControl;
        if ((flags & HotkeyKeyCore.ShiftFlag) != 0) modifiers |= ModShift;
        return registrar.Register(windowHandle, id, modifiers, (uint)(hotkey & 0xFF));
    }
}
