using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Services.Editing;

/// <summary>
/// The editing session's actions that reach beyond the editor's state: confirming the discard
/// of unsaved changes, selecting another profile, saving, cancelling, copying settings and the
/// slider changes that preview the colors. Each one runs the original sequence. It is the only
/// place that asks whether to discard unsaved changes, for every action that needs it.
/// </summary>
internal sealed class ProfileEditingService(
    ProfileEditor editor,
    ProfileStore store,
    LivePreviewController preview,
    ConfigurationService configuration,
    HotkeyService hotkeys,
    ActiveProfileService activeProfile,
    HotkeyCaptureController capture,
    DisplayColorController colors,
    IDialogService dialogs)
{
    // A discard confirmation is open; another request while it is shown does not open a second one.
    private bool confirming;

    /// <summary>
    /// With unsaved changes, asks whether to discard them and discards them, removing an unsaved
    /// new profile; true when there is nothing to keep any more.
    /// </summary>
    public async Task<bool> ConfirmAndDiscardAsync()
    {
        if (!editor.IsDirty)
            return true;
        if (!await ConfirmDiscardAsync())
            return false;

        editor.Discard(removeUnsavedProfile: true);
        return true;
    }

    /// <summary>
    /// Handles a new list selection as the original selection handler did: with unsaved
    /// changes in another profile, the list goes back to the edited profile and the discard
    /// is confirmed first; then the requested profile starts being edited.
    /// </summary>
    public async Task RequestSelectionAsync(ProfileListItem requested)
    {
        editor.SetSelection(requested);
        if (editor.Session is { } session && !ReferenceEquals(requested, session.Source) && editor.IsDirty)
        {
            editor.SetSelection(session.Source);
            if (!await ConfirmAndDiscardAsync())
                return;

            editor.SetSelection(requested);
        }

        editor.Begin(requested);
    }

    /// <summary>The user moved a slider: the draft takes the values and the preview shows them.</summary>
    public void SetValues(ColorValues values)
    {
        editor.SetCurrentValues(values);
        preview.Preview();
    }

    /// <summary>Shows the NVIDIA defaults for the visible display and previews them; nothing is saved.</summary>
    public void Reset()
    {
        editor.ResetValues();
        preview.Preview();
    }

    /// <summary>Copies the color settings of a chosen profile into the draft and previews them; nothing is saved.</summary>
    public async Task CopySettingsAsync()
    {
        if (editor.Session is not { } session)
            return;

        editor.CompleteDraft();
        var candidates = store.Profiles.Where(profile => !ReferenceEquals(profile, session.Source)).ToList();
        if (candidates.Count == 0)
            return;

        if (await dialogs.ChooseCopySourceAsync(candidates.Select(profile => profile.Name).ToList()) is not int index)
            return;

        if (!editor.CopyColorsFrom(candidates[index]))
            return;

        preview.Preview();
    }

    /// <summary>
    /// Discards the unsaved changes once confirmed and edits the profile again on the same
    /// display, with the real colors applied. Without changes, it only ends a capture of the
    /// profile hotkey.
    /// </summary>
    public async Task CancelAsync()
    {
        var profileHotkeyCaptureActive = capture.CaptureTarget == HotkeyField.Profile;
        if (!editor.IsDirty)
        {
            if (profileHotkeyCaptureActive)
                capture.End();
            return;
        }

        if (!await ConfirmDiscardAsync()) return;
        var source = editor.Session?.Source;
        var selectedMonitorId = editor.SelectedDisplay?.MonitorId;
        editor.Discard(removeUnsavedProfile: true);
        var target = source is not null && store.Profiles.Contains(source) ? source : editor.SelectedProfile;
        if (target is not null)
        {
            editor.SetSelection(target);
            editor.Begin(target, selectedMonitorId);
        }

        // The discard gave the previewed display back; the others already show the real colors.
        activeProfile.ReapplyRealColors(allDisplays: false);
    }

    /// <summary>
    /// Validates and saves the draft, then applies it. The profile is saved first and applied
    /// only once saved, so a failed save leaves the state, the hotkeys and the preview as they
    /// were, with the draft still unsaved (deliberate deviation: the original applied the draft
    /// first and ignored the failure).
    /// </summary>
    public async Task SaveAsync()
    {
        if (editor.Session is not { } session)
            return;

        var source = session.Source;
        var draft = session.Draft;
        editor.CompleteDraft();
        if (!draft.IsWindowsProfile)
            draft.Name = draft.Name.Trim();
        if (ProfileValidationCore.Message(draft) is { } message)
        {
            await dialogs.ShowMessageAsync("Cannot save profile", message);
            return;
        }

        if (editor.SelectedDisplay is not { } display)
            return;

        // The saved profile is the one the draft would be copied into: Enabled stays the
        // profile's own, as the list may have changed it while editing.
        var saved = ProfileEditingCore.CloneProfile(source);
        ProfileEditingCore.CopyProfile(draft, saved);
        if (!configuration.Save(source, saved, reportFailure: false))
        {
            await dialogs.ShowMessageAsync("Cannot save profile", "The profile could not be saved to the configuration file.");
            return;
        }

        // One operation: each display is written once with the profile that is active now.
        using (colors.Batch())
        {
            editor.Commit();
            hotkeys.Refresh(showAggregateMessage: true);
            preview.Accept();
            activeProfile.Reevaluate();
            editor.Begin(source, display.MonitorId);
        }
    }

    // While a discard confirmation is open, another request, such as a second close from the
    // tray, keeps the changes and leaves the decision to the open one (deliberate deviation:
    // the original opened a second confirmation).
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (confirming)
            return false;

        confirming = true;
        try
        {
            return await dialogs.ConfirmDiscardAsync(editor.IsEditingUnsavedProfile);
        }
        finally
        {
            confirming = false;
        }
    }
}
