namespace NvProfileSwitcher;

/// <summary>An item of the Resolution selector: "Don't change", or a resolution.</summary>
internal sealed class ResolutionComboItem
{
    public string Text { get; init; } = string.Empty;
    public DisplayResolution? Resolution { get; init; }

    public static ResolutionComboItem DontChange() => new() { Text = "Don't change" };

    public static ResolutionComboItem Offered(DisplayResolution resolution) =>
        new() { Text = $"{resolution.Width} × {resolution.Height}", Resolution = resolution };

    /// <summary>The saved resolution before the list is read, or one the display does not offer.</summary>
    public static ResolutionComboItem Saved(DisplayResolution resolution, bool supported) =>
        new() { Text = supported ? $"{resolution.Width} × {resolution.Height}" : $"{resolution.Width} × {resolution.Height} · not supported", Resolution = resolution };
}

/// <summary>
/// An item of the Refresh rate selector: the desktop rate (the default), the highest rate, or an
/// explicit one. Once the display's modes are read, the first two show the rate they would apply.
/// The first explicit rate shows the "Fixed refresh rate" separator and caption above it, so the
/// list holds only rates: nothing in it is a header the keyboard or a selection could land on.
/// </summary>
internal sealed class RefreshRateComboItem
{
    public string Text { get; init; } = string.Empty;
    public RefreshRateSetting? Rate { get; init; }

    /// <summary>Whether the fixed rates start with this one: the list shows their caption above it.</summary>
    public bool StartsFixedRates { get; init; }

    /// <summary>This item, as the first of the fixed rates.</summary>
    public RefreshRateComboItem AsFirstFixed() => new() { Text = Text, Rate = Rate, StartsFixedRates = true };

    public static RefreshRateComboItem Desktop(int? hertz) =>
        new() { Text = hertz is { } value ? $"Desktop rate · {value} Hz" : "Desktop rate" };

    public static RefreshRateComboItem Highest(int? hertz) =>
        new() { Text = hertz is { } value ? $"Highest available · {value} Hz" : "Highest available", Rate = RefreshRateSetting.Highest };

    public static RefreshRateComboItem Offered(int hertz) =>
        new() { Text = $"{hertz} Hz", Rate = RefreshRateSetting.Explicit(hertz) };

    /// <summary>The saved rate before the list is read, or one the resolution does not offer.</summary>
    public static RefreshRateComboItem Saved(RefreshRateSetting rate, bool supported) =>
        new() { Text = supported ? $"{rate.Hertz} Hz" : $"{rate.Hertz} Hz · not supported", Rate = rate };
}
