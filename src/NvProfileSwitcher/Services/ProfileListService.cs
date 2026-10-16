using NvProfileSwitcher.Core;
using NvProfileSwitcher.Services.Editing;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Services;

/// <summary>
/// The profile list actions: add, remove, move and enable or disable a profile, each with the
/// original sequence. The store keeps the list consistent; these actions add what follows a
/// change: the editor's selection, the hotkeys registered again, the save and the reevaluation of
/// the active profile. A failed save is reported by the configuration service (deliberate
/// deviation: the original only reported it in its hidden status text, and Move closed the
/// application).
/// </summary>
internal sealed class ProfileListService(
    ProfileStore store,
    ProfileEditor editor,
    ProfileEditingService editing,
    LivePreviewController preview,
    HotkeyService hotkeys,
    ConfigurationService configuration,
    ActiveProfileService activeProfile,
    NvidiaService nvidia,
    DisplayColorController colors,
    IDialogService dialogs)
{
    /// <summary>
    /// Adds a new profile with the Windows values for every connected display, unsaved until
    /// Save profile, once unsaved changes were discarded.
    /// </summary>
    public async Task AddAsync()
    {
        if (!await editing.ConfirmAndDiscardAsync())
            return;

        preview.Suspend();
        editor.AddUnsaved(ProfileDefaultsCore.NewProfile(store.Windows, nvidia.Displays));
    }

    /// <summary>Removes the selected application profile once confirmed and selects the profile at its place.</summary>
    public async Task RemoveSelectedAsync()
    {
        if (editor.SelectedProfile is not { IsWindowsProfile: false } profile)
            return;

        if (!await dialogs.ConfirmRemoveProfileAsync(profile.Name))
            return;

        var removedIndex = store.Profiles.IndexOf(profile);
        if (removedIndex < 1)
            return;

        var wasUnsaved = ReferenceEquals(profile, store.UnsavedNewProfile);

        // One operation: each display is written once with the profile that is active then.
        using var batch = colors.Batch();
        preview.Suspend();
        editor.StopEditing(profile);

        store.Remove(profile);
        editor.SetSelection(store.Profiles.Count == 1
            ? store.Profiles[0]
            : store.Profiles[Math.Min(removedIndex, store.Profiles.Count - 1)]);
        hotkeys.Refresh(showAggregateMessage: true);

        if (editor.SelectedProfile is { } selected)
            editor.Begin(selected);

        // As in the original, a failed save skips the reevaluation.
        if (!wasUnsaved && !configuration.Save())
            return;
        activeProfile.Reevaluate();
    }

    /// <summary>
    /// Moves an application profile one position up (-1) or down (1) and selects it, as the
    /// original handled it as a new selection: moving another profile with unsaved changes asks
    /// to discard them. Moving the edited profile keeps its draft and resumes its preview
    /// (deliberate deviation: the original started the draft over, losing its unsaved changes,
    /// because the list drops a moved selected item).
    /// </summary>
    public async Task MoveAsync(ProfileListItem? profile, int direction)
    {
        if (profile is not { IsWindowsProfile: false })
            return;

        var listIndex = store.Profiles.IndexOf(profile);
        if (listIndex <= 0) return;

        var from = listIndex - 1;
        var to = from + direction;
        if (to < 0 || to >= store.Profiles.Count - 1) return;

        var movedEditedProfile = ReferenceEquals(editor.Session?.Source, profile);
        var selection = Task.CompletedTask;

        // One operation: the preview of a moved edited profile resumes without its display
        // showing the real colors in between.
        using (colors.Batch())
        {
            preview.Discard();
            store.Move(listIndex, to + 1);
            if (movedEditedProfile)
                editor.SetSelection(profile);
            else
                selection = editing.RequestSelectionAsync(profile);
            // As in the original, moving registers the hotkeys again, which reports any that cannot be enabled.
            hotkeys.Refresh(showAggregateMessage: true);

            configuration.Save();
            if (movedEditedProfile)
                preview.Resume();
        }

        await selection;
    }

    /// <summary>Enables or disables an application profile; disabled profiles have no registered hotkey.</summary>
    public void ToggleEnabled(ProfileListItem? profile)
    {
        if (profile is not { IsWindowsProfile: false })
            return;

        profile.Enabled = !profile.Enabled;
        hotkeys.Refresh(showAggregateMessage: true);

        // As in the original, a failed save skips the reevaluation.
        if (!ReferenceEquals(profile, store.UnsavedNewProfile) && !configuration.Save())
            return;
        activeProfile.Reevaluate();
    }
}
