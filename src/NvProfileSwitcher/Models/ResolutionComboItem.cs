namespace NvProfileSwitcher;

/// <summary>An item of the Resolution selector: "Don't change", or a resolution.</summary>
internal sealed class ResolutionComboItem
{
    public string Text { get; init; } = string.Empty;
    public DisplayResolution? Resolution { get; init; }

    public static ResolutionComboItem DontChange() => new() { Text = "Don't change" };

    public static ResolutionComboItem Offered(DisplayResolution resolution) => new() { Text = resolution.ToString(), Resolution = resolution };

    /// <summary>A saved resolution the display does not offer: kept, marked, and never applied by the driver.</summary>
    public static ResolutionComboItem NotOffered(DisplayResolution resolution) =>
        new() { Text = $"{resolution} · Not supported", Resolution = resolution };
}
