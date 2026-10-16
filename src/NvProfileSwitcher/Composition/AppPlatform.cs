using NvProfileSwitcher.Core;
using NvProfileSwitcher.Nvidia;
using NvProfileSwitcher.Platform;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Composition;

/// <summary>
/// Everything the application graph reaches outside the process: the configuration file,
/// the NVIDIA API and the NVIDIA Control Panel settings, the foreground window, GitHub
/// releases, global hotkeys, the Windows startup entry, dialogs, shutdown and the single
/// instance. <see cref="AppHost.Start"/>
/// passes the real implementations; tests pass fakes to the same composition.
/// </summary>
internal sealed record AppPlatform(
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
    Func<MainWindowHost, IDialogService> CreateDialogs,
    Func<bool> Shutdown,
    SingleInstanceService? SingleInstance);
