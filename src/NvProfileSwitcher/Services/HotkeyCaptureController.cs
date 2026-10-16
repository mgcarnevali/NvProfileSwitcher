using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services.Editing;

namespace NvProfileSwitcher.Services;

/// <summary>
/// Captures a new global hotkey for one field at a time. While capturing, every hotkey is
/// unregistered and the field shows a blinking caret; a failed validation shows its message
/// and keeps the capture, with the hotkeys still unregistered, until it ends. Every way of
/// ending a capture goes through one place, which registers the hotkeys again. Global
/// hotkeys are saved at once; the profile hotkey goes to the draft being edited.
/// </summary>
internal sealed partial class HotkeyCaptureController : ObservableObject
{
    private readonly HotkeyService hotkeys;
    private readonly AppSettings settings;
    private readonly ProfileStore profiles;
    private readonly ConfigurationService configuration;
    private readonly ProfileEditor editor;
    private readonly Func<bool> isWindowActive;
    private readonly DispatcherTimer caretTimer = new() { Interval = TimeSpan.FromMilliseconds(530) };

    public HotkeyCaptureController(
        HotkeyService hotkeys,
        AppSettings settings,
        ProfileStore profiles,
        ConfigurationService configuration,
        ProfileEditor editor,
        Func<bool> isWindowActive)
    {
        this.hotkeys = hotkeys;
        this.settings = settings;
        this.profiles = profiles;
        this.configuration = configuration;
        this.editor = editor;
        this.isWindowActive = isWindowActive;
        caretTimer.Tick += (_, _) => ToggleCaret();
        settings.PropertyChanged += (_, _) => StoredHotkeyChanged?.Invoke();
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProfileEditor.ProfileHotkey))
                StoredHotkeyChanged?.Invoke();
        };
    }

    /// <summary>Raised when the value a field shows may have changed.</summary>
    public event Action? StoredHotkeyChanged;

    /// <summary>Asks the view to focus the field again after a validation message.</summary>
    public event Action<HotkeyField>? FocusRequested;

    /// <summary>The field being captured, or null.</summary>
    [ObservableProperty] public partial HotkeyField? CaptureTarget { get; private set; }

    [ObservableProperty] public partial bool IsCaretVisible { get; private set; }

    /// <summary>Starts capturing <paramref name="field"/> when the main window is active.</summary>
    public void Begin(HotkeyField field)
    {
        if (!isWindowActive())
            return;

        CaptureTarget = field;
        hotkeys.UnregisterAll();
        IsCaretVisible = true;
        caretTimer.Stop();
        caretTimer.Start();
    }

    /// <summary>
    /// Handles a key pressed on <paramref name="field"/> and returns true when the key was
    /// consumed. Only the field being captured consumes keys.
    /// </summary>
    public bool KeyPressed(HotkeyField field, Key key, KeyModifiers modifiers)
    {
        if (CaptureTarget != field) return false;
        if (HotkeyKeyCore.IsModifierKey(key)) return true;
        if (key == Key.Escape)
        {
            Store(field, 0);
            End();
            return true;
        }

        var virtualKey = HotkeyKeyCore.VirtualKey(key);
        if (virtualKey == 0) return true;

        _ = ApplyAsync(field, HotkeyKeyCore.Compose(virtualKey, modifiers));
        return true;
    }

    /// <summary>Ends the capture, if any, and registers the hotkeys again.</summary>
    public void End() => EndCore();

    /// <summary>
    /// Ends the capture unless a hotkey message is open: the message takes the focus and
    /// the activation from the main window without ending the capture.
    /// </summary>
    public void EndUnlessMessageOpen()
    {
        if (hotkeys.IsMessageDialogOpen)
            return;

        EndCore();
    }

    /// <summary>
    /// The field lost the focus: ends its capture unless a hotkey message is open. Other
    /// fields losing the focus never end the capture (the original ended it for any field).
    /// </summary>
    public void FocusLost(HotkeyField field)
    {
        if (CaptureTarget != field)
            return;

        EndUnlessMessageOpen();
    }

    public int StoredHotkey(HotkeyField field) => field switch
    {
        HotkeyField.ShowHide => settings.ShowHideHotkey,
        HotkeyField.WindowsOverride => settings.WindowsOverrideHotkey,
        HotkeyField.ResumeAutomatic => settings.ResumeAutomaticHotkey,
        HotkeyField.GlobalSwitching => settings.GlobalSwitchingHotkey,
        _ => editor.ProfileHotkey,
    };

    private async Task ApplyAsync(HotkeyField field, int requested)
    {
        if (requested == StoredHotkey(field))
        {
            End();
            return;
        }

        hotkeys.UnregisterAll();
        var message = ValidationMessage(field, requested);
        if (message is not null)
        {
            await hotkeys.ShowMessageAsync(message);
            if (CaptureTarget == field)
                FocusRequested?.Invoke(field);
            return;
        }

        Store(field, requested);
        End();
    }

    // The original checks, in their order.
    private string? ValidationMessage(HotkeyField field, int requested)
    {
        if (HotkeyCore.NeedsModifier(requested))
            return "Hotkeys must use Ctrl or Alt with another key. Function keys can be used alone.\n\nUse Ctrl + Shift, Alt + Shift, or a function key instead.";
        if (HotkeyCore.IsCtrlAlt(requested))
            return "Ctrl + Alt hotkeys are not supported because Windows may treat Right Alt (AltGr) as Ctrl + Alt.\n\nUse Ctrl + Shift, Alt + Shift, or a function key instead.";
        var owner = OwnerName(requested, field);
        if (!string.IsNullOrEmpty(owner))
            return $"That hotkey is already assigned to {owner}.\n\nChoose a different combination.";
        if (!hotkeys.TryTest(requested))
            return "That hotkey is already in use by another application.";
        return null;
    }

    private string OwnerName(int hotkey, HotkeyField excluded)
    {
        if (excluded != HotkeyField.ShowHide && HotkeyCore.Conflict(settings.ShowHideHotkey, hotkey)) return "Show / hide window";
        if (excluded != HotkeyField.WindowsOverride && HotkeyCore.Conflict(settings.WindowsOverrideHotkey, hotkey)) return "Windows Override";
        if (excluded != HotkeyField.ResumeAutomatic && HotkeyCore.Conflict(settings.ResumeAutomaticHotkey, hotkey)) return "Resume auto switching";
        if (excluded != HotkeyField.GlobalSwitching && HotkeyCore.Conflict(settings.GlobalSwitchingHotkey, hotkey)) return "Global Switching";
        // The unsaved new profile has no hotkey of its own yet, and the edited profile's saved
        // hotkey is being replaced by the profile field.
        foreach (var profile in profiles.ApplicationProfiles.Where(p => !ReferenceEquals(p, profiles.UnsavedNewProfile)))
            if (!(excluded == HotkeyField.Profile && ReferenceEquals(profile, editor.SelectedProfile)) &&
                HotkeyCore.Conflict(profile.Hotkey, hotkey)) return profile.Name;
        return string.Empty;
    }

    // Global hotkeys are saved at once. A failed save is reported by the configuration
    // service instead of closing the application (deliberate deviation): the hotkey stays
    // active and is saved with the next successful save. The profile hotkey only changes
    // the draft; it is saved with the profile.
    private void Store(HotkeyField field, int hotkey)
    {
        if (field == HotkeyField.Profile)
        {
            editor.SetDraftHotkey(hotkey);
            return;
        }

        switch (field)
        {
            case HotkeyField.ShowHide: settings.ShowHideHotkey = hotkey; break;
            case HotkeyField.WindowsOverride: settings.WindowsOverrideHotkey = hotkey; break;
            case HotkeyField.ResumeAutomatic: settings.ResumeAutomaticHotkey = hotkey; break;
            case HotkeyField.GlobalSwitching: settings.GlobalSwitchingHotkey = hotkey; break;
        }

        configuration.Save();
    }

    // The only place a capture ends. The hotkeys are always registered again; the message
    // about saved hotkeys that could not be enabled is shown only when a capture actually
    // ended (deliberate deviation: the original showed it on every deactivation).
    private void EndCore()
    {
        var wasCapturing = CaptureTarget is not null;
        CaptureTarget = null;
        IsCaretVisible = false;
        caretTimer.Stop();
        hotkeys.Refresh(showAggregateMessage: wasCapturing);
    }

    private void ToggleCaret()
    {
        if (CaptureTarget is null)
        {
            caretTimer.Stop();
            return;
        }

        IsCaretVisible = !IsCaretVisible;
    }
}
