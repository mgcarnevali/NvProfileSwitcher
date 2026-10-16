using Xunit;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.Core.Tests;

public sealed class SavedDisplaysCoreTests
{
    private const string Dell = "DEL4321-ABC123";
    private const string Lg = "GSM5B7F-305NTAB1C2D3";
    private const string Old = "SAM0F00-OLD";

    private static ProfileDisplaySettings Values(string? monitorId, string displayName) =>
        new() { MonitorId = monitorId!, DisplayName = displayName };

    private static ProfileListItem Profile(string name, params ProfileDisplaySettings[] values)
    {
        var profile = new ProfileListItem(name, isWindowsProfile: name == "Windows");
        profile.DisplayProfiles.AddRange(values);
        return profile;
    }

    // Connected displays first, in display order and named by their label; then the
    // disconnected ones in the order they were found.
    [Fact]
    public void ConnectedDisplaysComeFirstWithTheirLabels()
    {
        var windows = Profile("Windows", Values(Old, "Old monitor"), Values(Dell, "DELL"));
        var game = Profile("Game", Values(Lg, "LG"), Values(Dell, "DELL"));

        var saved = SavedDisplaysCore.Build([windows, game], [Display(Lg, "LG ULTRAGEAR"), Display(Dell, "DELL U2720Q", 2)]);

        Assert.Equal([Lg, Dell, Old], saved.Select(d => d.MonitorId));
        Assert.Equal(["LG ULTRAGEAR (Primary)", "DELL U2720Q", "Old monitor"], saved.Select(d => d.Name));
        Assert.Equal([true, true, false], saved.Select(d => d.Connected));
    }

    // Monitor ids match as the rest of the application does, so each display is listed once,
    // and entries without a monitor id are skipped.
    [Fact]
    public void EachDisplayIsListedOnceAndEmptyIdsAreSkipped()
    {
        var windows = Profile("Windows", Values(Old, "Old monitor"), Values("", "No id"), Values(null, "Null id"));
        var game = Profile("Game", Values(Old.ToLowerInvariant(), "Other name"));

        var saved = SavedDisplaysCore.Build([windows, game], []);

        var display = Assert.Single(saved);
        Assert.Equal(Old, display.MonitorId);
        Assert.Equal("Old monitor", display.Name);
    }

    // A name that is only the monitor id takes the first display name found later.
    [Fact]
    public void AMonitorIdNameTakesTheFirstDisplayNameFound()
    {
        var windows = Profile("Windows", Values(Old, Old));
        var game = Profile("Game", Values(Old, ""), Values(Old, "Samsung"));
        var notepad = Profile("Notepad", Values(Old, "Later name"));

        var saved = SavedDisplaysCore.Build([windows, game, notepad], []);

        Assert.Equal("Samsung", Assert.Single(saved).Name);
    }

    [Fact]
    public void RemoveTakesTheDisplayOutOfEveryProfile()
    {
        var windows = Profile("Windows", Values(Old, "Old"), Values(Dell, "DELL"));
        var game = Profile("Game", Values(Old.ToLowerInvariant(), "Old"), Values(Old, "Old again"), Values(Dell, "DELL"));

        SavedDisplaysCore.Remove([windows, game], Old);

        Assert.Equal([Dell], windows.DisplayProfiles.Select(v => v.MonitorId));
        Assert.Equal([Dell], game.DisplayProfiles.Select(v => v.MonitorId));
    }
}
