using NvProfileSwitcher.Models;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

public sealed class ActiveProfileCoreTests
{
    private static ProfileListItem Profile(string name, bool enabled, params string[] executables)
    {
        var profile = new ProfileListItem(name) { Enabled = enabled };
        profile.SetExecutables(executables);
        return profile;
    }

    // File names only: matching full paths is covered by SwitchingCoreTests.
    private static readonly IReadOnlyList<SwitchingCandidate> Candidates = ActiveProfileCore.Candidates(
    [
        Profile("Tarkov", enabled: false, "EscapeFromTarkov.exe"),
        Profile("Game", enabled: true, "game.exe", "launcher.exe"),
        Profile("Editor", enabled: true),
    ]);

    [Fact]
    public void CandidatesKeepExecutablesAndUseTheNameWithoutThem()
    {
        Assert.Equal(["Tarkov", "Game", "Editor"], Candidates.Select(c => c.Name));
        Assert.Equal(["game.exe", "launcher.exe"], Candidates[1].ExecutablePaths);
        Assert.Equal(["Editor"], Candidates[2].ExecutablePaths);
        Assert.False(Candidates[0].Enabled);
    }

    // The automatic target is the one switching selects: the first enabled match.
    [Theory]
    [InlineData("launcher.exe", 1)]
    [InlineData("EDITOR", 2)]
    [InlineData("EscapeFromTarkov.exe", -1)]
    [InlineData("explorer.exe", -1)]
    [InlineData("", -1)]
    public void AutomaticTargetIsTheFirstEnabledMatch(string foreground, int expected) =>
        Assert.Equal(expected, ActiveProfileCore.RealColorsTarget(Candidates, () => foreground, OverrideMode.Automatic, -1));

    // A profile override with a valid index applies that profile even when it is disabled.
    [Fact]
    public void ValidProfileOverrideIsTheTargetEvenWhenDisabled() =>
        Assert.Equal(0, ActiveProfileCore.RealColorsTarget(Candidates, () => "game.exe", OverrideMode.Profile, 0));

    // Unlike switching, an invalid profile override applies Windows instead of falling back
    // to the automatic match.
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void InvalidProfileOverrideTargetsWindows(int index)
    {
        Assert.Equal(-1, ActiveProfileCore.RealColorsTarget(Candidates, () => "game.exe", OverrideMode.Profile, index));
        Assert.Equal(1, SwitchingCore.SelectTarget(Candidates, "game.exe", OverrideMode.Profile, index).ProfileIndex);
    }

    // As in the original, the foreground application is read only in Automatic mode.
    [Theory]
    [InlineData("Automatic", 0, 1)]
    [InlineData("Profile", 1, 0)]
    [InlineData("Profile", 7, 0)]
    [InlineData("Windows", -1, 0)]
    public void ForegroundIsReadOnlyInAutomaticMode(string mode, int overrideProfileIndex, int expectedReads)
    {
        var reads = 0;

        ActiveProfileCore.RealColorsTarget(
            Candidates, () => { reads++; return "game.exe"; }, Enum.Parse<OverrideMode>(mode), overrideProfileIndex);

        Assert.Equal(expectedReads, reads);
    }

    [Fact]
    public void WindowsOverrideTargetsWindows() =>
        Assert.Equal(-1, ActiveProfileCore.RealColorsTarget(Candidates, () => "game.exe", OverrideMode.Windows, 1));

    [Theory]
    [InlineData("Game", "Automatic", false, "Game")]
    [InlineData("", "Automatic", false, "Windows")]
    [InlineData("Windows", "Windows", false, "Windows (override)")]
    [InlineData("Game", "Profile", false, "Game (override)")]
    [InlineData("", "Profile", false, "Windows (override)")]
    // Unlike the original footer, a paused state shows only "Windows" (deviation 3.0-1).
    [InlineData("Game", "Profile", true, "Windows")]
    public void StatusTextMatchesTheOriginalFooter(string name, string mode, bool paused, string expected) =>
        Assert.Equal(expected, ActiveProfileCore.StatusText(new ActiveProfileDisplay(name, Enum.Parse<OverrideMode>(mode), paused)));

    [Fact]
    public void InitialStatusTextIsWindows() =>
        Assert.Equal("Windows", ActiveProfileCore.StatusText(ActiveProfileDisplay.Initial));
}
