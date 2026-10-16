using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using Xunit;
using static NvProfileSwitcher.Core.Tests.ConfigFixtures;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;
using static NvProfileSwitcher.Core.Tests.NvTweakColorCoreTests;

namespace NvProfileSwitcher.Core.Tests;

// The display synchronization starts the brightness, contrast and gamma of a display new to the
// Windows profile from its NVIDIA Control Panel settings: the device id the API reports for the
// display names its registry key. Known displays are never read again.
public sealed class NvTweakInitializationTests
{
    private const string Asus1 = "AUS27A1-S1";
    private const string Asus2 = "AUS27A1-S2";
    private const string Portable = "AUS16B2-S3";
    private const uint Device1 = 3107697358, Device2 = 3107697359, Device3 = 3107697360;

    private const string EmptyConfig = """{ "Windows Profiles": [], "Application Profiles": [] }""";

    // The registry seen by the synchronization, by device id, and the ids it read.
    private sealed class Registry
    {
        public Dictionary<uint, IReadOnlyList<NvTweakValue?>> Keys { get; } = [];
        public List<uint> Reads { get; } = [];

        public IReadOnlyList<NvTweakValue?>? Read(uint deviceId)
        {
            Reads.Add(deviceId);
            return Keys.GetValueOrDefault(deviceId);
        }
    }

    private static void ThreeMonitors(FakeDisplayBackend backend)
    {
        backend.Displays.Add(Display(Asus1, "ASUS VG27AQL3A", 1));
        backend.Displays.Add(Display(Asus2, "ASUS VG27AQL3A", 2));
        backend.Displays.Add(Display(Portable, "ASUS MB16NCG", 3));
        backend.NvTweakDeviceIds[Asus1] = Device1;
        backend.NvTweakDeviceIds[Asus2] = Device2;
        backend.NvTweakDeviceIds[Portable] = Device3;
        foreach (var display in backend.Displays)
            backend.Vibrance[display.MonitorId] = 60;
    }

    private static Registry ThreeSettings() => new()
    {
        Keys =
        {
            [Device1] = Values(105, 110, 130),
            [Device2] = Values(110, 105, 120),
            [Device3] = Values(95, 95, 110)
        }
    };

    private sealed class Setup : IDisposable
    {
        public Setup(ConfigSession session, Registry registry, Action<FakeDisplayBackend> configure)
        {
            Session = session;
            Factory = new FakeBackendFactory { Configure = configure };
            Nvidia = new NvidiaService(Factory.Create);
            Nvidia.Initialize();
            Nvidia.RefreshDisplays();
            Colors = new DisplayColorController(Nvidia, session.Profiles, new SwitchingState());
            Sync = new DisplayProfileSyncService(session.Profiles, Nvidia, Colors, session.Service, registry.Read);
        }

        public ConfigSession Session { get; }
        public FakeBackendFactory Factory { get; }
        public NvidiaService Nvidia { get; }
        public DisplayColorController Colors { get; }
        public DisplayProfileSyncService Sync { get; }

        public void Dispose() => Nvidia.Dispose();
    }

    private static (double, double, double, int, int) Windows(ConfigSession session, string monitorId)
    {
        var values = DisplayCore.FindValues(session.Profiles.Profiles[0], monitorId)!;
        return (values.Brightness, values.Contrast, values.Gamma, values.DigitalVibrance, values.Hue);
    }

