using CommunityToolkit.Mvvm.ComponentModel;

namespace NvProfileSwitcher.Models;

/// <summary>
/// Application preferences and global hotkeys loaded from and saved to the configuration.
/// </summary>
internal sealed partial class AppSettings : ObservableObject
{
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial bool StartMinimized { get; set; }
    [ObservableProperty] public partial bool MinimizeToTray { get; set; }
    [ObservableProperty] public partial bool AlwaysOnTop { get; set; }
    [ObservableProperty] public partial bool CheckForUpdates { get; set; }
    [ObservableProperty] public partial bool RememberGlobalSwitching { get; set; }
    [ObservableProperty] public partial bool RememberOverride { get; set; }
    [ObservableProperty] public partial int ShowHideHotkey { get; set; }
    [ObservableProperty] public partial int WindowsOverrideHotkey { get; set; }
    [ObservableProperty] public partial int ResumeAutomaticHotkey { get; set; }
    [ObservableProperty] public partial int GlobalSwitchingHotkey { get; set; }
}
