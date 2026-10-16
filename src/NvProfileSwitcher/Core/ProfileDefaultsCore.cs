using NvProfileSwitcher.Nvidia;

namespace NvProfileSwitcher.Core;

/// <summary>The profile Add profile creates, as the original created it.</summary>
internal static class ProfileDefaultsCore
{
    /// <summary>
    /// An enabled "New Profile" with values for every connected display: the Windows profile's
    /// values for that monitor, or the NVIDIA neutral values when it has none.
    /// </summary>
    internal static ProfileListItem NewProfile(ProfileListItem? windows, IEnumerable<NvidiaDisplay> displays)
    {
        var profile = new ProfileListItem("New Profile") { Enabled = true };
        foreach (var display in displays)
        {
            var source = windows is not null ? DisplayCore.FindValues(windows, display.MonitorId) : null;
            profile.DisplayProfiles.Add(new ProfileDisplaySettings
            {
                DisplayName = display.Name,
                MonitorId = display.MonitorId,
                Brightness = source?.Brightness ?? 100,
                Contrast = source?.Contrast ?? 100,
                Gamma = source?.Gamma ?? 1,
                DigitalVibrance = source?.DigitalVibrance ?? 50,
                Hue = source?.Hue ?? 0
            });
        }

        return profile;
    }
}
