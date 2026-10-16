using NvProfileSwitcher.Nvidia;

namespace NvProfileSwitcher.Core;

/// <summary>The displays saved in the profiles, as the original Manage displays dialog listed and removed them.</summary>
internal static class SavedDisplaysCore
{
    /// <summary>
    /// Lists every display saved in any profile once: the connected ones first, in display
    /// order and named by their label, then the disconnected ones, in the order they were
    /// found. A saved name that is only the monitor id takes the first display name found.
    /// </summary>
    internal static List<SavedDisplayInfo> Build(IEnumerable<ProfileListItem> profiles, IEnumerable<NvidiaDisplay> connectedDisplays)
    {
        var saved = new List<SavedDisplayInfo>();
        foreach (var profile in profiles)
        {
            foreach (var values in profile.DisplayProfiles)
            {
                if (string.IsNullOrWhiteSpace(values.MonitorId)) continue;
                var found = saved.FirstOrDefault(d => DisplayCore.SameMonitorId(d.MonitorId, values.MonitorId));
                if (found is null) saved.Add(new SavedDisplayInfo(values.MonitorId, values.DisplayName, false));
                else if (found.Name == found.MonitorId && !string.IsNullOrWhiteSpace(values.DisplayName)) found.Name = values.DisplayName;
            }
        }

        var result = new List<SavedDisplayInfo>();
        foreach (var active in connectedDisplays)
        {
            var found = saved.FirstOrDefault(d => DisplayCore.SameMonitorId(d.MonitorId, active.MonitorId));
            if (found is null) continue;
            result.Add(new SavedDisplayInfo(found.MonitorId, active.Label, true));
        }
        foreach (var item in saved)
            if (!result.Any(d => DisplayCore.SameMonitorId(d.MonitorId, item.MonitorId))) result.Add(item);
        return result;
    }

    /// <summary>Removes the display's saved values from every profile.</summary>
    internal static void Remove(IEnumerable<ProfileListItem> profiles, string monitorId)
    {
        foreach (var profile in profiles)
            profile.DisplayProfiles.RemoveAll(v => DisplayCore.SameMonitorId(v.MonitorId, monitorId));
    }
}
