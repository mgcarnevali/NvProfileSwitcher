using Xunit;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;

namespace NvProfileSwitcher.Core.Tests;

// The store keeps its list consistent on every change: the ▲▼ state, the unsaved new profile
// and the profile override, which is a position in the list.
public sealed class ProfileStoreTests
{
    private readonly SwitchingState switching = new();

    private ProfileStore Store(params string[] applicationProfiles)
    {
        var store = new ProfileStore(switching);
        store.Load([new ProfileListItem("Windows", isWindowsProfile: true), .. applicationProfiles.Select(name => new ProfileListItem(name))]);
        return store;
    }

    private static string MoveState(ProfileStore store) =>
        string.Join(" ", store.Profiles.Select(p => $"{p.Name}:{(p.CanMoveUp ? "U" : "-")}{(p.CanMoveDown ? "D" : "-")}"));

    private static List<string> Names(ProfileStore store) => store.Profiles.Select(p => p.Name).ToList();

    private void OverrideOn(int applicationIndex)
    {
        switching.OverrideMode = OverrideMode.Profile;
        switching.OverrideProfileIndex = applicationIndex;
    }

    // The Windows profile never moves and the first application profile cannot go above it.
    [Fact]
    public void LoadingUpdatesTheMoveState() =>
        Assert.Equal("Windows:-- A:-D B:UD C:U-", MoveState(Store("A", "B", "C")));

    [Fact]
    public void ASingleApplicationProfileCannotMove() =>
        Assert.Equal("Windows:-- A:--", MoveState(Store("A")));

    [Fact]
    public void EveryChangeUpdatesTheMoveState()
    {
        var store = Store("A", "B");

        store.AddUnsaved(new ProfileListItem("C"));
        Assert.Equal("Windows:-- A:-D B:UD C:U-", MoveState(store));

        store.Move(3, 1);
        Assert.Equal("Windows:-- C:-D A:UD B:U-", MoveState(store));

        store.Remove(store.Profiles[3]);
        Assert.Equal("Windows:-- C:-D A:U-", MoveState(store));
    }

    [Fact]
    public void TheUnsavedProfileStaysUntilSavedRemovedOrReplaced()
    {
        var store = Store("A");
        var added = new ProfileListItem("New");

        store.AddUnsaved(added);
        Assert.Same(added, store.UnsavedNewProfile);
        store.MarkSaved(store.Profiles[1]);
        Assert.Same(added, store.UnsavedNewProfile);
        store.MarkSaved(added);
        Assert.Null(store.UnsavedNewProfile);

        store.AddUnsaved(added = new ProfileListItem("Other"));
        store.Remove(added);
        Assert.Null(store.UnsavedNewProfile);

        store.AddUnsaved(new ProfileListItem("Third"));
        store.Load([new ProfileListItem("Windows", isWindowsProfile: true)]);
        Assert.Null(store.UnsavedNewProfile);
    }

    // The override of a removed profile returns to Automatic, keeping its index as the original
    // did; a later one follows its profile.
    [Theory]
    [InlineData(0, (int)OverrideMode.Automatic, 0)]
    [InlineData(1, (int)OverrideMode.Profile, 0)]
    [InlineData(2, (int)OverrideMode.Profile, 1)]
    public void RemovingAProfileKeepsTheOverrideOnItsProfile(int overrideIndex, int modeValue, int index)
    {
        var store = Store("A", "B", "C");
        OverrideOn(overrideIndex);

        store.Remove(store.Profiles[1]);

        Assert.Equal(((OverrideMode)modeValue, index), (switching.OverrideMode, switching.OverrideProfileIndex));
    }

    [Fact]
    public void RemoveReturnsTheListPosition()
    {
        var store = Store("A", "B", "C");

        Assert.Equal(2, store.Remove(store.Profiles[2]));
        Assert.Equal(["Windows", "A", "C"], Names(store));
        Assert.Equal(-1, store.Remove(new ProfileListItem("Missing")));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    public void MovingAProfileKeepsTheOverrideOnItsProfile(int overrideIndex, int expected)
    {
        var store = Store("A", "B", "C");
        OverrideOn(overrideIndex);

        store.Move(1, 2);

        Assert.Equal(["Windows", "B", "A", "C"], Names(store));
        Assert.Equal(expected, switching.OverrideProfileIndex);
    }

    [Fact]
    public void WithoutAProfileOverrideTheIndexIsLeftAlone()
    {
        var store = Store("A", "B");
        switching.OverrideMode = OverrideMode.Windows;
        switching.OverrideProfileIndex = -1;

        store.Move(1, 2);
        store.Remove(store.Profiles[1]);

        Assert.Equal((OverrideMode.Windows, -1), (switching.OverrideMode, switching.OverrideProfileIndex));
    }

    // Sort by Name reorders the application profiles at once: the Windows profile stays first,
    // the ▲▼ state follows, and a profile override stays on its profile.
    [Fact]
    public void ReorderPutsTheApplicationProfilesInTheGivenOrder()
    {
        var store = Store("A", "B", "C");
        var app = store.ApplicationProfiles;

        Assert.True(store.Reorder([app[2], app[0], app[1]]));

        Assert.Equal(["Windows", "C", "A", "B"], Names(store));
        Assert.Equal("Windows:-- C:-D A:UD B:U-", MoveState(store));
    }

    [Fact]
    public void ReorderKeepsTheProfileOverrideOnItsProfile()
    {
        var store = Store("A", "B", "C");
        var app = store.ApplicationProfiles;
        OverrideOn(0);

        store.Reorder([app[1], app[2], app[0]]);

        Assert.Equal((OverrideMode.Profile, 2), (switching.OverrideMode, switching.OverrideProfileIndex));
        Assert.Same(app[0], store.ApplicationProfiles[switching.OverrideProfileIndex]);
    }

    [Theory]
    [InlineData((int)OverrideMode.Automatic)]
    [InlineData((int)OverrideMode.Windows)]
    public void ReorderLeavesAnotherOverrideAlone(int modeValue)
    {
        var mode = (OverrideMode)modeValue;
        var store = Store("A", "B", "C");
        var app = store.ApplicationProfiles;
        switching.OverrideMode = mode;
        switching.OverrideProfileIndex = 0;

        store.Reorder([app[2], app[1], app[0]]);

        Assert.Equal((mode, 0), (switching.OverrideMode, switching.OverrideProfileIndex));
    }

    [Fact]
    public void ReorderingIntoTheSameOrderChangesNothing()
    {
        var store = Store("A", "B");
        var changes = 0;
        ((System.Collections.Specialized.INotifyCollectionChanged)store.Profiles).CollectionChanged += (_, _) => changes++;

        Assert.False(store.Reorder(store.ApplicationProfiles));

        Assert.Equal(0, changes);
        Assert.Equal(["Windows", "A", "B"], Names(store));
    }

    [Fact]
    public void ReorderRejectsAnOrderWithoutEachApplicationProfileOnce()
    {
        var store = Store("A", "B");
        var app = store.ApplicationProfiles;

        Assert.Throws<ArgumentException>(() => store.Reorder([app[0]]));
        Assert.Throws<ArgumentException>(() => store.Reorder([app[0], app[0]]));
        Assert.Throws<ArgumentException>(() => store.Reorder([app[0], new ProfileListItem("B")]));
        Assert.Throws<ArgumentException>(() => store.Reorder([app[0], app[1], store.Windows!]));
        Assert.Equal(["Windows", "A", "B"], Names(store));
    }
}
