using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Nvidia;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Services.Editing;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.ViewModels;

/// <summary>
/// Profile Settings panel: editing of the selected profile.
/// Depends only on services and shared state, never on another panel ViewModel; it owns
/// the field ViewModel of the profile hotkey.
/// </summary>
/// <remarks>
/// The editor state lives in <see cref="ProfileEditor"/>; this ViewModel loads its controls
/// from it without side effects and sends back only the changes the user makes, as the
/// original suppressed its control handlers while loading. The actions with effects beyond the
/// editor's state run in <see cref="ProfileEditingService"/>; the executable pickers only feed
/// the editor. Messages the original only wrote to its hidden status text are not shown.
/// </remarks>
internal sealed partial class ProfileSettingsViewModel : ObservableObject
{
    private readonly ProfileEditor editor;
    private readonly ProfileEditingService editing;
    private readonly HotkeyCaptureController capture;
    private readonly IDialogService dialogs;

    // True while the controls are loaded from the editor state: what the controls write back
    // meanwhile (a selection cleared by new items) is not a user change.
    private bool loading;
    private bool bouncing;
    private string name = string.Empty;
    private bool isApplicationProfile;
    private int selectedExecutableIndex;
    private ExecutableComboItem? selectedExecutableItem;

    public ProfileSettingsViewModel(
        ProfileEditor editor,
        ProfileEditingService editing,
        HotkeyService hotkeys,
        HotkeyCaptureController capture,
        IDialogService dialogs)
    {
        this.editor = editor;
        this.editing = editing;
        this.capture = capture;
        this.dialogs = dialogs;
        ProfileHotkey = new HotkeyFieldViewModel(HotkeyField.Profile, capture, hotkeys);
        editor.EditingStarted += () => Load(LoadDraftFields);
        editor.ValuesLoaded += () => Load(NotifyValues);
        editor.DraftChanged += () => OnPropertyChanged(nameof(CanCancel));
        editor.PropertyChanged += OnEditorPropertyChanged;
        capture.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HotkeyCaptureController.CaptureTarget))
                OnPropertyChanged(nameof(CanCancel));
        };
    }

    /// <summary>The name, executable and hotkey fields are shown only for application profiles.</summary>
    public bool IsApplicationProfile => isApplicationProfile;

    public string Name
    {
        get => name;
        set
        {
            if (loading || name == value) return;
            name = value;
            OnPropertyChanged();
            editor.SetName(value);
        }
    }

    public ObservableCollection<ExecutableComboItem> ExecutableItems { get; } = [];

    /// <summary>
    /// The executable selection. Choosing an action item keeps the current executable selected
    /// and runs the action, which adds the chosen executable.
    /// </summary>
    public ExecutableComboItem? SelectedExecutableItem
    {
        get => loading && bouncing ? null : selectedExecutableItem;
        set
        {
            if (loading || value is null || ReferenceEquals(value, selectedExecutableItem)) return;

            if (value.Action == ExecutableComboAction.None)
            {
                selectedExecutableItem = value;
                selectedExecutableIndex = value.ExecutableIndex;
                return;
            }

            // The selector cannot take the previous item back within its own change, and its
            // binding ignores a notification of the value it read back, so the previous item
            // is notified after an empty one.
            Dispatcher.UIThread.Post(() => Load(() =>
            {
                bouncing = true;
                OnPropertyChanged(nameof(SelectedExecutableItem));
                bouncing = false;
                OnPropertyChanged(nameof(SelectedExecutableItem));
            }));
            _ = RunExecutableActionAsync(value.Action);
        }
    }

    public HotkeyFieldViewModel ProfileHotkey { get; }

    public IReadOnlyList<NvidiaDisplay> Displays => editor.Displays;

    public NvidiaDisplay? SelectedDisplay
    {
        get => editor.SelectedDisplay;
        set
        {
            if (loading || Equals(value, editor.SelectedDisplay)) return;
            editor.SelectDisplay(value);
        }
    }

    public double Brightness
    {
        get => editor.Values.Brightness;
        set => SetValues(editor.Values with { Brightness = value });
    }

    public double Contrast
    {
        get => editor.Values.Contrast;
        set => SetValues(editor.Values with { Contrast = value });
    }

    public double Gamma
    {
        get => editor.Values.Gamma;
        set => SetValues(editor.Values with { Gamma = value });
    }

    public double Vibrance
    {
        get => editor.Values.Vibrance;
        set => SetValues(editor.Values with { Vibrance = value });
    }

    public double Hue
    {
        get => editor.Values.Hue;
        set => SetValues(editor.Values with { Hue = value });
    }

    // The original badge formats: brightness and contrast in the current culture, gamma
    // with two invariant decimals, vibrance and hue with their unit.
    public string BrightnessText => Math.Round(Brightness).ToString();

    public string ContrastText => Math.Round(Contrast).ToString();

    public string GammaText => Gamma.ToString("0.00", CultureInfo.InvariantCulture);

    public string VibranceText => $"{Math.Round(Vibrance)}%";

    public string HueText => $"{Math.Round(Hue)}°";

    /// <summary>Cancel is shown while there are unsaved changes or the profile hotkey is being captured.</summary>
    public bool CanCancel => editor.IsDirty || capture.CaptureTarget == HotkeyField.Profile;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RunningAppsAsync()
    {
        if (editor.SelectedProfile is not { IsWindowsProfile: false }) return;
        var path = await dialogs.PickRunningAppAsync();
        if (!string.IsNullOrWhiteSpace(path))
            ReplaceSelectedExecutable(path);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task BrowseAsync()
    {
        if (editor.SelectedProfile is not { IsWindowsProfile: false }) return;
        var path = await dialogs.PickExecutableAsync();
        if (!string.IsNullOrWhiteSpace(path))
            ReplaceSelectedExecutable(path);
    }

    [RelayCommand]
    private void RemoveExecutable(ExecutableComboItem? item)
    {
        if (item is not { CanRemove: true } || editor.Session?.Draft is not { } draft ||
            !editor.RemoveExecutable(item.ExecutableIndex)) return;

        if (selectedExecutableIndex >= draft.ExecutablePaths.Count) selectedExecutableIndex = draft.ExecutablePaths.Count - 1;
        else if (item.ExecutableIndex < selectedExecutableIndex) selectedExecutableIndex--;
        RefreshExecutableItems(selectedExecutableIndex);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task CopySettingsAsync() => editing.CopySettingsAsync();

    /// <summary>Shows the NVIDIA defaults for the visible display and previews them; nothing is saved.</summary>
    [RelayCommand]
    private void Reset() => editing.Reset();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task CancelAsync() => editing.CancelAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SaveAsync() => editing.SaveAsync();

    private async Task RunExecutableActionAsync(ExecutableComboAction action)
    {
        var path = action == ExecutableComboAction.AddRunningApp
            ? await dialogs.PickRunningAppAsync()
            : await dialogs.PickExecutableAsync();
        if (!string.IsNullOrWhiteSpace(path))
            AddExecutable(path);
    }

    // Running apps and Browse replace the selected executable; a duplicate is ignored.
    private void ReplaceSelectedExecutable(string path)
    {
        if (!editor.ReplaceExecutable(selectedExecutableIndex, path)) return;
        RefreshExecutableItems(selectedExecutableIndex);
    }

    // The selector's actions add an executable; a duplicate is ignored.
    private void AddExecutable(string path)
    {
        if (!editor.AddExecutable(path) || editor.Session?.Draft is not { } draft) return;
        RefreshExecutableItems(draft.ExecutablePaths.Count - 1);
    }

    private void SetValues(ColorValues values)
    {
        if (loading || values == editor.Values) return;
        editing.SetValues(values);
        NotifyValues();
    }

    private void LoadDraftFields()
    {
        if (editor.Session?.Draft is not { } draft) return;
        name = draft.Name;
        OnPropertyChanged(nameof(Name));
        selectedExecutableIndex = Math.Clamp(selectedExecutableIndex, 0, Math.Max(0, draft.ExecutablePaths.Count - 1));
        RefreshExecutableItems();
        isApplicationProfile = !draft.IsWindowsProfile;
        OnPropertyChanged(nameof(IsApplicationProfile));
    }

    // One item per executable (removable when there is more than one), then the two actions.
    private void RefreshExecutableItems(int? selectIndex = null)
    {
        if (editor.Session?.Draft is not { IsWindowsProfile: false } draft) return;
        if (selectIndex.HasValue) selectedExecutableIndex = selectIndex.Value;
        selectedExecutableIndex = Math.Clamp(selectedExecutableIndex, 0, Math.Max(0, draft.ExecutablePaths.Count - 1));

        Load(() =>
        {
            ExecutableItems.Clear();
            for (var i = 0; i < draft.ExecutablePaths.Count; i++)
                ExecutableItems.Add(ExecutableComboItem.Executable(draft.ExecutablePaths[i], i, draft.ExecutablePaths.Count > 1));
            ExecutableItems.Add(ExecutableComboItem.CreateAction("+ Running apps...", ExecutableComboAction.AddRunningApp));
            ExecutableItems.Add(ExecutableComboItem.CreateAction("+ Browse...", ExecutableComboAction.AddBrowse));
            selectedExecutableItem = draft.ExecutablePaths.Count > 0 ? ExecutableItems[selectedExecutableIndex] : null;
            OnPropertyChanged(nameof(SelectedExecutableItem));
        });
    }

    private void NotifyValues()
    {
        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(Contrast));
        OnPropertyChanged(nameof(Gamma));
        OnPropertyChanged(nameof(Vibrance));
        OnPropertyChanged(nameof(Hue));
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(ContrastText));
        OnPropertyChanged(nameof(GammaText));
        OnPropertyChanged(nameof(VibranceText));
        OnPropertyChanged(nameof(HueText));
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfileEditor.Displays) or nameof(ProfileEditor.SelectedDisplay))
            Load(() => OnPropertyChanged(e.PropertyName));
    }

    private void Load(Action load)
    {
        var wasLoading = loading;
        loading = true;
        try
        {
            load();
        }
        finally
        {
            loading = wasLoading;
        }
    }
}
