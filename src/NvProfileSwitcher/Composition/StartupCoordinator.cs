using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Services.Editing;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Composition;

/// <summary>
/// Runs the startup sequence in the original order: the configuration load before the window
/// exists, then the steps that need the opened window. It holds no state and only calls services.
/// </summary>
internal sealed class StartupCoordinator
{
    private readonly MainWindowHost host;
    private readonly ConfigurationService configuration;
    private readonly StartupRegistrationService startupRegistration;
    private readonly AppSettings settings;
    private readonly NvidiaService nvidia;
    private readonly DisplayRefreshCoordinator displayRefresh;
    private readonly DisplayColorController colors;
    private readonly ActiveProfileService activeProfile;
    private readonly AutomaticSwitchingService automaticSwitching;
    private readonly SwitchingState switching;
    private readonly UpdateCheckService updateCheck;
    private readonly HotkeyService hotkeys;
    private readonly ProfileEditor editor;

    public StartupCoordinator(
        MainWindowHost host,
        ConfigurationService configuration,
        StartupRegistrationService startupRegistration,
        AppSettings settings,
        NvidiaService nvidia,
        DisplayRefreshCoordinator displayRefresh,
        DisplayColorController colors,
        ActiveProfileService activeProfile,
        AutomaticSwitchingService automaticSwitching,
        SwitchingState switching,
        UpdateCheckService updateCheck,
        HotkeyService hotkeys,
        ProfileEditor editor)
    {
        this.host = host;
        this.configuration = configuration;
        this.startupRegistration = startupRegistration;
        this.settings = settings;
        this.nvidia = nvidia;
        this.displayRefresh = displayRefresh;
        this.colors = colors;
        this.activeProfile = activeProfile;
        this.automaticSwitching = automaticSwitching;
        this.switching = switching;
        this.updateCheck = updateCheck;
        this.hotkeys = hotkeys;
        this.editor = editor;
        host.Opened += OnOpened;
    }

    /// <summary>Steps that run before the main window is created.</summary>
    public void LoadBeforeWindow()
    {
        configuration.Load();
        startupRegistration.Apply();
        editor.BeginFirstProfile();
    }

    /// <summary>Steps that run once the main window is attached, before it is shown.</summary>
    public void PrepareWindow() => host.SetTopmost(settings.AlwaysOnTop);

    // Runs every time the window opens, including each time it is shown from the tray.
    private void OnOpened()
    {
        // Registers the saved hotkeys only on the first opening with a window handle.
        hotkeys.Attach(host.WindowHandle);

        try
        {
            // One operation: each display is written once. A failed write is thrown before
            // switching starts, as in the original.
            using (colors.Batch(throwOnFailure: true))
            {
                nvidia.Initialize();
                activeProfile.PublishActiveProfile();
                // The refresh also starts a clean draft over on the refreshed displays.
                displayRefresh.Refresh();
                // When switching is already active, the refresh has just applied the active profile;
                // restoring Windows over it would desynchronize them (deliberate deviation, §14.6).
                if (!automaticSwitching.IsSwitchingActive)
                    colors.RestoreWindows(force: switching.GlobalPaused);
            }

            automaticSwitching.Start();
        }
        catch (Exception)
        {
            nvidia.ReportUnavailable();
        }

        // Not awaited: it runs in the background on every opening, also when the NVIDIA API failed.
        _ = updateCheck.CheckAsync(manual: false);
        configuration.ReportLoadProblem();
    }
}
