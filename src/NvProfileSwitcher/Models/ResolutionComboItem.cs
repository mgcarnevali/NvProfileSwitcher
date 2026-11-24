using NvProfileSwitcher.Core;

namespace NvProfileSwitcher;

/// <summary>An item of the Resolution selector: "Don't change", or a resolution with the refresh rate it would use.</summary>
internal sealed class ResolutionComboItem
{
    public string Text { get; init; } = string.Empty;
    public DisplayResolution? Resolution { get; init; }

    public static ResolutionComboItem DontChange() => new() { Text = "Don't change" };

    public static ResolutionComboItem Offered(DisplayModeInfo mode) =>
        new() { Text = $"{mode.Width} × {mode.Height} · {mode.RefreshRate} Hz", Resolution = new DisplayResolution(mode.Width, mode.Height) };

    /// <summary>The saved resolution before the list is read, or one the display does not offer.</summary>
    public static ResolutionComboItem Saved(DisplayResolution resolution, bool supported) =>
        new() { Text = supported ? $"{resolution.Width} × {resolution.Height}" : $"{resolution.Width} × {resolution.Height} · not supported", Resolution = resolution };
}
