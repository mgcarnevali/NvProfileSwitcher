using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.ViewModels;

/// <summary>
/// Application Settings panel: application preferences.
/// Depends only on services and shared state, never on another ViewModel.
/// </summary>
/// <remarks>
/// Only a change made through this ViewModel runs the original checkbox actions; loading
/// the configuration changes <see cref="AppSettings"/> directly and never saves. A failed save
/// is reported by the configuration service and skips the steps the original skipped; registry
/// failures are ignored, as the original only reported them in its hidden status text.
/// The Advanced Settings dialog runs its actions through the services passed to it.
/// </remarks>
internal sealed partial class ApplicationSettingsViewModel : ObservableObject
{
    private const string UpdatesDisabledToolTip = "Update checks are disabled in development builds.";

    private readonly AppSettings settings;
    private readonly ConfigurationService configuration;
    private readonly StartupRegistrationService startupRegistration;
    private readonly TrayService tray;
    private readonly MainWindowHost host;
    private readonly IDialogService dialogs;
    private readonly UpdateCheckService updateCheck;
    private readonly SavedDisplaysService savedDisplays;
    private readonly ConfigurationBackupService backup;
    private readonly bool isDevelopmentBuild;

    public ApplicationSettingsViewModel(
        AppSettings settings,
        ConfigurationService configuration,
        StartupRegistrationService startupRegistration,
        TrayService tray,
        MainWindowHost host,
        IDialogService dialogs,
        UpdateCheckService updateCheck,
        SavedDisplaysService savedDisplays,
        ConfigurationBackupService backup,
        bool isDevelopmentBuild)
    {
        this.settings = settings;
        this.configuration = configuration;
        this.startupRegistration = startupRegistration;
        this.tray = tray;
        this.host = host;
        this.dialogs = dialogs;
        this.updateCheck = updateCheck;
        this.savedDisplays = savedDisplays;
        this.backup = backup;
        this.isDevelopmentBuild = isDevelopmentBuild;
        settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    public bool StartWithWindows
    {
        get => settings.StartWithWindows;
        set
        {
            if (settings.StartWithWindows == value) return;
            settings.StartWithWindows = value;
            SaveAndApplyStartupRegistration();
        }
    }

    public bool StartMinimized
    {
        get => settings.StartMinimized;
        set
        {
            if (settings.StartMinimized == value) return;
            settings.StartMinimized = value;
            SaveAndApplyStartupRegistration();
        }
    }

    public bool MinimizeToTray
    {
        get => settings.MinimizeToTray;
        set
        {
            if (settings.MinimizeToTray == value) return;
            settings.MinimizeToTray = value;
            if (configuration.Save() && !value && host.IsVisible)
                tray.SetIconVisible(false);
        }
    }

    public bool AlwaysOnTop
    {
        get => settings.AlwaysOnTop;
        set
        {
            if (settings.AlwaysOnTop == value) return;
            settings.AlwaysOnTop = value;
            host.SetTopmost(value);
            configuration.Save();
        }
    }

    /// <summary>Saved at once; development builds keep it off and disabled.</summary>
    public bool CheckForUpdates
    {
        get => settings.CheckForUpdates;
        set
        {
            if (settings.CheckForUpdates == value) return;
            settings.CheckForUpdates = value;
            configuration.Save();
        }
    }

    public bool IsCheckForUpdatesEnabled => !isDevelopmentBuild;

    public string? CheckForUpdatesToolTip => isDevelopmentBuild ? UpdatesDisabledToolTip : null;

    /// <summary>
    /// Opens Advanced Settings; with Save, the Remember settings are applied and saved. A failed
    /// save keeps them applied and is reported (deliberate deviation: the original closed the
    /// application).
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task AdvancedAsync()
    {
        var result = await dialogs.ShowAdvancedSettingsAsync(new AdvancedSettingsOptions(
            RememberState(),
            RememberState,
            () => updateCheck.CheckAsync(manual: true),
            () => dialogs.ShowManageDisplaysAsync(savedDisplays.GetSavedDisplays(), savedDisplays.Remove),
            backup.ImportAsync,
            backup.ExportAsync));
        if (result is not { } chosen)
            return;

        settings.RememberGlobalSwitching = chosen.RememberGlobalSwitching;
        settings.RememberOverride = chosen.RememberOverride;
        configuration.Save();
    }

    private AdvancedSettingsResult RememberState() =>
        new(settings.RememberGlobalSwitching, settings.RememberOverride);

    private void SaveAndApplyStartupRegistration()
    {
        if (configuration.Save())
            startupRegistration.Apply();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.StartWithWindows) or nameof(AppSettings.StartMinimized)
            or nameof(AppSettings.MinimizeToTray) or nameof(AppSettings.AlwaysOnTop) or nameof(AppSettings.CheckForUpdates))
            OnPropertyChanged(e.PropertyName);
    }
}
