using CommunityToolkit.Mvvm.ComponentModel;

namespace NvProfileSwitcher.Models;

/// <summary>
/// Automatic switching state: the active override, whether global switching is paused,
/// the name of the profile currently applied and the active profile shown in the status bar.
/// </summary>
internal sealed partial class SwitchingState : ObservableObject
{
    [ObservableProperty] public partial OverrideMode OverrideMode { get; set; } = OverrideMode.Automatic;

    /// <summary>Index of the override profile among the application profiles, or -1.</summary>
    [ObservableProperty] public partial int OverrideProfileIndex { get; set; } = -1;

    [ObservableProperty] public partial bool GlobalPaused { get; set; }
    [ObservableProperty] public partial string ActiveProfileName { get; set; } = "Windows";
    [ObservableProperty] public partial ActiveProfileDisplay DisplayedActiveProfile { get; set; } = ActiveProfileDisplay.Initial;
}
