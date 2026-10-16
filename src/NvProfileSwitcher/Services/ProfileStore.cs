using System.Collections.ObjectModel;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;

namespace NvProfileSwitcher.Services;

/// <summary>
/// The profile list: the Windows profile first, then the application profiles in order,
/// and the new profile that has not been saved yet, if any. Its changes keep the list
/// consistent on their own: the ▲▼ state follows every change, and a profile override, which
/// is a position in this list, stays on its profile when profiles are removed or moved.
/// </summary>
internal sealed class ProfileStore
{
    private readonly ObservableCollection<ProfileListItem> profiles = [];
    private readonly SwitchingState switching;
    private readonly HashSet<string> nonUniqueMonitorIds = new(StringComparer.OrdinalIgnoreCase);

    public ProfileStore(SwitchingState switching)
    {
        this.switching = switching;
        Profiles = new ReadOnlyObservableCollection<ProfileListItem>(profiles);
    }

    /// <summary>The list, which changes only through this store.</summary>
    public ReadOnlyObservableCollection<ProfileListItem> Profiles { get; }

    public ProfileListItem? Windows => profiles.FirstOrDefault(p => p.IsWindowsProfile);

    public IReadOnlyList<ProfileListItem> ApplicationProfiles => profiles.Where(p => !p.IsWindowsProfile).ToList();

    /// <summary>
    /// Physical monitor identities found shared by two connected monitors: those monitors are
    /// told apart by their connection identity (<see cref="MonitorIdentityCore"/>).
    /// </summary>
    public IReadOnlySet<string> NonUniqueMonitorIds => nonUniqueMonitorIds;

    /// <summary>The profile added with Add profile until it is saved, removed or discarded.</summary>
    public ProfileListItem? UnsavedNewProfile { get; private set; }

    /// <summary>
    /// Replaces the whole list, which has no unsaved new profile. The profile override is set
    /// by whoever loads the list, from the same configuration.
    /// </summary>
    public void Load(IEnumerable<ProfileListItem> loaded)
    {
        profiles.Clear();
        foreach (var profile in loaded)
            profiles.Add(profile);
        UnsavedNewProfile = null;
        UpdateMoveState();
    }

    /// <summary>Replaces the physical identities known to be shared, from the same configuration as the list.</summary>
    public void LoadNonUniqueMonitorIds(IEnumerable<string>? ids)
    {
        nonUniqueMonitorIds.Clear();
        foreach (var id in ids ?? [])
            if (!string.IsNullOrWhiteSpace(id))
                nonUniqueMonitorIds.Add(id);
    }

    /// <summary>Records physical identities found shared; true when any was new.</summary>
    public bool MarkNonUnique(IEnumerable<string> ids)
    {
        var added = false;
        foreach (var id in ids)
            added |= nonUniqueMonitorIds.Add(id);
        return added;
    }

    /// <summary>Adds <paramref name="profile"/> at the end as the unsaved new profile.</summary>
    public void AddUnsaved(ProfileListItem profile)
    {
        UnsavedNewProfile = profile;
        profiles.Add(profile);
        UpdateMoveState();
    }

    /// <summary><paramref name="profile"/> was saved: it is no longer the unsaved new profile.</summary>
    public void MarkSaved(ProfileListItem profile)
    {
        if (ReferenceEquals(profile, UnsavedNewProfile))
            UnsavedNewProfile = null;
    }

    /// <summary>
    /// Removes <paramref name="profile"/> and returns the list position it had. The override of
    /// the removed profile returns to Automatic; the override of a later profile follows it.
    /// </summary>
    public int Remove(ProfileListItem profile)
    {
        var listIndex = profiles.IndexOf(profile);
        if (listIndex < 0)
            return listIndex;

        (switching.OverrideMode, switching.OverrideProfileIndex) =
            OverrideCore.AfterRemove(switching.OverrideMode, switching.OverrideProfileIndex, listIndex - 1);
        profiles.RemoveAt(listIndex);
        if (ReferenceEquals(profile, UnsavedNewProfile))
            UnsavedNewProfile = null;
        UpdateMoveState();
        return listIndex;
    }

    /// <summary>Moves the profile at <paramref name="listIndex"/> to <paramref name="newListIndex"/>; a profile override follows its profile.</summary>
    public void Move(int listIndex, int newListIndex)
    {
        profiles.Move(listIndex, newListIndex);
        switching.OverrideProfileIndex = OverrideCore.AfterMove(
            switching.OverrideMode, switching.OverrideProfileIndex, listIndex - 1, newListIndex - 1);
        UpdateMoveState();
    }

    // The Windows profile and its first follower never move up; the last profile never moves down.
    private void UpdateMoveState()
    {
        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            profile.CanMoveUp = profile.CanReorder && i > 1;
            profile.CanMoveDown = profile.CanReorder && i < profiles.Count - 1;
        }
    }
}
