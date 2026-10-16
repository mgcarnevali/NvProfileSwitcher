using System.Text.Json.Nodes;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using Xunit;
using static NvProfileSwitcher.Core.Tests.ConfigFixtures;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.Core.Tests;

public sealed class DisplayProfileSyncServiceTests
{
    private const string Dell = "DEL4321-ABC123";
    private const string Lg = "GSM5B7F-305NTAB1C2D3";
    private const string Samsung = "SAM0F12-H4ZN901234";

    private static (NvidiaService Nvidia, DisplayProfileSyncService Sync) Create(ConfigSession session, Action<FakeDisplayBackend> configure)
    {
        var factory = new FakeBackendFactory { Configure = configure };
        var nvidia = new NvidiaService(factory.Create);
        nvidia.Initialize();
        nvidia.RefreshDisplays();
        return (nvidia, new DisplayProfileSyncService(session.Profiles, nvidia, new DisplayColorController(nvidia, session.Profiles, new SwitchingState()), session.Service));
    }

    // Removes every display entry so the remaining configuration can be compared.
    private static string WithoutDisplays(string json)
    {
        var node = Node(json);
        node.Remove("Windows Profiles");
        foreach (var profile in node["Application Profiles"]!.AsArray())
            profile!.AsObject().Remove("Display Profiles");
        return node.ToJsonString();
    }

    // The real display synchronization followed by its save changes display entries only:
    // profiles, executables, hotkeys, settings and switching state stay as they were.
    [Fact]
    public void SynchronizationChangesOnlyDisplayEntries()
    {
        using var session = new ConfigSession(Canonical);
        session.Service.Load();
        var (nvidia, sync) = Create(session, backend =>
        {
            backend.Displays.Add(Display(Dell, "DELL U2720QM", 1));
            backend.Displays.Add(Display(Lg, "LG ULTRAGEAR", 2));
            backend.Displays.Add(Display(Samsung, "SAMSUNG Odyssey", 3));
            backend.Vibrance[Samsung] = 62;
            backend.Hue[Samsung] = 4;
        });
        using (nvidia)
            sync.Synchronize();

        var saved = File.ReadAllText(session.ConfigPath);
        Assert.Empty(Differences(WithoutDisplays(Canonical), WithoutDisplays(saved)));

        var node = Node(saved);
        var windows = node["Windows Profiles"]!.AsArray();
        Assert.Equal(3, windows.Count);
        Assert.Equal("DELL U2720QM", windows[0]!["DisplayName"]!.GetValue<string>());
        Assert.Equal("Display 3", windows[2]!["Name"]!.GetValue<string>());
        Assert.Equal((62, 4), (windows[2]!["DigitalVibrance"]!.GetValue<int>(), windows[2]!["Hue"]!.GetValue<int>()));

        var game = node["Application Profiles"]![0]!["Display Profiles"]!.AsArray();
        Assert.Equal(3, game.Count);
        Assert.Equal("DELL U2720QM", game[0]!["DisplayName"]!.GetValue<string>());
        Assert.Equal(80, game[0]!["DigitalVibrance"]!.GetValue<int>());
        Assert.Equal(62, game[2]!["DigitalVibrance"]!.GetValue<int>());

        Assert.Equal(3, node["Application Profiles"]![1]!["Display Profiles"]!.AsArray().Count);
    }

    [Fact]
    public void SynchronizationWithoutChangesDoesNotWrite()
    {
        using var session = new ConfigSession(Canonical);
        session.Service.Load();
        var original = File.ReadAllBytes(session.ConfigPath);
        var (nvidia, sync) = Create(session, backend =>
        {
            backend.Displays.Add(Display(Dell, "DELL U2720Q", 1));
            backend.Displays.Add(Display(Lg, "LG ULTRAGEAR", 2));
        });

        // The canonical Tarkov profile has no display values, so the first pass adds them.
        using (nvidia)
        {
            sync.Synchronize();
            var afterFirstPass = File.ReadAllBytes(session.ConfigPath);
            var firstPassWrite = File.GetLastWriteTimeUtc(session.ConfigPath);
            Assert.NotEqual(original, afterFirstPass);

            sync.Synchronize();
            Assert.Equal(afterFirstPass, File.ReadAllBytes(session.ConfigPath));
            Assert.Equal(firstPassWrite, File.GetLastWriteTimeUtc(session.ConfigPath));
        }
    }

    [Fact]
    public void FirstSynchronizationWithoutConfigurationCreatesIt()
    {
        using var session = new ConfigSession();
        session.Service.Load();
        var (nvidia, sync) = Create(session, backend => backend.Displays.Add(Display(Dell, "DELL U2720Q", 1)));

        using (nvidia)
            sync.Synchronize();

        var saved = Node(File.ReadAllText(session.ConfigPath));
        Assert.Single(saved["Windows Profiles"]!.AsArray());
        Assert.Empty(saved["Application Profiles"]!.AsArray());
        Assert.False(saved["CheckForUpdates"]!.GetValue<bool>());
    }

    // Two connected monitors reporting the same serial number: the identity is recorded as
    // shared in the configuration, so after a restart either monitor alone keeps its
    // connection identity and its own values.
    [Fact]
    public void ASharedSerialIsSavedAndKeptAcrossSessions()
    {
        const string twin = "SAM0F12-1";
        using var session = new ConfigSession(Canonical);
        session.Service.Load();
        var factory = new FakeBackendFactory
        {
            Configure = backend =>
            {
                backend.Displays.Add(Display(twin, "Samsung 1", 1));
                backend.Displays.Add(Display(twin, "Samsung 2", 2));
            }
        };
        using var nvidia = new NvidiaService(factory.Create);
        nvidia.Initialize();
        var sync = new DisplayProfileSyncService(session.Profiles, nvidia, new DisplayColorController(nvidia, session.Profiles, new SwitchingState()), session.Service);

        sync.Synchronize(nvidia.RefreshDisplays(session.Profiles.NonUniqueMonitorIds));

        var saved = Node(File.ReadAllText(session.ConfigPath));
        Assert.Equal([twin], saved["Non-unique Monitor Ids"]!.AsArray().Select(id => id!.GetValue<string>()));
        Assert.Equal([$"SAM0F12@{Instance(1)}", $"SAM0F12@{Instance(2)}"], nvidia.Displays.Select(d => d.MonitorId));

        using var restarted = new ConfigSession(File.ReadAllText(session.ConfigPath));
        restarted.Service.Load();
        factory.Configure = backend => backend.Displays.Add(Display(twin, "Samsung 2", 2));
        nvidia.Initialize();
        nvidia.RefreshDisplays(restarted.Profiles.NonUniqueMonitorIds);
        Assert.Equal($"SAM0F12@{Instance(2)}", Assert.Single(nvidia.Displays).MonitorId);
    }

    // Configurations without shared identities are written as before, without the entry.
    [Fact]
    public void ConfigurationsWithoutSharedSerialsHaveNoEntry()
    {
        using var session = new ConfigSession(Canonical);
        session.Service.Load();
        var (nvidia, sync) = Create(session, backend => backend.Displays.Add(Display(Dell, "DELL U2720Q", 1)));

        using (nvidia)
            sync.Synchronize();

        Assert.Null(Node(File.ReadAllText(session.ConfigPath))["Non-unique Monitor Ids"]);
    }
}
