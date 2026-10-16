using System.ComponentModel;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Nvidia;

namespace NvProfileSwitcher.Services.Editing;

/// <summary>
/// The state of the profile being edited: the list selection, the editing session and its
/// draft, the display chosen in the editor and the color values shown by its sliders, with the
/// transitions between them. Every change of the draft is announced by the editor itself. It
/// reproduces the original selection and editor sequences, including the display selector
/// reacting only when its selection changes. It shows no dialogs and has no effects outside its
/// state and the profile store: those belong to <see cref="ProfileEditingService"/>. It does not
/// know the live preview: it raises <see cref="EditingTargetChanging"/> where the original
/// suspended it and <see cref="DisplayChanged"/> where the original resumed it.
/// </summary>
internal sealed class ProfileEditor(ProfileStore store) : INotifyPropertyChanged
{
    private bool valuesLoaded;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised before the edited profile or display changes, where the original suspended the preview.</summary>
    public event Action? EditingTargetChanging;

    /// <summary>Raised after the display selection changed and its values were loaded, where the original resumed the preview.</summary>
    public event Action? DisplayChanged;

    /// <summary>Raised when a profile starts being edited, so the editor loads its fields from the draft.</summary>
    public event Action? EditingStarted;

    /// <summary>Raised when the slider values were loaded without being a user change.</summary>
    public event Action? ValuesLoaded;

    /// <summary>Raised when the draft or the editing session changed, which may change <see cref="IsDirty"/>.</summary>
    public event Action? DraftChanged;

    /// <summary>The profile selected in the list. Every assignment is notified, as the list is updated again.</summary>
    public ProfileListItem? SelectedProfile { get; private set; }

    public ProfileEditorSession? Session { get; private set; }

    /// <summary>The displays of the editor's display selector, as of the last display refresh.</summary>
    public IReadOnlyList<NvidiaDisplay> Displays { get; private set; } = [];

    public int SelectedDisplayIndex { get; private set; } = -1;

    public NvidiaDisplay? SelectedDisplay =>
        SelectedDisplayIndex >= 0 && SelectedDisplayIndex < Displays.Count ? Displays[SelectedDisplayIndex] : null;

    /// <summary>The values of the five sliders.</summary>
    public ColorValues Values { get; private set; } = ColorValues.Defaults;

    /// <summary>Whether the sliders were ever loaded from a profile or display; the preview needs it to begin.</summary>
    public bool HasLoadedValues => valuesLoaded;

    /// <summary>The hotkey of the profile hotkey field: the draft's, or 0 for the Windows profile.</summary>
    public int DraftHotkey { get; private set; }

    /// <summary>The value the profile hotkey field shows and captures over: 0 unless an application profile is selected.</summary>
    public int ProfileHotkey => SelectedProfile is { IsWindowsProfile: false } ? DraftHotkey : 0;

    /// <summary>The source is the unsaved new profile, or the draft differs from it.</summary>
    public bool IsDirty => Session is { } session &&
        (ReferenceEquals(session.Source, store.UnsavedNewProfile) || session.HasChanges);

    public bool HasUnsavedColorChanges => Session?.HasUnsavedColorChanges == true;

    /// <summary>Whether the edited profile is the unsaved new profile.</summary>
    public bool IsEditingUnsavedProfile => Session is { } session && ReferenceEquals(session.Source, store.UnsavedNewProfile);

    /// <summary>Selects the first profile, the Windows one, and starts editing it, as the original did on startup.</summary>
    public void BeginFirstProfile()
    {
        SetSelection(store.Profiles.Count > 0 ? store.Profiles[0] : null);
        if (SelectedProfile is { } selected)
            Begin(selected);
    }

