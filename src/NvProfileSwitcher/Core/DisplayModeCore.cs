namespace NvProfileSwitcher.Core;

/// <summary>A display mode: resolution in pixels and refresh rate in hertz.</summary>
internal readonly record struct DisplayModeInfo(int Width, int Height, int RefreshRate)
{
    public bool SameResolution(DisplayResolution resolution) => Width == resolution.Width && Height == resolution.Height;
}

/// <summary>
/// A monitor whose mode was changed: its desktop mode, the mode requested and the mode read back,
/// and its connection identity (<see cref="MonitorIdentityCore.ConnectionId"/>), which it takes
/// if its serial number is later found shared; null in files written without it.
/// </summary>
internal sealed record DisplayModeRecoveryEntry(
    string MonitorId, DisplayModeInfo Original, DisplayModeInfo Target, DisplayModeInfo? Applied, string? Connection = null);

/// <summary>The content of the recovery file (display-modes.json).</summary>
internal sealed class DisplayModeRecovery
{
    public List<DisplayModeRecoveryEntry> Monitors { get; set; } = [];
}

/// <summary>The desktop mode of a monitor and the modes it supports: what the editor offers from.</summary>
internal sealed record DisplayModeOptions(DisplayModeInfo Desktop, IReadOnlyList<DisplayModeInfo> Supported);

/// <summary>Choosing the display mode a profile's resolution and refresh rate are applied with.</summary>
internal static class DisplayModeCore
{
    /// <summary>
    /// Whether a monitor reporting <paramref name="current"/> still shows <paramref name="applied"/>,
    /// a mode this application set: the same resolution, and a refresh rate at most 1 Hz apart,
    /// since a fractional rate (59.94, 143.98) can be reported rounded either way. Only to
    /// recognize the application's own mode; whether to apply or restore a mode is decided with
    /// exact modes, so 59 and 60 Hz stay different choices.
    /// </summary>
    internal static bool StillShows(DisplayModeInfo current, DisplayModeInfo applied) =>
        current.Width == applied.Width && current.Height == applied.Height &&
        Math.Abs(current.RefreshRate - applied.RefreshRate) <= 1;

    /// <summary>
    /// The mode a profile asks for among the <paramref name="supported"/> modes of a monitor whose
    /// desktop mode is <paramref name="desktop"/>: the <paramref name="resolution"/>, or the desktop
    /// one when null, at the <paramref name="rate"/>. Without a rate, the desktop rate when that
    /// resolution supports it, and otherwise its highest; with <see cref="RefreshRateSetting.Highest"/>,
    /// its highest. The reference is always the desktop mode, never a mode another profile applied.
    /// Null when the monitor does not support the resolution, or an explicit rate at that
    /// resolution: a rate the user chose is never replaced by another one.
    /// </summary>
    internal static DisplayModeInfo? ChooseMode(
        IReadOnlyList<DisplayModeInfo> supported, DisplayResolution? resolution, DisplayModeInfo desktop, RefreshRateSetting? rate = null)
    {
        var width = resolution?.Width ?? desktop.Width;
        var height = resolution?.Height ?? desktop.Height;
        DisplayModeInfo? highest = null;
        DisplayModeInfo? atDesktopRate = null;
        foreach (var mode in supported)
        {
            if (mode.Width != width || mode.Height != height)
                continue;
            if (rate is { IsHighest: false } explicitRate)
            {
                if (mode.RefreshRate == explicitRate.Hertz)
                    return mode;
                continue;
            }

            if (mode.RefreshRate == desktop.RefreshRate)
                atDesktopRate = mode;
            if (highest is not { } best || mode.RefreshRate > best.RefreshRate)
                highest = mode;
        }

        return rate is { IsHighest: true } ? highest : atDesktopRate ?? highest;
    }

    /// <summary>The resolutions a monitor offers, one per width and height, largest first.</summary>
    internal static IReadOnlyList<DisplayResolution> OfferedResolutions(IReadOnlyList<DisplayModeInfo> supported) =>
        supported
            .Where(mode => mode.Width > 0 && mode.Height > 0)
            .Select(mode => new DisplayResolution(mode.Width, mode.Height))
            .Distinct()
            .OrderByDescending(resolution => resolution.Width)
            .ThenByDescending(resolution => resolution.Height)
            .ToList();

    /// <summary>The refresh rates a monitor offers at <paramref name="resolution"/>, highest first.</summary>
    internal static IReadOnlyList<int> OfferedRefreshRates(IReadOnlyList<DisplayModeInfo> supported, DisplayResolution resolution) =>
        supported
            .Where(mode => mode.SameResolution(resolution) && mode.RefreshRate > 0)
            .Select(mode => mode.RefreshRate)
            .Distinct()
            .OrderDescending()
            .ToList();
}
