namespace NvProfileSwitcher.Models;

/// <summary>
/// The active profile as last published to the status bar. It is a snapshot because the
/// status bar changes only when the active profile is published, as in the original.
/// </summary>
internal sealed record ActiveProfileDisplay(string Name, OverrideMode Mode, bool GlobalPaused)
{
    /// <summary>Shown until the first publication: "Windows".</summary>
    public static ActiveProfileDisplay Initial { get; } = new("Windows", OverrideMode.Automatic, false);
}
