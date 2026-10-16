using NvProfileSwitcher.Core;

namespace NvProfileSwitcher.Services;

/// <summary>
/// Keeps the profiles in line with the connected displays and saves the configuration
/// when that changed any profile. <paramref name="readNvTweakColors"/> reads the NVIDIA Control
/// Panel settings of a device id from the registry (<see cref="Nvidia.NvTweakColorRegistry"/>).
/// </summary>
internal sealed class DisplayProfileSyncService(
    ProfileStore profiles,
    NvidiaService nvidia,
    DisplayColorController colors,
    ConfigurationService configuration,
    Func<uint, IReadOnlyList<NvTweakValue?>?>? readNvTweakColors = null)
{
    /// <summary>
    /// Records the physical identities the last enumeration found shared, synchronizes the
    /// profiles and returns the monitor ids whose values moved, which a draft follows.
    /// </summary>
    public IReadOnlyList<DisplayRemap> Synchronize(IReadOnlyList<string>? newlySharedIds = null)
    {
        var marked = profiles.MarkNonUnique(newlySharedIds ?? []);
        var result = DisplayProfileSyncCore.Synchronize(
            profiles.Profiles, nvidia.Displays, profiles.NonUniqueMonitorIds, nvidia.UnidentifiedCount, ReadVibrance, ReadHue,
            ReadControlPanelColors);
        if (marked || result.Changed)
            configuration.Save();
        return result.Remaps;
    }

    // A display shows colors of its own only until this application writes it; after that it
    // shows a profile or a preview, which must not become the Windows values of a monitor
    // (deliberate deviation: the original read it anyway), so the NVIDIA defaults are used.
    private int? ReadVibrance(Nvidia.NvidiaDisplay display) => colors.HasWritten(display.MonitorId) ? null : nvidia.GetVibrance(display);

    private int? ReadHue(Nvidia.NvidiaDisplay display) => colors.HasWritten(display.MonitorId) ? null : nvidia.GetHue(display);

    // Read only for a display new to the Windows profile, as vibrance and hue: the NVIDIA Control
    // Panel settings start its values once; after that this application owns them.
    private ControlPanelColors? ReadControlPanelColors(Nvidia.NvidiaDisplay display) =>
        readNvTweakColors is null || colors.HasWritten(display.MonitorId) || nvidia.GetNvTweakDeviceId(display) is not { } deviceId
            ? null
            : NvTweakColorCore.Parse(readNvTweakColors(deviceId));
}
