using Avalonia.Controls;
using Avalonia.Threading;
using NvProfileSwitcher.Composition;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Services.Editing;
using NvProfileSwitcher.Shell;
using NvProfileSwitcher.ViewModels;

namespace NvProfileSwitcher.UiTests;

// The application composed by AppHost.Compose over fakes: a temporary config.json, a fake
// NVIDIA API, foreground monitor, dialogs, releases, startup registry and hotkey registrar,
// and a plain window attached to the window host. The optional JSON is written as
// config.json before it is loaded; it can be locked while it loads, as another process would.
internal sealed class TestComposition : IDisposable
{
    public const string ExecutablePath = @"C:\Apps\NvProfileSwitcher\NvProfileSwitcher.exe";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "NvProfileSwitcher.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppComposition app;
    private bool closed;

    public TestComposition(
        string? configJson = null,
        bool isDevelopmentBuild = false,
        bool configLockedWhileLoading = false,
        FakeDialogService? dialogs = null,
        Func<MainWindowViewModel, Window>? createWindow = null)
    {
        Dialogs = dialogs ?? new FakeDialogService();
        if (configJson is not null)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(ConfigPath, configJson);
        }
        Monitor = new FakeForegroundMonitor(Factory.Log);

        var platform = new AppPlatform(
            ConfigPath,
            isDevelopmentBuild,
            Factory.Create,
            () => InstalledDriverVersion,
            deviceId => NvTweakColors.GetValueOrDefault(deviceId),
            Monitor,
            () => Foreground,
            Releases.GetLatestReleaseInfoAsync,
            Releases.GetReleasePackageAsync,
            Installer,
            Registrar,
            Registry,
            ExecutablePath,
            _ => Dialogs,
            () =>
            {
                ShutdownRequests++;
                return ShutdownAnswer;
            },
            SingleInstance: null);
        using (configLockedWhileLoading ? new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None) : null)
            app = AppHost.Compose(platform, createWindow ?? (_ => new Window()));
        app.Host.Closed += () => closed = true;
    }

    public string ConfigPath => Path.Combine(directory, "config.json");
    public FakeBackendFactory Factory { get; } = new();
    public FakeForegroundMonitor Monitor { get; }
    public FakeDialogService Dialogs { get; }
    public FakeReleaseSource Releases { get; } = new();
    public FakeStartupRegistry Registry { get; } = new();
    public FakeHotkeyRegistrar Registrar { get; } = new();
    public FakeUpdateInstaller Installer { get; } = new();
    public int ShutdownRequests { get; private set; }

    /// <summary>Whether a shutdown request closes the application.</summary>
    public bool ShutdownAnswer { get; set; } = true;
    public string Foreground { get; set; } = "explorer.exe";

    /// <summary>The version of the NVIDIA driver installed in Windows; null when it cannot be read, so the API reports it.</summary>
    public string? InstalledDriverVersion { get; set; }

    /// <summary>The NVIDIA Control Panel settings in the registry, by device id; a missing key reads as null.</summary>
    public Dictionary<uint, IReadOnlyList<NvTweakValue?>> NvTweakColors { get; } = [];

    public ProfileStore Profiles => app.Services.Profiles;
    public AppSettings Settings => app.Services.Settings;
    public SwitchingState Switching => app.Services.Switching;
    public ConfigurationService Configuration => app.Services.Configuration;
    public NvidiaService Nvidia => app.Services.Nvidia;
    public DisplayTopologyService Topology => app.Services.Topology;
    public DisplayColorController Colors => app.Services.Colors;
    public ActiveProfileService ActiveProfile => app.Services.ActiveProfile;
    public AutomaticSwitchingService AutomaticSwitching => app.Services.AutomaticSwitching;
    public UpdateCheckService UpdateCheck => app.Services.UpdateCheck;
    public HotkeyService Hotkeys => app.Services.Hotkeys;
    public HotkeyCaptureController Capture => app.Services.Capture;
    public HotkeysViewModel HotkeysPanel => app.ViewModel.Hotkeys;
    public StartupRegistrationService StartupRegistration => app.Services.StartupRegistration;
    public TrayService Tray => app.Tray;
    public ApplicationSettingsViewModel ApplicationSettings => app.ViewModel.ApplicationSettings;
    public ProfileEditor Editor => app.Services.Editor;
    public ProfileEditingService Editing => app.Services.Editing;
    public ProfileListService ProfileList => app.Services.ProfileList;
    public LivePreviewController Preview => app.Services.Preview;
    public SavedDisplaysService SavedDisplays => app.Services.SavedDisplays;
    public ConfigurationBackupService Backup => app.Services.Backup;
    public ProfilesViewModel ProfilesPanel => app.ViewModel.Profiles;
    public ProfileSettingsViewModel ProfileSettings => app.ViewModel.ProfileSettings;
    public MainWindowViewModel ViewModel => app.ViewModel;
    public MainWindowHost Host => app.Host;
    public StartupCoordinator Startup => app.Startup;
    public Window Window => app.Window;

    // Runs the dispatcher, including its timers, until the condition holds or the timeout ends.
    public static bool RunUntil(Func<bool> condition, TimeSpan timeout)
    {
        using var stop = new CancellationTokenSource(timeout);
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        poll.Tick += (_, _) =>
        {
            if (condition())
                stop.Cancel();
        };
        poll.Start();
        Dispatcher.UIThread.MainLoop(stop.Token);
        poll.Stop();
        return condition();
    }

    // Shows the window and makes it the active one, as headless windows are not activated on their own.
    public void ShowActive()
    {
        Window.Show();
        Window.Activate();
        Dispatcher.UIThread.RunJobs();
    }

    // Headless windows do not deactivate each other, so the platform deactivation callback
    // that Avalonia sets on the window implementation is invoked directly.
    public void Deactivate()
    {
        var impl = Window.PlatformImpl!;
        var callback = impl.GetType()
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Single(property => property.Name.EndsWith("Deactivated", StringComparison.Ordinal));
        ((Action)callback.GetValue(impl)!).Invoke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Presses a hotkey as Windows would: WM_HOTKEY with the ID that holds the combination.
    /// Returns false when no registration holds it, so the key would reach other applications.
    /// </summary>
    public bool PressHotkey(int hotkey)
    {
        if (Registrar.IdOf(hotkey) is not { } id)
            return false;

        Host.DispatchMessage(0x0312, id);
        Dispatcher.UIThread.RunJobs();
        return true;
    }

    /// <summary>
    /// Checks that the registered hotkeys are exactly the ones the state calls for: the global
    /// hotkeys and the hotkeys of the enabled application profiles, each combination once, minus
    /// the registrations a test made fail. Nothing is registered before the hotkeys were attached,
    /// during a capture or after the window closed.
    /// </summary>
    public void AssertHotkeysInSync()
    {
        var attached = Registrar.Attached;
        var expected = new HashSet<int>();
        if (attached && !closed && Capture.CaptureTarget is null)
        {
            void Expect(int id, int hotkey)
            {
                if (hotkey != 0 && !HotkeyCore.IsCtrlAlt(hotkey) && !Registrar.FailingIds.Contains(id))
                    expected.Add(hotkey);
            }

            Expect(HotkeyService.ShowHideId, Settings.ShowHideHotkey);
            Expect(HotkeyService.WindowsOverrideId, Settings.WindowsOverrideHotkey);
            Expect(HotkeyService.ResumeAutomaticId, Settings.ResumeAutomaticHotkey);
            Expect(HotkeyService.GlobalSwitchingId, Settings.GlobalSwitchingHotkey);
            var applicationProfiles = Profiles.ApplicationProfiles;
            for (var i = 0; i < applicationProfiles.Count && HotkeyService.ProfileBaseId + i <= 0xBFFF; i++)
                if (applicationProfiles[i].Enabled)
                    Expect(HotkeyService.ProfileBaseId + i, applicationProfiles[i].Hotkey);
        }

        Assert.Equal(expected.Order(), Registrar.ActiveHotkeys().Order());
    }

    public void Dispose()
    {
        AssertHotkeysInSync();
        if (Window.IsVisible)
            Window.Close();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
