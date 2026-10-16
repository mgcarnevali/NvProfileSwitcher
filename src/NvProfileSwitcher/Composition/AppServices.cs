using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Nvidia;
using NvProfileSwitcher.Platform;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Services.Editing;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Composition;

/// <summary>
/// What the services reach outside themselves: the platform seams, the dialogs and three facts
/// about the main window. The services never see the window host itself.
/// </summary>
internal sealed record ServiceEnvironment(
    string ConfigPath,
    bool IsDevelopmentBuild,
    Func<IDisplayColorBackend> CreateDisplayBackend,
    Func<string?> InstalledDriverVersion,
    Func<uint, IReadOnlyList<NvTweakValue?>?> ReadNvTweakColors,
    IForegroundMonitor ForegroundMonitor,
    Func<string> ForegroundExecutable,
    Func<Task<UpdateReleaseInfo?>> GetLatestReleaseInfo,
    Func<UpdateReleaseInfo, Task<UpdateRelease?>> GetReleasePackage,
    IUpdateInstaller UpdateInstaller,
    IHotkeyRegistrar HotkeyRegistrar,
    IStartupRegistry StartupRegistry,
    string? ExecutablePath,
    IDialogService Dialogs,
    Func<bool> IsWindowActive,
    Func<bool> IsWindowShownNormally,
    Action<bool> SetWindowTopmost,
    Func<bool> Shutdown);

/// <summary>
/// The application state and services, wired once. The application adds the window host, the
/// tray, the coordinators and the ViewModels on top; tests use the same graph over fakes.
/// </summary>
internal sealed class AppServices
{
    private AppServices(ServiceEnvironment environment)
    {
        Switching = new SwitchingState();
        Profiles = new ProfileStore(Switching);
        Settings = new AppSettings();
        Dialogs = environment.Dialogs;
        Configuration = new ConfigurationService(
            environment.ConfigPath, Profiles, Settings, Switching, environment.IsDevelopmentBuild, Dialogs);
        Nvidia = new NvidiaService(environment.CreateDisplayBackend, environment.InstalledDriverVersion);
        Colors = new DisplayColorController(Nvidia, Profiles, Switching);
        DisplaySync = new DisplayProfileSyncService(Profiles, Nvidia, Colors, Configuration, environment.ReadNvTweakColors);
        Topology = new DisplayTopologyService();
        Hotkeys = new HotkeyService(environment.HotkeyRegistrar, Settings, Profiles, Dialogs);
        Editor = new ProfileEditor(Profiles);

        // The preview gives its display back to the real colors, which the active profile
        // chooses; the active profile tells it before a switch so it ends first.
        ActiveProfile = new ActiveProfileService(
            Colors, Nvidia, Profiles, Switching, environment.ForegroundExecutable, Editor, environment.IsWindowActive);
        // A display a batch could not write is written again by the next check, as after a failed switch.
        Colors.WriteFailed += ActiveProfile.Invalidate;
        Preview = new LivePreviewController(
            Editor, ActiveProfile, Colors, Switching, environment.IsWindowShownNormally);
        AutomaticSwitching = new AutomaticSwitchingService(
            environment.ForegroundMonitor, ActiveProfile, Colors, Switching, Settings, Configuration, Profiles, Preview);

        Capture = new HotkeyCaptureController(Hotkeys, Settings, Profiles, Configuration, Editor, environment.IsWindowActive);
        Editing = new ProfileEditingService(Editor, Profiles, Preview, Configuration, Hotkeys, ActiveProfile, Capture, Colors, Dialogs);
        ProfileList = new ProfileListService(
            Profiles, Editor, Editing, Preview, Hotkeys, Configuration, ActiveProfile, Nvidia, Colors, Dialogs);
        UpdateCheck = new UpdateCheckService(
            Settings, Dialogs, environment.IsDevelopmentBuild, environment.GetLatestReleaseInfo, environment.GetReleasePackage,
            environment.UpdateInstaller, Editing.ConfirmAndDiscardAsync, environment.Shutdown);
        StartupRegistration = new StartupRegistrationService(Settings, environment.StartupRegistry, environment.ExecutablePath);
        SavedDisplays = new SavedDisplaysService(Profiles, Nvidia, Configuration);
        Backup = new ConfigurationBackupService(
            Dialogs, Configuration, Settings, Editor, Editing, Preview, Hotkeys, DisplaySync, StartupRegistration,
            AutomaticSwitching, environment.SetWindowTopmost);
    }

    public ProfileStore Profiles { get; }
    public AppSettings Settings { get; }
    public SwitchingState Switching { get; }
    public IDialogService Dialogs { get; }
    public ConfigurationService Configuration { get; }
    public NvidiaService Nvidia { get; }
    public DisplayProfileSyncService DisplaySync { get; }
    public DisplayTopologyService Topology { get; }
    public DisplayColorController Colors { get; }
    public HotkeyService Hotkeys { get; }
    public ProfileEditor Editor { get; }
    public ProfileEditingService Editing { get; }
    public ProfileListService ProfileList { get; }
    public ActiveProfileService ActiveProfile { get; }
    public LivePreviewController Preview { get; }
    public AutomaticSwitchingService AutomaticSwitching { get; }
    public UpdateCheckService UpdateCheck { get; }
    public HotkeyCaptureController Capture { get; }
    public StartupRegistrationService StartupRegistration { get; }
    public SavedDisplaysService SavedDisplays { get; }
    public ConfigurationBackupService Backup { get; }

    public static AppServices Create(ServiceEnvironment environment) => new(environment);
}