    [Fact]
    public void ThreeNewMonitorsStartFromTheirOwnSettingsAndTheyAreSaved()
    {
        using var session = new ConfigSession(EmptyConfig);
        session.Service.Load();
        var registry = ThreeSettings();
        using (var setup = new Setup(session, registry, ThreeMonitors))
            setup.Sync.Synchronize();

        Assert.Equal((105d, 110d, 1.3, 60, 0), Windows(session, Asus1));
        Assert.Equal((110d, 105d, 1.2, 60, 0), Windows(session, Asus2));
        Assert.Equal((95d, 95d, 1.1, 60, 0), Windows(session, Portable));
        Assert.Equal([Device1, Device2, Device3], registry.Reads);

        var saved = Node(File.ReadAllText(session.ConfigPath))["Windows Profiles"]!.AsArray();
        Assert.Equal([105d, 110d, 95d], saved.Select(v => v!["Brightness"]!.GetValue<double>()));
        Assert.Equal([110d, 105d, 95d], saved.Select(v => v!["Contrast"]!.GetValue<double>()));
        Assert.Equal([1.3, 1.2, 1.1], saved.Select(v => v!["Gamma"]!.GetValue<double>()));
    }

    // After a restart the saved values are used: the settings are not read again, even when
    // they changed in the NVIDIA Control Panel.
    [Fact]
    public void SavedMonitorsAreNotReadAgainAfterARestart()
    {
        using var first = new ConfigSession(EmptyConfig);
        first.Service.Load();
        using (var setup = new Setup(first, ThreeSettings(), ThreeMonitors))
            setup.Sync.Synchronize();

        using var restarted = new ConfigSession(File.ReadAllText(first.ConfigPath));
        restarted.Service.Load();
        var changed = new Registry
        {
            Keys = { [Device1] = Values(80, 80, 30), [Device2] = Values(80, 80, 30), [Device3] = Values(80, 80, 30) }
        };
        using (var setup = new Setup(restarted, changed, ThreeMonitors))
            setup.Sync.Synchronize();

        Assert.Empty(changed.Reads);
        Assert.Equal((105d, 110d, 1.3, 60, 0), Windows(restarted, Asus1));
        Assert.Equal((110d, 105d, 1.2, 60, 0), Windows(restarted, Asus2));
        Assert.Equal((95d, 95d, 1.1, 60, 0), Windows(restarted, Portable));
    }

    // Within a session too: a monitor that reconnects, or a refresh after a display change or a
    // resume, keeps the values it has.
    [Fact]
    public void AKnownMonitorIsNotReadOnLaterRefreshes()
    {
        using var session = new ConfigSession(EmptyConfig);
        session.Service.Load();
        var registry = ThreeSettings();
        using var setup = new Setup(session, registry, ThreeMonitors);
        setup.Sync.Synchronize();
        registry.Reads.Clear();
        registry.Keys[Device1] = Values(120, 120, 280);

        setup.Nvidia.RefreshDisplays();
        setup.Sync.Synchronize();

        Assert.Empty(registry.Reads);
        Assert.Equal((105d, 110d, 1.3, 60, 0), Windows(session, Asus1));
    }

    [Fact]
    public void ExistingMonitorsKeepTheirValuesAndOnlyTheNewOneIsRead()
    {
        using var session = new ConfigSession("""
            {
              "Windows Profiles": [
                { "Name": "Display 1", "DisplayName": "ASUS VG27AQL3A", "MonitorId": "AUS27A1-S1", "Brightness": 98, "Contrast": 102, "Gamma": 0.95, "DigitalVibrance": 55, "Hue": 3 }
              ],
              "Application Profiles": [
                { "Id": "11111111-1111-1111-1111-111111111111", "Name": "Game", "Exe Paths": ["game.exe"], "Enabled": true, "Hotkey": 0,
                  "Display Profiles": [
                    { "DisplayName": "ASUS VG27AQL3A", "MonitorId": "AUS27A1-S1", "Brightness": 90, "Contrast": 95, "Gamma": 0.9, "DigitalVibrance": 80, "Hue": 5 }
                  ] }
              ]
            }
            """);
        session.Service.Load();
        var registry = ThreeSettings();
        using (var setup = new Setup(session, registry, ThreeMonitors))
            setup.Sync.Synchronize();

        Assert.Equal([Device2, Device3], registry.Reads);
        Assert.Equal((98d, 102d, 0.95, 55, 3), Windows(session, Asus1));
        Assert.Equal((110d, 105d, 1.2, 60, 0), Windows(session, Asus2));

        var game = session.Profiles.ApplicationProfiles[0];
        var gameValues = DisplayCore.FindValues(game, Asus1)!;
        Assert.Equal((90d, 95d, 0.9, 80, 5), (gameValues.Brightness, gameValues.Contrast, gameValues.Gamma, gameValues.DigitalVibrance, gameValues.Hue));
        // A display new to the application profile takes the Windows values, as before.
        Assert.Equal(110d, DisplayCore.FindValues(game, Asus2)!.Brightness);
    }

