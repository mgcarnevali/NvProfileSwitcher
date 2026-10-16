using NvProfileSwitcher.Models;

namespace NvProfileSwitcher.Core;

/// <summary>
/// Pure rules around the active profile that complement <see cref="SwitchingCore"/>: the
/// switching candidates, the target of a real colors reapplication and the status bar text.
/// </summary>
internal static class ActiveProfileCore
{
    /// <summary>
    /// The application profiles as switching candidates; a profile without executables
    /// matches by its name.
    /// </summary>
    internal static IReadOnlyList<SwitchingCandidate> Candidates(IReadOnlyList<ProfileListItem> applicationProfiles) =>
        applicationProfiles
            .Select(p => new SwitchingCandidate(
                p.Name,
                p.ExecutablePaths.Count > 0 ? p.ExecutablePaths : [p.Name],
                p.Enabled))
            .ToList();

    /// <summary>
    /// Index of the application profile whose colors are the real ones, or -1 for Windows.
    /// Unlike <see cref="SwitchingCore.SelectTarget"/>, a profile override with a valid index
    /// applies that profile even when it is disabled, and an invalid profile override applies
    /// Windows instead of falling back to Automatic, as the original reapplication does.
    /// The foreground application is read only in Automatic mode, as in the original.
    /// </summary>
    internal static int RealColorsTarget(
        IReadOnlyList<SwitchingCandidate> candidates,
        Func<string> foregroundExecutable,
        OverrideMode mode,
        int overrideProfileIndex) => mode switch
        {
            OverrideMode.Profile => overrideProfileIndex >= 0 && overrideProfileIndex < candidates.Count ? overrideProfileIndex : -1,
            OverrideMode.Automatic => SwitchingCore.SelectTarget(candidates, foregroundExecutable(), OverrideMode.Automatic, -1).ProfileIndex,
            _ => -1,
        };

    /// <summary>The status bar text of the active profile.</summary>
    internal static string StatusText(ActiveProfileDisplay display)
    {
        if (display.GlobalPaused)
            return "Windows";

        var name = string.IsNullOrWhiteSpace(display.Name) ? "Windows" : display.Name;
        return display.Mode == OverrideMode.Automatic ? name : name + " (override)";
    }
}
