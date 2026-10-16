namespace NvProfileSwitcher;

internal readonly record struct SwitchingCandidate(string Name, IReadOnlyList<string> ExecutablePaths, bool Enabled)
{
    internal SwitchingCandidate(string name, string executablePath, bool enabled)
        : this(name, string.IsNullOrWhiteSpace(executablePath) ? Array.Empty<string>() : new[] { executablePath }, enabled) { }
}
internal readonly record struct SwitchingDecision(OverrideMode Mode, int ProfileIndex, string ActiveName);

internal static class SwitchingCore
{
    internal static bool ExecutableMatches(string configuredPathOrName, string foregroundExecutable)
    {
        if (string.IsNullOrWhiteSpace(foregroundExecutable)) return false;
        var configured = Path.GetFileName(configuredPathOrName);
        return !string.IsNullOrWhiteSpace(configured) &&
               string.Equals(configured, foregroundExecutable, StringComparison.OrdinalIgnoreCase);
    }

    internal static SwitchingDecision SelectTarget(
        IReadOnlyList<SwitchingCandidate> profiles,
        string foregroundExecutable,
        OverrideMode mode,
        int overrideProfileIndex)
    {
        if (mode == OverrideMode.Windows)
            return new SwitchingDecision(mode, -1, "Windows");

        if (mode == OverrideMode.Profile)
        {
            if (overrideProfileIndex >= 0 && overrideProfileIndex < profiles.Count && profiles[overrideProfileIndex].Enabled)
                return new SwitchingDecision(mode, overrideProfileIndex, profiles[overrideProfileIndex].Name);

            mode = OverrideMode.Automatic;
            overrideProfileIndex = -1;
        }

        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            if (profile.Enabled && profile.ExecutablePaths.Any(path => ExecutableMatches(path, foregroundExecutable)))
                return new SwitchingDecision(mode, i, profile.Name);
        }

        return new SwitchingDecision(mode, -1, "Windows");
    }
}
