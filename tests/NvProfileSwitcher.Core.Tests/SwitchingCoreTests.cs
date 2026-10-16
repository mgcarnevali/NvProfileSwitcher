using Xunit;
namespace NvProfileSwitcher.Core.Tests;

public sealed class SwitchingCoreTests
{
    private static List<SwitchingCandidate> Profiles() =>
    [
        new("Escape From Tarkov", @"D:\Games\EscapeFromTarkov.exe", true),
        new("Tarkov Arena", @"D:\Games\EscapeFromTarkovArena.EXE", true),
        new("Disabled", @"C:\Tools\Disabled.exe", false),
        new("No executable", "", true)
    ];

    [Fact]
    public void AutomaticSelectsEnabledMatchingProfileCaseInsensitively()
    {
        var target = SwitchingCore.SelectTarget(Profiles(), "ESCAPEFROMTARKOV.exe", OverrideMode.Automatic, -1);
        Assert.Equal(0, target.ProfileIndex);
        Assert.Equal("Escape From Tarkov", target.ActiveName);
    }

    [Fact]
    public void AutomaticIgnoresDisabledAndFallsBackToWindows()
    {
        var disabled = SwitchingCore.SelectTarget(Profiles(), "Disabled.exe", OverrideMode.Automatic, -1);
        var unknown = SwitchingCore.SelectTarget(Profiles(), "Unknown.exe", OverrideMode.Automatic, -1);
        var missing = SwitchingCore.SelectTarget(Profiles(), "", OverrideMode.Automatic, -1);
        Assert.Equal("Windows", disabled.ActiveName);
        Assert.Equal("Windows", unknown.ActiveName);
        Assert.Equal("Windows", missing.ActiveName);
    }

    [Fact]
    public void AutomaticPreservesFirstMatchPrecedence()
    {
        var profiles = Profiles();
        profiles.Insert(0, new("First match", @"E:\Duplicate\EscapeFromTarkov.exe", true));
        var target = SwitchingCore.SelectTarget(profiles, "EscapeFromTarkov.exe", OverrideMode.Automatic, -1);
        Assert.Equal(0, target.ProfileIndex);
        Assert.Equal("First match", target.ActiveName);
    }

    [Fact]
    public void WindowsOverrideAlwaysSelectsWindows()
    {
        var target = SwitchingCore.SelectTarget(Profiles(), "EscapeFromTarkov.exe", OverrideMode.Windows, -1);
        Assert.Equal(OverrideMode.Windows, target.Mode);
        Assert.Equal(-1, target.ProfileIndex);
        Assert.Equal("Windows", target.ActiveName);
    }

    [Fact]
    public void ProfileOverrideSelectsRequestedEnabledProfile()
    {
        var target = SwitchingCore.SelectTarget(Profiles(), "", OverrideMode.Profile, 1);
        Assert.Equal(OverrideMode.Profile, target.Mode);
        Assert.Equal(1, target.ProfileIndex);
        Assert.Equal("Tarkov Arena", target.ActiveName);
    }

    [Fact]
    public void InvalidOrDisabledProfileOverrideReturnsToAutomatic()
    {
        var disabled = SwitchingCore.SelectTarget(Profiles(), "EscapeFromTarkov.exe", OverrideMode.Profile, 2);
        var removed = SwitchingCore.SelectTarget(Profiles(), "Unknown.exe", OverrideMode.Profile, 99);
        Assert.Equal(OverrideMode.Automatic, disabled.Mode);
        Assert.Equal("Escape From Tarkov", disabled.ActiveName);
        Assert.Equal(OverrideMode.Automatic, removed.Mode);
        Assert.Equal("Windows", removed.ActiveName);
    }
    [Fact]
    public void AutomaticMatchesAnyExecutableAssignedToProfile()
    {
        var profiles = new List<SwitchingCandidate>
        {
            new("Multi", new[] { @"C:\Games\Game.exe", @"C:\Games\Launcher.exe" }, true)
        };

        var game = SwitchingCore.SelectTarget(profiles, "Game.exe", OverrideMode.Automatic, -1);
        var launcher = SwitchingCore.SelectTarget(profiles, "LAUNCHER.EXE", OverrideMode.Automatic, -1);

        Assert.Equal("Multi", game.ActiveName);
        Assert.Equal("Multi", launcher.ActiveName);
    }

}
