using NvProfileSwitcher.Composition;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Services.Editing;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.Core.Tests;

// Executables are file names only: matching full paths is covered by SwitchingCoreTests.
// Profiles, two connected displays and the switching services over a fake NVIDIA API and a
// fake foreground monitor, with the editor and live preview of a window that is active
// and shown when the test says so. Every profile has distinct values on each display, so each
// application identifies the profile it came from.
internal sealed class SwitchingFixture : IDisposable
{
    public const string Dell = "DEL4321-ABC123";
    public const string Lg = "GSM5B7F-305NTAB1C2D3";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "NvProfileSwitcher.Tests", Guid.NewGuid().ToString("N"));

    public SwitchingFixture(bool initialize = true)
    {
        Monitor = new FakeForegroundMonitor(Factory.Log);
        Services = AppServices.Create(new ServiceEnvironment(
            Path.Combine(directory, "config.json"),
            IsDevelopmentBuild: false,
            Factory.Create,
            () => InstalledDriverVersion,
            ReadNvTweakColors,
            Monitor,
            () =>
            {
                ForegroundReads++;
                return Foreground;
            },
            Releases.GetLatestReleaseInfoAsync,
            Releases.GetReleasePackageAsync,
            new FakeUpdateInstaller(),
            Registrar,
            new FakeStartupRegistry(),
            ExecutablePath: null,
            Dialogs,
            () => WindowActive,
            () => WindowShown,
            _ => { },
            () => true));
        Session = new FixtureSession(Services, Path.Combine(directory, "config.json"));

        // No config.json: the default Windows profile, without writing the file.
        Session.Service.Load();
        Windows = Session.Profiles.Windows!;
        Windows.DisplayProfiles.Add(Values(Dell, 50, 100, 100, 1, 0));
        Windows.DisplayProfiles.Add(Values(Lg, 55, 98, 102, 0.95, 0));

        Game = Profile("Game", enabled: true, "game.exe");
        Game.DisplayProfiles.Add(Values(Dell, 80, 105, 110, 1.15, 10));
        Game.DisplayProfiles.Add(Values(Lg, 60, 100, 100, 1, 355));

        Tarkov = Profile("Tarkov", enabled: false, "EscapeFromTarkov.exe");
        Tarkov.DisplayProfiles.Add(Values(Dell, 30, 90, 95, 0.9, 20));
        Tarkov.DisplayProfiles.Add(Values(Lg, 31, 91, 96, 0.91, 21));

        // No executables: matches a foreground application whose name is the profile name.
        Notepad = Profile("notepad.exe", enabled: true);
        Notepad.DisplayProfiles.Add(Values(Dell, 70, 101, 99, 1.05, 5));
        Notepad.DisplayProfiles.Add(Values(Lg, 71, 102, 98, 1.06, 6));

        Session.Profiles.Load([Windows, Game, Tarkov, Notepad]);

        Factory.Configure = backend =>
        {
            backend.Displays.Add(Display(Dell, "DELL U2720Q"));
            backend.Displays.Add(Display(Lg, "LG ULTRAGEAR", 2));
        };
        if (initialize)
        {
            Nvidia.Initialize();
            Nvidia.RefreshDisplays();
        }
    }

    public AppServices Services { get; }
    public FixtureSession Session { get; }
    public FakeBackendFactory Factory { get; } = new();
    public FakeReleaseSource Releases { get; } = new();
    public NvidiaService Nvidia => Services.Nvidia;
    public DisplayColorController Colors => Services.Colors;
    public ActiveProfileService Active => Services.ActiveProfile;
    public FakeDialogService Dialogs { get; } = new();
    public FakeHotkeyRegistrar Registrar { get; } = new();
    public HotkeyService Hotkeys => Services.Hotkeys;
    public ProfileEditor Editor => Services.Editor;
    public ProfileEditingService Editing => Services.Editing;
    public LivePreviewController Preview => Services.Preview;
    public bool WindowActive { get; set; }
    public bool WindowShown { get; set; } = true;
    public FakeForegroundMonitor Monitor { get; }
    public AutomaticSwitchingService Automatic => Services.AutomaticSwitching;
    public string Foreground { get; set; } = "explorer.exe";

    /// <summary>The version of the NVIDIA driver installed in Windows; null when it cannot be read, so the API reports it.</summary>
    public string? InstalledDriverVersion { get; set; }

    /// <summary>The NVIDIA Control Panel settings in the registry, by device id; a missing key reads as null.</summary>
    public Dictionary<uint, IReadOnlyList<NvTweakValue?>> NvTweakColors { get; } = [];

    /// <summary>The device ids whose settings were read.</summary>
    public List<uint> NvTweakReads { get; } = [];

    private IReadOnlyList<NvTweakValue?>? ReadNvTweakColors(uint deviceId)
    {
        NvTweakReads.Add(deviceId);
        return NvTweakColors.GetValueOrDefault(deviceId);
    }

    public int ForegroundReads { get; private set; }

    public ProfileListItem Windows { get; }
    public ProfileListItem Game { get; }
    public ProfileListItem Tarkov { get; }
    public ProfileListItem Notepad { get; }

    public FakeDisplayBackend Backend => Factory.Created[^1];

    /// <summary>The profile each application came from, in order ("?" when none matches).</summary>
    public List<string> AppliedProfiles() =>
        Backend.Applied.Select(application =>
            new[] { Windows, Game, Tarkov, Notepad }
                .FirstOrDefault(p => DisplayCore.FindValues(p, application.MonitorId) is { } values && application.Matches(values))
                ?.Name ?? "?")
            .ToList();

    public string StatusText => ActiveProfileCore.StatusText(Session.Switching.DisplayedActiveProfile);

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private ProfileListItem Profile(string name, bool enabled, params string[] executables)
    {
        var profile = new ProfileListItem(name) { Enabled = enabled };
        profile.SetExecutables(executables);
        return profile;
    }

    private static ProfileDisplaySettings Values(string monitorId, int vibrance, double brightness, double contrast, double gamma, int hue) => new()
    {
        MonitorId = monitorId,
        DisplayName = monitorId,
        DigitalVibrance = vibrance,
        Brightness = brightness,
        Contrast = contrast,
        Gamma = gamma,
        Hue = hue,
    };
}

// The fixture's state and configuration service, under the names the configuration tests use.
internal sealed class FixtureSession(AppServices services, string configPath)
{
    public string ConfigPath { get; } = configPath;
    public ProfileStore Profiles => services.Profiles;
    public AppSettings Settings => services.Settings;
    public SwitchingState Switching => services.Switching;
    public ConfigurationService Service => services.Configuration;
}
