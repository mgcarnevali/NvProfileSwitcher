using NvProfileSwitcher.Nvidia;

namespace NvProfileSwitcher.Core;

internal static class DisplayProfileSyncCore
{
    /// <summary>
    /// Keeps the profiles in line with the connected displays, which all have an identity:
    /// <list type="number">
    /// <item>A monitor that moved to another connection keeps its values (<see cref="Adopt"/>).</item>
    /// <item>The values saved for a physical identity found shared are copied to the connection
    /// identity of each connected monitor that shared it, and then removed.</item>
    /// <item>Every display has values in every profile: a display new to the Windows profile
    /// starts from the current vibrance and hue reported by the API and from the brightness,
    /// contrast and gamma set in the NVIDIA Control Panel, or the neutral values when they cannot
    /// be used; application profiles start from the Windows values. The display names are kept
    /// current.</item>
    /// </list>
    /// Returns whether any profile changed and the monitor ids whose values moved, so a draft
    /// can follow them.
    /// </summary>
    internal static DisplaySyncResult Synchronize(
        IReadOnlyList<ProfileListItem> profiles,
        IReadOnlyList<NvidiaDisplay> displays,
        IReadOnlySet<string> nonUniqueIds,
        int unidentifiedCount,
        Func<NvidiaDisplay, int?> readVibrance,
        Func<NvidiaDisplay, int?> readHue,
        Func<NvidiaDisplay, ControlPanelColors?>? readControlPanelColors = null)
    {
        var windows = profiles.FirstOrDefault(p => p.IsWindowsProfile);
        if (windows is null) return new DisplaySyncResult(false, []);

        var remaps = new List<DisplayRemap>();
        if (unidentifiedCount == 0)
            remaps.AddRange(Adopt(profiles, displays));
        remaps.AddRange(CarryOverSharedIds(profiles, displays, nonUniqueIds));
        foreach (var remap in remaps)
            Remap(profiles, remap, displays);
        var changed = remaps.Count > 0;

        foreach (var profile in profiles)
            changed |= profile.DisplayProfiles.RemoveAll(values => nonUniqueIds.Contains(values.MonitorId)) > 0;

        foreach (var display in displays)
        {
            var desktop = DisplayCore.FindValues(windows, display.MonitorId);
            if (desktop is null)
            {
                var controlPanel = readControlPanelColors?.Invoke(display);
                desktop = new ProfileDisplaySettings
                {
                    Name = WindowsProfileJsonName(display.GdiName),
                    DisplayName = display.Name,
                    MonitorId = display.MonitorId,
                    DigitalVibrance = readVibrance(display) ?? 50,
                    Hue = readHue(display) ?? 0,
                    Brightness = controlPanel?.Brightness ?? 100,
                    Contrast = controlPanel?.Contrast ?? 100,
                    Gamma = controlPanel?.Gamma ?? 1
                };
                windows.DisplayProfiles.Add(desktop);
                changed = true;
            }
            else if (desktop.DisplayName != display.Name)
            {
                desktop.DisplayName = display.Name;
                changed = true;
            }

            foreach (var profile in profiles.Where(p => !p.IsWindowsProfile))
            {
                var values = DisplayCore.FindValues(profile, display.MonitorId);
                if (values is null)
                {
                    profile.DisplayProfiles.Add(DisplayCore.CloneValues(desktop, display.Name, display.MonitorId));
                    changed = true;
                }
                else if (values.DisplayName != display.Name)
                {
                    values.DisplayName = display.Name;
                    changed = true;
                }
            }
        }

        return new DisplaySyncResult(changed, remaps);
    }

