namespace NvProfileSwitcher.Core;

internal static class OverrideCore
{
    /// <summary>
    /// Restores the remembered override from the configuration. A profile override is
    /// restored only when its profile still exists and is enabled; its index is counted
    /// among the application profiles.
    /// </summary>
    internal static (OverrideMode Mode, int ProfileIndex) Restore(
        AppConfig config,
        bool rememberOverride,
        IReadOnlyList<ProfileListItem> applicationProfiles)
    {
        if (!rememberOverride)
            return (OverrideMode.Automatic, -1);

        if (config.OverrideMode == OverrideMode.Windows)
            return (OverrideMode.Windows, -1);

        if (config.OverrideMode != OverrideMode.Profile || config.OverrideProfileId is not Guid profileId)
            return (OverrideMode.Automatic, -1);

        var index = applicationProfiles.ToList().FindIndex(p => p.Id == profileId && p.Enabled);
        return index < 0 ? (OverrideMode.Automatic, -1) : (OverrideMode.Profile, index);
    }

    /// <summary>The Windows override toggles with Automatic; the profile index is kept.</summary>
    internal static OverrideMode ToggleWindows(OverrideMode mode) =>
        mode == OverrideMode.Windows ? OverrideMode.Automatic : OverrideMode.Windows;

    /// <summary>
    /// Toggles the override of an application profile: selecting the profile that is
    /// already the override returns to Automatic. Returns null, leaving the override
    /// unchanged, when the index is out of range or the profile is disabled.
    /// </summary>
    internal static (OverrideMode Mode, int ProfileIndex)? ToggleProfile(
        OverrideMode mode,
        int overrideProfileIndex,
        int requestedIndex,
        IReadOnlyList<ProfileListItem> applicationProfiles)
    {
        if (requestedIndex < 0 || requestedIndex >= applicationProfiles.Count) return null;
        if (!applicationProfiles[requestedIndex].Enabled) return null;

        return mode == OverrideMode.Profile && overrideProfileIndex == requestedIndex
            ? (OverrideMode.Automatic, -1)
            : (OverrideMode.Profile, requestedIndex);
    }

    /// <summary>
    /// The override index after an application profile moved from <paramref name="from"/> to
    /// the adjacent <paramref name="to"/>: a profile override follows the two swapped positions.
    /// </summary>
    internal static int AfterMove(OverrideMode mode, int overrideProfileIndex, int from, int to)
    {
        if (mode != OverrideMode.Profile) return overrideProfileIndex;
        if (overrideProfileIndex == from) return to;
        if (overrideProfileIndex == to) return from;
        return overrideProfileIndex;
    }

    /// <summary>
    /// The override after removing the application profile at <paramref name="removedIndex"/>:
    /// the override of the removed profile returns to Automatic, keeping its index as the
    /// original did, and the override of a later profile follows it one position up.
    /// </summary>
    internal static (OverrideMode Mode, int ProfileIndex) AfterRemove(OverrideMode mode, int overrideProfileIndex, int removedIndex)
    {
        if (mode != OverrideMode.Profile) return (mode, overrideProfileIndex);
        if (overrideProfileIndex == removedIndex) return (OverrideMode.Automatic, overrideProfileIndex);
        if (overrideProfileIndex > removedIndex) return (mode, overrideProfileIndex - 1);
        return (mode, overrideProfileIndex);
    }
}
