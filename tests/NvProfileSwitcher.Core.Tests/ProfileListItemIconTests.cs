using System.Reflection;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// The executable icon is loaded only when a list row reads it: copies of a profile, such as the
// editor's drafts, never load one.
public sealed class ProfileListItemIconTests
{
    private static bool IconLoaded(ProfileListItem profile) =>
        (bool)typeof(ProfileListItem).GetField("iconLoaded", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(profile)!;

    [Fact]
    public void TheIconIsLoadedWhenFirstRead()
    {
        var profile = new ProfileListItem("Game");
        profile.SetExecutables(["game.exe"]);
        var draft = ProfileEditingCore.CloneProfile(profile);

        Assert.False(IconLoaded(profile));
        Assert.False(IconLoaded(draft));
        _ = profile.HasApplicationIcon;
        Assert.True(IconLoaded(profile));
        Assert.False(IconLoaded(draft));
    }

    [Fact]
    public void ChangingTheExecutablesReleasesALoadedIconAndNotifiesIt()
    {
        var profile = new ProfileListItem("Game");
        profile.SetExecutables(["game.exe"]);
        _ = profile.Icon;
        var changed = new List<string?>();
        profile.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        profile.SetExecutables(["other.exe"]);

        Assert.False(IconLoaded(profile));
        Assert.Contains(nameof(ProfileListItem.Icon), changed);
        Assert.Contains(nameof(ProfileListItem.ShowFallbackIcon), changed);
    }

    [Fact]
    public void AnIconThatWasNeverReadIsNotNotified()
    {
        var profile = new ProfileListItem("Game");
        var changed = new List<string?>();
        profile.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        profile.SetExecutables(["game.exe"]);

        Assert.Equal([nameof(ProfileListItem.ExecutablePaths)], changed);
    }
}
