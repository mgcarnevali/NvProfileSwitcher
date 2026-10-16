using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using NvProfileSwitcher.Nvidia;
using NvProfileSwitcher.Platform;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Shell;
using NvProfileSwitcher.ViewModels;

namespace NvProfileSwitcher.Composition;

/// <summary>
/// Composition root. Builds the services, coordinators and ViewModels, creates the main
/// window and attaches the window host. It subscribes no handlers of its own.
/// </summary>
internal static class AppHost
{
    public static void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var platform = new AppPlatform(
            ConfigurationService.DefaultConfigPath,
            BuildInfo.IsDevelopmentBuild,
            () => new NvApi(),
            InstalledDriverVersion.Read,
            NvTweakColorRegistry.Read,
            new ForegroundWatcher(),
            ForegroundProcess.GetExecutableName,
            () => UpdateService.GetLatestReleaseInfoAsync(),
            info => UpdateService.GetReleasePackageAsync(info),
            new UpdateInstaller(),
            new Win32HotkeyRegistrar(),
            new StartupRegistry(),
            Environment.ProcessPath,
            host => new DialogService(host),
            () => desktop.TryShutdown(),
            Program.SingleInstance);

        var app = Compose(platform, viewModel => desktop.MainWindow = new MainWindow { DataContext = viewModel });

        if (desktop.Args?.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase)) == true)
            app.Host.StartHiddenToTray();
    }

    /// <summary>
    /// Builds the whole application over <paramref name="platform"/>, loads the configuration,
    /// creates the main window with <paramref name="createWindow"/> and attaches it.
    /// </summary>
    public static AppComposition Compose(AppPlatform platform, Func<MainWindowViewModel, Window> createWindow)
    {
        var host = new MainWindowHost();
        var services = AppServices.Create(new ServiceEnvironment(
            platform.ConfigPath,
            platform.IsDevelopmentBuild,
            platform.CreateDisplayBackend,
            platform.InstalledDriverVersion,
            platform.ReadNvTweakColors,
            platform.ForegroundMonitor,
            platform.ForegroundExecutable,
            platform.GetLatestReleaseInfo,
            platform.GetReleasePackage,
            platform.UpdateInstaller,
            platform.HotkeyRegistrar,
            platform.StartupRegistry,
            platform.ExecutablePath,
            platform.CreateDialogs(host),
            () => host.IsActive,
            () => host.IsShownNormally,
            host.SetTopmost,
            platform.Shutdown));
        host.AddMessageHandler(services.Topology.HandleMessage);
        host.AddMessageHandler(services.Hotkeys.HandleMessage);

        var tray = new TrayService(
            host, services.Switching, services.AutomaticSwitching, services.UpdateCheck, services.Dialogs, () => platform.Shutdown());

        var displayRefresh = new DisplayRefreshCoordinator(
            services.Nvidia, services.Profiles, services.DisplaySync, services.Topology, services.AutomaticSwitching, services.ActiveProfile,
            services.Preview, services.Colors, services.Editor, services.Switching);
        var startup = new StartupCoordinator(
            host, services.Configuration, services.StartupRegistration, services.Settings, services.Nvidia, displayRefresh,
            services.Colors, services.ActiveProfile, services.AutomaticSwitching, services.Switching, services.UpdateCheck,
            services.Hotkeys, services.Editor);
        _ = new WindowActivityCoordinator(host, services.Nvidia, services.Settings, tray, services.Capture, services.Preview);
        _ = new HotkeyActionDispatcher(services.Hotkeys, host, services.AutomaticSwitching, services.Preview);
        _ = new ShutdownCoordinator(
            host, services.Topology, services.Nvidia, services.AutomaticSwitching, services.Colors, tray, platform.SingleInstance,
            services.Hotkeys, services.Editor, services.Editing, services.Preview, services.Configuration);

        startup.LoadBeforeWindow();

        var mainViewModel = new MainWindowViewModel(
            new ProfilesViewModel(services.Profiles, services.Editor, services.Editing, services.ProfileList),
            new ProfileSettingsViewModel(services.Editor, services.Editing, services.Hotkeys, services.Capture, services.Dialogs),
            new HotkeysViewModel(services.Capture, services.Hotkeys),
            new ApplicationSettingsViewModel(
                services.Settings, services.Configuration, services.StartupRegistration, tray, host, services.Dialogs,
                services.UpdateCheck, services.SavedDisplays, services.Backup, platform.IsDevelopmentBuild),
            new StatusBarViewModel(services.Nvidia, services.Switching, services.Dialogs));

        var window = createWindow(mainViewModel);
        host.Attach(window);
        startup.PrepareWindow();

        // As in the original, the show requests of a second instance are handled once the
        // window exists; a request that arrived earlier is handled now.
        platform.SingleInstance?.RegisterShowHandler(tray.PostShowMainWindow);

        return new AppComposition(services, tray, host, startup, mainViewModel, window);
    }
}
