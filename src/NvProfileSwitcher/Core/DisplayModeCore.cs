namespace NvProfileSwitcher.Core;

/// <summary>A display mode: resolution in pixels and refresh rate in hertz.</summary>
internal readonly record struct DisplayModeInfo(int Width, int Height, int RefreshRate)
{
    public bool SameResolution(DisplayResolution resolution) => Width == resolution.Width && Height == resolution.Height;
}

/// <summary>A monitor whose mode was changed: its desktop mode, the mode requested and the mode read back.</summary>
internal sealed record DisplayModeRecoveryEntry(string MonitorId, DisplayModeInfo Original, DisplayModeInfo Target, DisplayModeInfo? Applied);

/// <summary>The content of the recovery file (display-modes.json).</summary>
internal sealed class DisplayModeRecovery
{
    public List<DisplayModeRecoveryEntry> Monitors { get; set; } = [];
}

/// <summary>Choosing the display mode a profile's resolution is applied with.</summary>
internal static class DisplayModeCore
{
    /// <summary>
    /// The mode for <paramref name="resolution"/> among the <paramref name="supported"/> modes of a
    /// monitor: at the refresh rate of the monitor's desktop mode, <paramref name="desktop"/>, when
    /// that resolution supports it, and otherwise at its highest refresh rate. The reference is
    /// always the desktop mode, never a mode another profile applied. Null when the monitor does
    /// not support the resolution.
    /// </summary>
    internal static DisplayModeInfo? ChooseMode(IReadOnlyList<DisplayModeInfo> supported, DisplayResolution resolution, DisplayModeInfo desktop)
    {
        DisplayModeInfo? highest = null;
        foreach (var mode in supported)
        {
            if (!mode.SameResolution(resolution))
                continue;
            if (mode.RefreshRate == desktop.RefreshRate)
                return mode;
            if (highest is not { } best || mode.RefreshRate > best.RefreshRate)
                highest = mode;
        }

        return highest;
    }

    /// <summary>
    /// The resolutions offered for a monitor, one per width and height, largest first, each with
    /// the mode <see cref="ChooseMode"/> would apply from <paramref name="desktop"/>.
    /// </summary>
    internal static IReadOnlyList<DisplayModeInfo> OfferedModes(IReadOnlyList<DisplayModeInfo> supported, DisplayModeInfo desktop) =>
        supported
            .Where(mode => mode.Width > 0 && mode.Height > 0)
            .Select(mode => new DisplayResolution(mode.Width, mode.Height))
            .Distinct()
            .Select(resolution => ChooseMode(supported, resolution, desktop)!.Value)
            .OrderByDescending(mode => mode.Width)
            .ThenByDescending(mode => mode.Height)
            .ToList();
}
