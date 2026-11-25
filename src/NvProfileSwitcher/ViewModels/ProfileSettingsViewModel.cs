using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
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
    // Reads the desktop mode and the modes a display offers; it changes nothing.
    private readonly Func<ConnectedDisplay, DisplayModeOptions?> modeOptions;

    // True while the controls are loaded from the editor state: what the controls write back
    // meanwhile (a selection cleared by new items) is not a user change.
    private bool loading;
    private bool bouncing;
    private string name = string.Empty;
    private bool isApplicationProfile;
    private int selectedExecutableIndex;
    private ExecutableComboItem? selectedExecutableItem;
    private ResolutionComboItem? selectedResolutionItem;
    private RefreshRateComboItem? selectedRefreshRateItem;
    private bool resolutionListOpen;
    private bool refreshRateListOpen;

    // The modes of the display they were read for (null when the read failed): read once per
    // display, and again after a display refresh, which replaces the display instances.
    private DisplayModeOptions? options;
    private ConnectedDisplay? optionsDisplay;

    public ProfileSettingsViewModel(
        ProfileEditor editor,
        ProfileEditingService editing,
        HotkeyService hotkeys,
        HotkeyCaptureController capture,
        IDialogService dialogs,
        Func<ConnectedDisplay, DisplayModeOptions?> modeOptions)
    {
        this.editor = editor;
        this.editing = editing;
        this.capture = capture;
        this.dialogs = dialogs;
        this.modeOptions = modeOptions;
        ProfileHotkey = new HotkeyFieldViewModel(HotkeyField.Profile, capture, hotkeys);
        editor.EditingStarted += () => Load(LoadDraftFields);
        editor.EditingStarted += LoadModes;
        editor.ValuesLoaded += () => Load(NotifyValues);
        editor.ValuesLoaded += LoadModes;
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

    public IReadOnlyList<ConnectedDisplay> Displays => editor.Displays;

    /// <summary>
    /// Whether the selected display's colors can be adjusted: the color controls, Copy settings and
    /// Reset are disabled otherwise, and nothing is shown as applied.
    /// </summary>
    public bool AreColorsAvailable => editor.SelectedDisplay?.CanAdjustColors == true;

    public string ColorsUnavailableToolTip =>
        "Brightness, contrast, gamma, digital vibrance and hue need an NVIDIA GPU driving this display, " +
        "with the NVIDIA driver available. The saved values are kept; the resolution and refresh rate can still be set.";

    public ConnectedDisplay? SelectedDisplay
    {
        get => editor.SelectedDisplay;
        set
        {
            if (loading || Equals(value, editor.SelectedDisplay)) return;
            editor.SelectDisplay(value);
        }
    }

    /// <summary>
    /// The Resolution selector of the selected display: "Don't change" and every resolution the
    /// display offers, with the saved one marked when it does not offer it.
    /// </summary>
    public ObservableCollection<ResolutionComboItem> ResolutionItems { get; } = [];

    public ResolutionComboItem? SelectedResolutionItem
    {
        get => selectedResolutionItem;
        set
        {
            if (loading || value is null || ReferenceEquals(value, selectedResolutionItem)) return;
            selectedResolutionItem = value;
            editor.SetResolution(value.Resolution);
            // The rates follow the resolution; the chosen rate stays, marked when it is not offered.
            ShowRefreshRateItems();
        }
    }

    /// <summary>
    /// The Refresh rate selector of the selected display, independent of the resolution: "Desktop
    /// rate" and "Highest available" with the rate each applies, then every rate the selected
    /// resolution (or the desktop one) offers, with the saved rate marked when it does not offer it.
    /// </summary>
    public ObservableCollection<RefreshRateComboItem> RefreshRateItems { get; } = [];

    public RefreshRateComboItem? SelectedRefreshRateItem
    {
        get => selectedRefreshRateItem;
        set
        {
            if (loading || value is null || ReferenceEquals(value, selectedRefreshRateItem)) return;
            selectedRefreshRateItem = value;
            editor.SetRefreshRate(value.Rate);
        }
    }

    /// <summary>Whether the Resolution list is open; it shows the modes already read, reading them only if they were not.</summary>
    public bool IsResolutionListOpen
    {
        get => resolutionListOpen;
        set
        {
            if (resolutionListOpen == value) return;
            resolutionListOpen = value;
            if (value)
                LoadOptions();
            OnPropertyChanged();
        }
    }

    /// <summary>Whether the Refresh rate list is open; it shows the modes already read, reading them only if they were not.</summary>
    public bool IsRefreshRateListOpen
    {
        get => refreshRateListOpen;
        set
        {
            if (refreshRateListOpen == value) return;
            refreshRateListOpen = value;
            if (value)
                LoadOptions();
            OnPropertyChanged();
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
        {
            Load(() => OnPropertyChanged(e.PropertyName));
            OnPropertyChanged(nameof(AreColorsAvailable));
        }
        if (e.PropertyName == nameof(ProfileEditor.SelectedDisplay))
            LoadModes();
    }

    // The selected display's resolution and rate. For an application profile the display's modes
    // are read here, so "Desktop rate" and "Highest available" show their rate before a list is
    // opened; another profile on the same display, the lists and a change of resolution reuse them.
    // They are read again for another display or after a display refresh; a failed read leaves the
    // defaults without their rate until then. The Windows profile has no mode selectors and reads
    // nothing.
    private void LoadModes()
    {
        if (!ReferenceEquals(optionsDisplay, editor.SelectedDisplay))
        {
            optionsDisplay = null;
            options = null;
        }
        ShowResolutionItems();
        ShowRefreshRateItems();
        if (isApplicationProfile || resolutionListOpen || refreshRateListOpen)
            LoadOptions();
    }

    private void LoadOptions()
    {
        if (editor.SelectedDisplay is not { } display || ReferenceEquals(optionsDisplay, display)) return;
        optionsDisplay = display;
        options = modeOptions(display);
        ShowResolutionItems();
        ShowRefreshRateItems();
    }

    private void ShowResolutionItems() => Load(() =>
    {
        var saved = editor.Resolution;
        var offered = options is { } modes ? DisplayModeCore.OfferedResolutions(modes.Supported) : [];
        ResolutionItems.Clear();
        ResolutionItems.Add(ResolutionComboItem.DontChange());
        foreach (var resolution in offered)
            ResolutionItems.Add(ResolutionComboItem.Offered(resolution));
        if (saved is not null && !offered.Contains(saved))
            ResolutionItems.Add(ResolutionComboItem.Saved(saved, supported: options is null));
        selectedResolutionItem = ResolutionItems.First(item => item.Resolution == saved);
        OnPropertyChanged(nameof(SelectedResolutionItem));
    });

    // Each rate shows what it means at the resolution chosen, or the desktop one: the default and
    // "Highest available" with the rate they would apply, and a saved rate that resolution does not
    // offer marked as such, never replaced by another one.
    private void ShowRefreshRateItems() => Load(() =>
    {
        var saved = editor.RefreshRate;
        RefreshRateComboItem[] defaults = [RefreshRateComboItem.Desktop(null), RefreshRateComboItem.Highest(null)];
        var rates = new List<int>();
        if (options is { } modes)
        {
            var resolution = editor.Resolution ?? new DisplayResolution(modes.Desktop.Width, modes.Desktop.Height);
            rates.AddRange(DisplayModeCore.OfferedRefreshRates(modes.Supported, resolution));
            defaults =
            [
                RefreshRateComboItem.Desktop(DisplayModeCore.ChooseMode(modes.Supported, resolution, modes.Desktop)?.RefreshRate),
                RefreshRateComboItem.Highest(DisplayModeCore.ChooseMode(modes.Supported, resolution, modes.Desktop, RefreshRateSetting.Highest)?.RefreshRate),
            ];
        }

        RefreshRateItems.Clear();
        foreach (var item in defaults)
            RefreshRateItems.Add(item);
        var fixedRates = rates.Select(RefreshRateComboItem.Offered).ToList();
        if (saved is { IsHighest: false } && !rates.Contains(saved.Hertz))
            fixedRates.Add(RefreshRateComboItem.Saved(saved, supported: options is null));
        // The first fixed rate carries the group's caption: the list holds only rates.
        for (var i = 0; i < fixedRates.Count; i++)
            RefreshRateItems.Add(i == 0 ? fixedRates[i].AsFirstFixed() : fixedRates[i]);
        selectedRefreshRateItem = RefreshRateItems.First(item => item.Rate == saved);
        OnPropertyChanged(nameof(SelectedRefreshRateItem));
    });

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
