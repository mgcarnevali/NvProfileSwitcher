using System.Globalization;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// The order Sort by Name gives the application profiles: by name, ignoring case, with equal
// names in their order, and the unsaved new profile last.
public sealed class ProfileOrderCoreTests
{
    private static List<ProfileListItem> Profiles(params string[] names) => names.Select(name => new ProfileListItem(name)).ToList();

    private static List<string> Names(IEnumerable<ProfileListItem> profiles) => profiles.Select(p => p.Name).ToList();

    [Fact]
    public void AToZIgnoresCase() =>
        Assert.Equal(["Alpha", "beta", "Gamma"], Names(ProfileOrderCore.ByName(Profiles("Gamma", "beta", "Alpha"), null, descending: false)));

    [Fact]
    public void ZToAIgnoresCase() =>
        Assert.Equal(["Gamma", "beta", "Alpha"], Names(ProfileOrderCore.ByName(Profiles("beta", "Alpha", "Gamma"), null, descending: true)));

    // Accented names sort with their letter, as the culture orders them, not after Z.
    [Fact]
    public void AccentedNamesFollowTheCulture()
    {
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), ignoreCase: true);

        var order = ProfileOrderCore.ByName(Profiles("Zeta", "Árbol", "Beta"), null, descending: false, comparer);

        Assert.Equal(["Árbol", "Beta", "Zeta"], Names(order));
    }

    // A stable sort: profiles with the same name keep their order, both ways.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualNamesKeepTheirOrder(bool descending)
    {
        var profiles = Profiles("Game", "game", "Editor");

        var order = ProfileOrderCore.ByName(profiles, null, descending);

        var games = order.Where(p => p.Name.Equals("game", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Same(profiles[0], games[0]);
        Assert.Same(profiles[1], games[1]);
    }

    // The unsaved new profile takes no part in the sort, wherever it was: it goes last.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheUnsavedProfileGoesLast(bool descending)
    {
        var profiles = Profiles("Game", "AAA New", "Editor");

        var order = ProfileOrderCore.ByName(profiles, profiles[1], descending);

        Assert.Equal(descending ? ["Game", "Editor", "AAA New"] : ["Editor", "Game", "AAA New"], Names(order));
        Assert.Same(profiles[1], order[^1]);
    }

    [Fact]
    public void AnUnsavedProfileOutsideTheListIsNotAdded()
    {
        var order = ProfileOrderCore.ByName(Profiles("B", "A"), new ProfileListItem("New Profile"), descending: false);

        Assert.Equal(["A", "B"], Names(order));
    }

    [Fact]
    public void NoProfilesGiveNoOrder() =>
        Assert.Empty(ProfileOrderCore.ByName([], null, descending: false));
}
