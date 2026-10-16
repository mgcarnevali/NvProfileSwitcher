using NvProfileSwitcher.Models;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

public sealed class OverrideCoreTests
{
    private static readonly Guid FirstId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SecondId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid DisabledId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private static List<ProfileListItem> ApplicationProfiles() =>
    [
        new("First", id: FirstId) { Enabled = true },
        new("Disabled", id: DisabledId) { Enabled = false },
        new("Second", id: SecondId) { Enabled = true }
    ];

    private static AppConfig Config(OverrideMode mode, Guid? profileId = null) =>
        new() { OverrideMode = mode, OverrideProfileId = profileId };

    [Fact]
    public void WindowsOverrideIsNotRestoredWhenItIsNotRemembered() =>
        Assert.Equal((OverrideMode.Automatic, -1),
            OverrideCore.Restore(Config(OverrideMode.Windows), rememberOverride: false, ApplicationProfiles()));

    [Fact]
    public void ProfileOverrideIsNotRestoredWhenItIsNotRemembered() =>
        Assert.Equal((OverrideMode.Automatic, -1),
            OverrideCore.Restore(Config(OverrideMode.Profile, FirstId), rememberOverride: false, ApplicationProfiles()));

    [Fact]
    public void WindowsOverrideIsRestored() =>
        Assert.Equal((OverrideMode.Windows, -1),
            OverrideCore.Restore(Config(OverrideMode.Windows), rememberOverride: true, ApplicationProfiles()));

    [Fact]
    public void ProfileOverrideIsRestoredWithItsIndexAmongApplicationProfiles() =>
        Assert.Equal((OverrideMode.Profile, 2),
            OverrideCore.Restore(Config(OverrideMode.Profile, SecondId), rememberOverride: true, ApplicationProfiles()));

    [Fact]
    public void DisabledProfileOverrideIsNotRestored() =>
        Assert.Equal((OverrideMode.Automatic, -1),
            OverrideCore.Restore(Config(OverrideMode.Profile, DisabledId), rememberOverride: true, ApplicationProfiles()));

    [Fact]
    public void UnknownProfileOverrideIsNotRestored() =>
        Assert.Equal((OverrideMode.Automatic, -1),
            OverrideCore.Restore(Config(OverrideMode.Profile, Guid.NewGuid()), rememberOverride: true, ApplicationProfiles()));

    [Fact]
    public void ProfileOverrideWithoutIdIsNotRestored() =>
        Assert.Equal((OverrideMode.Automatic, -1),
            OverrideCore.Restore(Config(OverrideMode.Profile), rememberOverride: true, ApplicationProfiles()));

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void GlobalSwitchingIsPausedOnlyWhenRememberedAsDisabled(bool remember, bool enabled, bool paused)
    {
        var config = new AppConfig { RememberGlobalSwitching = remember, GlobalSwitchingEnabled = enabled };
        var settings = new AppSettings();
        var switching = new SwitchingState();

        ConfigMappingCore.ApplyState(config, [], isDevelopmentBuild: false, settings, switching);

        Assert.Equal(paused, switching.GlobalPaused);
    }

    [Fact]
    public void WindowsOverrideTogglesWithAutomatic()
    {
        Assert.Equal(OverrideMode.Windows, OverrideCore.ToggleWindows(OverrideMode.Automatic));
        Assert.Equal(OverrideMode.Automatic, OverrideCore.ToggleWindows(OverrideMode.Windows));
        Assert.Equal(OverrideMode.Windows, OverrideCore.ToggleWindows(OverrideMode.Profile));
    }

    private static readonly IReadOnlyList<ProfileListItem> ToggleProfiles =
    [
        new ProfileListItem("Game") { Enabled = true },
        new ProfileListItem("Tarkov") { Enabled = false },
        new ProfileListItem("Editor") { Enabled = true },
    ];

    [Fact]
    public void ProfileOverrideSelectsAnEnabledProfile()
    {
        Assert.Equal((OverrideMode.Profile, 2), OverrideCore.ToggleProfile(OverrideMode.Automatic, -1, 2, ToggleProfiles));
        Assert.Equal((OverrideMode.Profile, 0), OverrideCore.ToggleProfile(OverrideMode.Profile, 2, 0, ToggleProfiles));
        Assert.Equal((OverrideMode.Profile, 0), OverrideCore.ToggleProfile(OverrideMode.Windows, 0, 0, ToggleProfiles));
    }

    [Fact]
    public void SelectingTheCurrentProfileOverrideReturnsToAutomatic() =>
        Assert.Equal((OverrideMode.Automatic, -1), OverrideCore.ToggleProfile(OverrideMode.Profile, 2, 2, ToggleProfiles));

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(1)]
    public void ProfileOverrideIgnoresMissingOrDisabledProfiles(int index) =>
        Assert.Null(OverrideCore.ToggleProfile(OverrideMode.Automatic, -1, index, ToggleProfiles));

    // Moving swaps two adjacent application profiles; a profile override follows its profile.
    [Theory]
    [InlineData((int)OverrideMode.Profile, 1, 1, 2, 2)]
    [InlineData((int)OverrideMode.Profile, 2, 1, 2, 1)]
    [InlineData((int)OverrideMode.Profile, 0, 1, 2, 0)]
    [InlineData((int)OverrideMode.Automatic, 1, 1, 2, 1)]
    [InlineData((int)OverrideMode.Windows, 2, 1, 2, 2)]
    public void MovingKeepsTheProfileOverrideOnItsProfile(int mode, int index, int from, int to, int expected) =>
        Assert.Equal(expected, OverrideCore.AfterMove((OverrideMode)mode, index, from, to));

    // As in the original, removing the override profile returns to Automatic but keeps the
    // index until switching reevaluates it.
    [Theory]
    [InlineData((int)OverrideMode.Profile, 1, 1, (int)OverrideMode.Automatic, 1)]
    [InlineData((int)OverrideMode.Profile, 2, 1, (int)OverrideMode.Profile, 1)]
    [InlineData((int)OverrideMode.Profile, 0, 1, (int)OverrideMode.Profile, 0)]
    [InlineData((int)OverrideMode.Windows, 3, 1, (int)OverrideMode.Windows, 3)]
    [InlineData((int)OverrideMode.Automatic, -1, 0, (int)OverrideMode.Automatic, -1)]
    public void RemovingAdjustsTheProfileOverride(int mode, int index, int removed, int expectedMode, int expectedIndex) =>
        Assert.Equal(((OverrideMode)expectedMode, expectedIndex), OverrideCore.AfterRemove((OverrideMode)mode, index, removed));
}
