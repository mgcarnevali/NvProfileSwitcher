using NvProfileSwitcher.Models;

namespace NvProfileSwitcher.Core;

/// <summary>
/// Maps the persisted configuration to the application state and back, exactly as the
/// original configuration load and save did.
/// </summary>
internal static class ConfigMappingCore
{
    /// <summary>The profile list used when no configuration could be loaded.</summary>
    internal static List<ProfileListItem> DefaultProfiles() =>
        [new ProfileListItem("Windows", isWindowsProfile: true)];

    /// <summary>
    /// Builds the profile list: the Windows profile first, then the application profiles
    /// in configuration order. Throws for configurations the original load also rejected
    /// (for example, a missing profile or executable list).
    /// </summary>
    internal static List<ProfileListItem> CreateProfiles(AppConfig config)
    {
        var profiles = new List<ProfileListItem>();

        var windows = new ProfileListItem("Windows", isWindowsProfile: true);
        foreach (var display in Identified(config.WindowsProfiles))
            windows.DisplayProfiles.Add(display);
        profiles.Add(windows);

        foreach (var source in config.ApplicationProfiles)
        {
            var profile = new ProfileListItem(source.Name, id: source.Id)
            {
                Enabled = source.Enabled,
                Hotkey = source.Hotkey
            };
            profile.SetExecutables(source.ExePaths);
            foreach (var display in Identified(source.DisplayProfiles))
                profile.DisplayProfiles.Add(display);
            profiles.Add(profile);
        }

        return profiles;
    }

    // Values without a monitor id belong to no display and are dropped, so the next save no
    // longer writes them (deliberate deviation: the original kept and wrote them).
    private static IEnumerable<ProfileDisplaySettings> Identified(IEnumerable<ProfileDisplaySettings> values) =>
        values.Where(value => value is null || !string.IsNullOrWhiteSpace(value.MonitorId));

    /// <summary>
    /// Applies the settings and the switching state of a loaded configuration, or the
    /// defaults of a session without configuration when <paramref name="config"/> is null.
    /// Development builds never check for updates.
    /// </summary>
    internal static void ApplyState(
        AppConfig? config,
        IReadOnlyList<ProfileListItem> applicationProfiles,
        bool isDevelopmentBuild,
        AppSettings settings,
        SwitchingState switching)
    {
        settings.StartWithWindows = config?.StartWithWindows ?? false;
        settings.StartMinimized = config?.StartMinimized ?? false;
        settings.MinimizeToTray = config?.MinimizeToTray ?? false;
        settings.AlwaysOnTop = config?.AlwaysOnTop ?? false;
        settings.CheckForUpdates = !isDevelopmentBuild && (config?.CheckForUpdates ?? false);
        settings.RememberGlobalSwitching = config?.RememberGlobalSwitching ?? false;
        settings.RememberOverride = config?.RememberOverride ?? false;
        settings.ShowHideHotkey = config?.ShowHideHotkey ?? 0;
        settings.WindowsOverrideHotkey = config?.WindowsOverrideHotkey ?? 0;
        settings.ResumeAutomaticHotkey = config?.ResumeAutomaticHotkey ?? 0;
        settings.GlobalSwitchingHotkey = config?.GlobalSwitchingHotkey ?? 0;

        switching.GlobalPaused = config is not null && settings.RememberGlobalSwitching && !config.GlobalSwitchingEnabled;
        var (mode, index) = config is null
            ? (OverrideMode.Automatic, -1)
            : OverrideCore.Restore(config, settings.RememberOverride, applicationProfiles);
        switching.OverrideMode = mode;
        switching.OverrideProfileIndex = index;
    }

    /// <summary>
    /// Builds the configuration to save from the current state, leaving out
    /// <paramref name="excluded"/> (the unsaved new profile), with the physical monitor identities
    /// known to be shared. The profile override is resolved
    /// over the whole list, which is what its index refers to, wherever the excluded profile is.
    /// </summary>
    internal static AppConfig CreateConfig(
        IReadOnlyList<ProfileListItem> profiles,
        AppSettings settings,
        SwitchingState switching,
        ProfileListItem? excluded,
        IReadOnlySet<string>? nonUniqueMonitorIds = null)
    {
        var applicationProfiles = profiles.Where(p => !p.IsWindowsProfile).ToList();
        var config = new AppConfig
        {
            StartWithWindows = settings.StartWithWindows,
            StartMinimized = settings.StartMinimized,
            MinimizeToTray = settings.MinimizeToTray,
            AlwaysOnTop = settings.AlwaysOnTop,
            CheckForUpdates = settings.CheckForUpdates,
            RememberGlobalSwitching = settings.RememberGlobalSwitching,
            RememberOverride = settings.RememberOverride,
            GlobalSwitchingEnabled = settings.RememberGlobalSwitching ? !switching.GlobalPaused : true,
            OverrideMode = settings.RememberOverride ? switching.OverrideMode : OverrideMode.Automatic,
            OverrideProfileId = settings.RememberOverride && switching.OverrideMode == OverrideMode.Profile
                ? applicationProfiles.ElementAtOrDefault(switching.OverrideProfileIndex)?.Id
                : null,
            ShowHideHotkey = settings.ShowHideHotkey,
            WindowsOverrideHotkey = settings.WindowsOverrideHotkey,
            ResumeAutomaticHotkey = settings.ResumeAutomaticHotkey,
            GlobalSwitchingHotkey = settings.GlobalSwitchingHotkey,
            NonUniqueMonitorIds = nonUniqueMonitorIds is { Count: > 0 }
                ? nonUniqueMonitorIds.Order(StringComparer.OrdinalIgnoreCase).ToList()
                : null
        };

        var windows = profiles.FirstOrDefault(p => p.IsWindowsProfile);
        if (windows is not null)
            config.WindowsProfiles.AddRange(windows.DisplayProfiles);

        foreach (var profile in applicationProfiles.Where(p => !ReferenceEquals(p, excluded)))
        {
            config.ApplicationProfiles.Add(new ApplicationProfileConfig
            {
                Id = profile.Id,
                Name = profile.Name,
                ExePaths = profile.ExecutablePaths.ToList(),
                Enabled = profile.Enabled,
                Hotkey = profile.Hotkey,
                DisplayProfiles = profile.DisplayProfiles.ToList()
            });
        }

        return config;
    }
}
