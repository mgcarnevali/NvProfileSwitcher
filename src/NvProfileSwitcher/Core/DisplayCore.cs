namespace NvProfileSwitcher;

internal static class DisplayCore
{
    internal static bool SameMonitorId(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The profile's values for a monitor, or null when it has none or the id is blank.</summary>
    internal static ProfileDisplaySettings? FindValues(ProfileListItem profile, string? monitorId) =>
        profile.DisplayProfiles.FirstOrDefault(values => SameMonitorId(values.MonitorId, monitorId));

    internal static ProfileDisplaySettings CloneValues(ProfileDisplaySettings source, string displayName, string monitorId) => new()
    {
        DisplayName = displayName,
        MonitorId = monitorId,
        Brightness = source.Brightness,
        Contrast = source.Contrast,
        Gamma = source.Gamma,
        DigitalVibrance = source.DigitalVibrance,
        Hue = source.Hue
    };
}