    /// <summary>Selects a profile in the list without handling it as a new selection.</summary>
    public void SetSelection(ProfileListItem? profile)
    {
        SelectedProfile = profile;
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(ProfileHotkey));
    }

    /// <summary>
    /// Starts editing a clone of <paramref name="profile"/> and loads it into the editor: the
    /// display stays on <paramref name="preferredMonitorId"/>, or else the primary or first
    /// display, and the sliders show the draft's values for it.
    /// </summary>
    public void Begin(ProfileListItem profile, string? preferredMonitorId = null)
    {
        EditingTargetChanging?.Invoke();
        Session = new ProfileEditorSession(profile);
        var draft = Session.Draft;
        DraftHotkey = draft.IsWindowsProfile ? 0 : draft.Hotkey;
        OnPropertyChanged(nameof(ProfileHotkey));
        EditingStarted?.Invoke();
        SelectDisplayIndex(DisplaySelectionIndex(preferredMonitorId));
        LoadValues(draft);
        DraftChanged?.Invoke();
    }

    /// <summary>
    /// Adds <paramref name="profile"/> at the end of the list as the unsaved new profile,
    /// selects it and starts editing it. It is written to the configuration only once saved.
    /// </summary>
    public void AddUnsaved(ProfileListItem profile)
    {
        store.AddUnsaved(profile);
        SetSelection(profile);
        Begin(profile);
    }

    /// <summary>Copies the draft into its profile, which is no longer the unsaved new profile.</summary>
    public void Commit()
    {
        if (Session is not { } session) return;
        ProfileEditingCore.CopyProfile(session.Draft, session.Source);
        store.MarkSaved(session.Source);
    }

    /// <summary>
    /// Stops editing without saving. With <paramref name="removeUnsavedProfile"/>, an unsaved
    /// new profile is removed from the list, the ▲▼ state follows the shorter list (deliberate
    /// deviation: the original left the new last profile able to move down) and the profile
    /// above it is selected.
    /// </summary>
    public void Discard(bool removeUnsavedProfile)
    {
        EditingTargetChanging?.Invoke();
        var source = Session?.Source;
        Session = null;
        if (removeUnsavedProfile && source is not null && ReferenceEquals(source, store.UnsavedNewProfile))
        {
            // The profile may have been moved up, so the profiles below it change position: their
            // override follows them in the store and their hotkeys stay with them (deliberate
            // deviation: the original left both on the old positions). The unsaved profile has no
            // registered hotkey, so nothing is registered again. As the original discard, nothing
            // is saved.
            var index = store.Remove(source);
            if (store.Profiles.Count > 0)
                SetSelection(store.Profiles[Math.Clamp(index - 1, 0, store.Profiles.Count - 1)]);
        }

        DraftChanged?.Invoke();
    }

    /// <summary>Stops editing <paramref name="profile"/> because it is being removed.</summary>
    public void StopEditing(ProfileListItem profile)
    {
        if (!ReferenceEquals(Session?.Source, profile))
            return;

        Session = null;
        DraftChanged?.Invoke();
    }

    /// <summary>The user chose another display in the editor.</summary>
    public void SelectDisplay(NvidiaDisplay? display)
    {
        var index = display is null ? -1 : Displays.ToList().IndexOf(display);
        SelectDisplayIndex(index);
    }

    /// <summary>
    /// Takes the displays of a refresh: the selector keeps the display with the same monitor,
    /// or the first one, and the sliders show the draft's values again. The draft's values
    /// follow the monitor ids whose values the synchronization moved (<paramref name="remaps"/>),
    /// as the selection does.
    /// </summary>
    public void RefreshDisplays(IReadOnlyList<NvidiaDisplay> displays, IReadOnlyList<DisplayRemap>? remaps = null)
    {
        var previousId = SelectedDisplay?.MonitorId;
        foreach (var remap in remaps ?? [])
        {
            if (Session is { } edited)
                DisplayProfileSyncCore.Remap([edited.Draft], remap, displays);
            if (DisplayCore.SameMonitorId(previousId, remap.From))
                previousId = remap.To[0];
        }

        var hadSelection = SelectedDisplayIndex >= 0;
        Displays = displays;
        OnPropertyChanged(nameof(Displays));

        // Replacing the selector's items clears its selection, which the original handled as a change.
        SelectedDisplayIndex = -1;
        OnPropertyChanged(nameof(SelectedDisplay));
        if (hadSelection)
            OnSelectedDisplayChanged();

        var index = !string.IsNullOrWhiteSpace(previousId)
            ? displays.ToList().FindIndex(d => DisplayCore.SameMonitorId(d.MonitorId, previousId))
            : -1;
        SelectDisplayIndex(index >= 0 ? index : displays.Count > 0 ? 0 : -1);
        if (Session is { } session)
            LoadValues(session.Draft);
    }

    /// <summary>The user changed the profile name.</summary>
    public void SetName(string name)
    {
        if (Session?.Draft is { IsWindowsProfile: false } draft)
            draft.Name = name;
        DraftChanged?.Invoke();
    }

    /// <summary>Adds an executable to an application profile's draft; false for a duplicate.</summary>
    public bool AddExecutable(string path) => ChangeExecutables(draft => draft.AddExecutable(path));

    /// <summary>Replaces the executable at <paramref name="index"/>, or adds the first one; false for a duplicate.</summary>
    public bool ReplaceExecutable(int index, string path) => ChangeExecutables(draft =>
        draft.ExecutablePaths.Count == 0
            ? draft.AddExecutable(path)
            : draft.ReplaceExecutableAt(Math.Clamp(index, 0, draft.ExecutablePaths.Count - 1), path));

    /// <summary>Removes the executable at <paramref name="index"/>; false when it is the only one.</summary>
    public bool RemoveExecutable(int index) => ChangeExecutables(draft => draft.RemoveExecutableAt(index));

    /// <summary>The profile hotkey field captured a new value for the draft; it is not saved.</summary>
    public void SetDraftHotkey(int hotkey)
    {
        if (SelectedProfile is not { IsWindowsProfile: false }) return;
        DraftHotkey = hotkey;
        if (Session is { } session)
            session.Draft.Hotkey = hotkey;
        OnPropertyChanged(nameof(ProfileHotkey));
        DraftChanged?.Invoke();
    }

    /// <summary>The user moved a slider: the values go to the draft for the selected display.</summary>
    public void SetCurrentValues(ColorValues values)
    {
        Values = values;
        CompleteDraft();
        DraftChanged?.Invoke();
    }

    /// <summary>
    /// Copies the slider values into the draft's values for the selected display, adding them
    /// if missing, as the original did before saving or copying.
    /// </summary>
    public void CompleteDraft()
    {
        if (Session?.Draft is not { } draft || SelectedDisplay is not { } display) return;
        var values = DisplayCore.FindValues(draft, display.MonitorId);
        if (values is null)
        {
            values = new ProfileDisplaySettings { DisplayName = display.Name, MonitorId = display.MonitorId };
            draft.DisplayProfiles.Add(values);
        }

        values.DisplayName = display.Name;
        values.MonitorId = display.MonitorId;
        values.Brightness = Values.Brightness;
        values.Contrast = Values.Contrast;
        values.Gamma = Values.Gamma;
        values.DigitalVibrance = Values.RoundedVibrance;
        values.Hue = Values.RoundedHue;
    }

    /// <summary>
    /// Copies the color values of <paramref name="source"/> into the draft for every display both
    /// have, and shows them; false when no display matched and nothing changed.
    /// </summary>
    public bool CopyColorsFrom(ProfileListItem source)
    {
        if (Session?.Draft is not { } draft || ProfileEditingCore.CopyDisplaySettings(source, draft) == 0)
            return false;

        LoadValues(draft);
        DraftChanged?.Invoke();
        return true;
    }

    // Shows the values of the selected display: the draft's, or else the saved ones of its
    // profile, or else the Windows profile's, or else the NVIDIA defaults. They never come from
    // another display or from the colors the display shows (deliberate deviation: the original
    // showed the profile's first values or read the display, which may show a preview or another
    // profile, and saving then kept them). Nothing is loaded without a selected display.
    private void LoadValues(ProfileListItem draft)
    {
        if (SelectedDisplay is not { } display)
            return;

        var settings = DisplayCore.FindValues(draft, display.MonitorId)
            ?? (Session is { } session ? DisplayCore.FindValues(session.Source, display.MonitorId) : null)
            ?? (store.Windows is { } windows ? DisplayCore.FindValues(windows, display.MonitorId) : null);
        SetValuesWithoutPreview(settings is null
            ? ColorValues.Defaults
            : new ColorValues(settings.Brightness, settings.Contrast, settings.Gamma, settings.DigitalVibrance, settings.Hue));
    }

    /// <summary>Shows the NVIDIA defaults for the selected display; the draft takes them, nothing is saved.</summary>
    public void ResetValues()
    {
        var loaded = valuesLoaded;
        SetValuesWithoutPreview(ColorValues.Defaults);
        valuesLoaded = loaded;
        CompleteDraft();
        DraftChanged?.Invoke();
    }

    /// <summary>The previewed values became the saved ones.</summary>
    public void AcceptValues()
    {
        if (SelectedDisplay is not null)
            valuesLoaded = true;
    }

    private bool ChangeExecutables(Func<ProfileListItem, bool> change)
    {
        if (Session?.Draft is not { IsWindowsProfile: false } draft || !change(draft))
            return false;

        DraftChanged?.Invoke();
        return true;
    }

    private void SelectDisplayIndex(int index)
    {
        if (index == SelectedDisplayIndex)
            return;

        SelectedDisplayIndex = index;
        OnPropertyChanged(nameof(SelectedDisplay));
        OnSelectedDisplayChanged();
    }

    // The original display selector handler.
    private void OnSelectedDisplayChanged()
    {
        EditingTargetChanging?.Invoke();
        if (Session?.Draft is { } draft)
            LoadValues(draft);

        DisplayChanged?.Invoke();
    }

    private int DisplaySelectionIndex(string? preferredMonitorId)
    {
        if (Displays.Count == 0)
            return -1;

        var selectedIndex = -1;
        var primaryIndex = -1;
        for (var i = 0; i < Displays.Count; i++)
        {
            if (Displays[i].Primary)
                primaryIndex = i;

            if (!string.IsNullOrWhiteSpace(preferredMonitorId) &&
                DisplayCore.SameMonitorId(Displays[i].MonitorId, preferredMonitorId))
                selectedIndex = i;
        }

        return selectedIndex >= 0 ? selectedIndex : primaryIndex >= 0 ? primaryIndex : 0;
    }

    private void SetValuesWithoutPreview(ColorValues values)
    {
        Values = values.ClampedToSliders();
        valuesLoaded = true;
        ValuesLoaded?.Invoke();
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
