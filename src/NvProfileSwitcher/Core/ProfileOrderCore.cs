namespace NvProfileSwitcher.Core;

/// <summary>The order Sort by Name gives the application profiles.</summary>
internal static class ProfileOrderCore
{
    /// <summary>
    /// The application profiles sorted by name, A–Z or Z–A (<paramref name="descending"/>),
    /// ignoring case and following the rules of the current culture; profiles with the same name
    /// keep their order. The unsaved new profile takes no part: it goes last.
    /// </summary>
    internal static IReadOnlyList<ProfileListItem> ByName(
        IReadOnlyList<ProfileListItem> applicationProfiles,
        ProfileListItem? unsaved,
        bool descending,
        StringComparer? comparer = null)
    {
        comparer ??= StringComparer.CurrentCultureIgnoreCase;
        var saved = applicationProfiles.Where(p => !ReferenceEquals(p, unsaved));
        var sorted = descending
            ? saved.OrderByDescending(p => p.Name, comparer)
            : saved.OrderBy(p => p.Name, comparer);

        var order = sorted.ToList();
        if (unsaved is not null && applicationProfiles.Any(p => ReferenceEquals(p, unsaved)))
            order.Add(unsaved);
        return order;
    }
}