    /// <summary>
    /// A connected monitor with a connection identity no profile has values for adopts the
    /// values of a saved connection identity of the same model that is not connected, when the
    /// enumeration leaves no doubt: for that model, exactly one new connection and exactly one
    /// saved one. The caller adopts nothing while any display has no identity, as it could be
    /// one of them.
    /// </summary>
    internal static IReadOnlyList<DisplayRemap> Adopt(IReadOnlyList<ProfileListItem> profiles, IReadOnlyList<NvidiaDisplay> displays)
    {
        var connected = displays.Select(d => d.MonitorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var saved = profiles.SelectMany(p => p.DisplayProfiles).Select(v => v.MonitorId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var remaps = new List<DisplayRemap>();
        var newConnections = displays
            .Where(d => MonitorIdentityCore.IsConnectionId(d.MonitorId) && !saved.Contains(d.MonitorId))
            .GroupBy(d => MonitorIdentityCore.ConnectionHardware(d.MonitorId), StringComparer.OrdinalIgnoreCase);
        foreach (var model in newConnections)
        {
            var candidates = saved
                .Where(id => MonitorIdentityCore.IsConnectionId(id) && !connected.Contains(id) &&
                             string.Equals(MonitorIdentityCore.ConnectionHardware(id), model.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (model.Count() == 1 && candidates.Count == 1)
                remaps.Add(new DisplayRemap(candidates[0], [model.Single().MonitorId]));
        }

        return remaps;
    }

    // The saved values of a physical identity found shared go to the connection identity of
    // every connected monitor that shared it, unless a profile already has values for it.
    private static IReadOnlyList<DisplayRemap> CarryOverSharedIds(
        IReadOnlyList<ProfileListItem> profiles, IReadOnlyList<NvidiaDisplay> displays, IReadOnlySet<string> nonUniqueIds)
    {
        var saved = profiles.SelectMany(p => p.DisplayProfiles).Select(v => v.MonitorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return displays
            .Where(d => d.PhysicalMonitorId is { } physical && nonUniqueIds.Contains(physical) && saved.Contains(physical) && !saved.Contains(d.MonitorId))
            .GroupBy(d => d.PhysicalMonitorId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DisplayRemap(group.Key, group.Select(d => d.MonitorId).ToList()))
            .ToList();
    }

    /// <summary>
    /// Moves the values of <see cref="DisplayRemap.From"/> to every monitor id of
    /// <see cref="DisplayRemap.To"/> that the profile has no values for, and removes them.
    /// </summary>
    internal static void Remap(IEnumerable<ProfileListItem> profiles, DisplayRemap remap, IReadOnlyList<NvidiaDisplay> displays)
    {
        foreach (var profile in profiles)
        {
            if (DisplayCore.FindValues(profile, remap.From) is not { } values)
                continue;

            foreach (var to in remap.To)
            {
                if (DisplayCore.FindValues(profile, to) is not null)
                    continue;
                var name = displays.FirstOrDefault(d => DisplayCore.SameMonitorId(d.MonitorId, to))?.Name ?? values.DisplayName;
                var moved = ProfileEditingCore.CloneDisplaySettings(values);
                moved.MonitorId = to;
                moved.DisplayName = name;
                profile.DisplayProfiles.Add(moved);
            }

            profile.DisplayProfiles.RemoveAll(v => DisplayCore.SameMonitorId(v.MonitorId, remap.From));
        }
    }

    /// <summary>Name of a Windows profile entry: "Display N" from the GDI device name.</summary>
    internal static string WindowsProfileJsonName(string gdiName)
    {
        var pos = gdiName.LastIndexOf("DISPLAY", StringComparison.OrdinalIgnoreCase);
        if (pos >= 0)
        {
            var number = gdiName[(pos + 7)..];
            if (number.Length > 0) return "Display " + number;
        }
        return "Display";
    }
}

/// <summary>Whether a synchronization changed any profile, and the monitor ids whose values moved.</summary>
internal sealed record DisplaySyncResult(bool Changed, IReadOnlyList<DisplayRemap> Remaps);

/// <summary>The values of the monitor id <paramref name="From"/> moved to each monitor id of <paramref name="To"/>.</summary>
internal sealed record DisplayRemap(string From, IReadOnlyList<string> To);