    // Any setting that cannot be used leaves the display neutral: brightness 100, contrast 100,
    // gamma 1.00. Vibrance and hue still come from the API.
    public static TheoryData<string> Unusable => new()
    {
        "no key", "missing value", "string value", "out of range", "channels differ", "no device id"
    };

    [Theory]
    [MemberData(nameof(Unusable))]
    public void UnusableSettingsLeaveTheDisplayNeutral(string reason)
    {
        using var session = new ConfigSession(EmptyConfig);
        session.Service.Load();
        var registry = ThreeSettings();
        var values = Values(105, 110, 130);
        switch (reason)
        {
            case "no key": registry.Keys.Remove(Device1); break;
            case "missing value": values[4] = null; registry.Keys[Device1] = values; break;
            case "string value": values[7] = new NvTweakValue(Microsoft.Win32.RegistryValueKind.String, "130"); registry.Keys[Device1] = values; break;
            case "out of range": registry.Keys[Device1] = Values(105, 110, 290); break;
            case "channels differ": values[0] = Dword(104); registry.Keys[Device1] = values; break;
        }
        using (var setup = new Setup(session, registry, backend =>
               {
                   ThreeMonitors(backend);
                   if (reason == "no device id") backend.NvTweakDeviceIds[Asus1] = null;
               }))
            setup.Sync.Synchronize();

        Assert.Equal((100d, 100d, 1d, 60, 0), Windows(session, Asus1));
        Assert.Equal((110d, 105d, 1.2, 60, 0), Windows(session, Asus2));
        Assert.Equal((95d, 95d, 1.1, 60, 0), Windows(session, Portable));
    }

    // A display this application already wrote in the session is not read, as vibrance and hue:
    // only settings from before this application changed the display can start its values.
    [Fact]
    public void ADisplayWrittenInThisSessionIsNotRead()
    {
        using var session = new ConfigSession(EmptyConfig);
        session.Service.Load();
        var registry = ThreeSettings();
        using var setup = new Setup(session, registry, backend =>
        {
            backend.Displays.Add(Display(Asus1, "ASUS VG27AQL3A", 1));
            backend.NvTweakDeviceIds[Asus1] = Device1;
        });
        session.Profiles.Profiles[0].DisplayProfiles.Add(new ProfileDisplaySettings { DisplayName = "ASUS VG27AQL3A", MonitorId = Asus1 });
        setup.Colors.RefreshAll();
        session.Profiles.Profiles[0].DisplayProfiles.Clear();

        setup.Sync.Synchronize();

        Assert.Empty(registry.Reads);
        Assert.Equal((100d, 100d, 1d, 50, 0), Windows(session, Asus1));
    }

    // Without a registry reader (as in the composition of tests that do not set one) the
    // displays start neutral, as before.
    [Fact]
    public void WithoutARegistryReaderDisplaysStartNeutral()
    {
        using var session = new ConfigSession(EmptyConfig);
        session.Service.Load();
        var factory = new FakeBackendFactory { Configure = ThreeMonitors };
        using var nvidia = new NvidiaService(factory.Create);
        nvidia.Initialize();
        nvidia.RefreshDisplays();
        new DisplayProfileSyncService(session.Profiles, nvidia, new DisplayColorController(nvidia, session.Profiles, new SwitchingState()), session.Service)
            .Synchronize();

        Assert.Equal((100d, 100d, 1d, 60, 0), Windows(session, Asus1));
    }
}
