namespace NvProfileSwitcher.ViewModels;

/// <summary>
/// Immutable container that exposes one ViewModel per main window panel.
/// It has no behavior and does not call or observe the panels it groups.
/// </summary>
internal sealed class MainWindowViewModel(
    ProfilesViewModel profiles,
    ProfileSettingsViewModel profileSettings,
    HotkeysViewModel hotkeys,
    ApplicationSettingsViewModel applicationSettings,
    StatusBarViewModel statusBar)
{
    public ProfilesViewModel Profiles { get; } = profiles;
    public ProfileSettingsViewModel ProfileSettings { get; } = profileSettings;
    public HotkeysViewModel Hotkeys { get; } = hotkeys;
    public ApplicationSettingsViewModel ApplicationSettings { get; } = applicationSettings;
    public StatusBarViewModel StatusBar { get; } = statusBar;
}
