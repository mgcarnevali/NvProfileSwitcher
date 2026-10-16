using NvProfileSwitcher.Services;
using NvProfileSwitcher.Services.Editing;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Composition;

/// <summary>
/// Runs the close confirmation, closing and closed sequences in the original order.
/// It holds no state and only calls services; each migrated subsystem adds its steps here.
/// </summary>
internal sealed class ShutdownCoordinator
{
    private readonly MainWindowHost host;
    private readonly DisplayTopologyService topology;
    private readonly NvidiaService nvidia;
    private readonly AutomaticSwitchingService automaticSwitching;
    private readonly DisplayColorController colors;
    private readonly TrayService tray;
    private readonly SingleInstanceService? singleInstance;
    private readonly HotkeyService hotkeys;
    private readonly ProfileEditor editor;
    private readonly ProfileEditingService editing;
    private readonly LivePreviewController preview;
    private readonly ConfigurationService configuration;

    public ShutdownCoordinator(
        MainWindowHost host,
        DisplayTopologyService topology,
        NvidiaService nvidia,
        AutomaticSwitchingService automaticSwitching,
        DisplayColorController colors,
        TrayService tray,
        SingleInstanceService? singleInstance,
        HotkeyService hotkeys,
        ProfileEditor editor,
        ProfileEditingService editing,
        LivePreviewController preview,
        ConfigurationService configuration)
    {
        this.host = host;
        this.topology = topology;
        this.nvidia = nvidia;
        this.automaticSwitching = automaticSwitching;
        this.colors = colors;
        this.tray = tray;
        this.singleInstance = singleInstance;
        this.hotkeys = hotkeys;
        this.editor = editor;
        this.editing = editing;
        this.preview = preview;
        this.configuration = configuration;
        // Closing with unsaved changes asks first; discarding them lets the window close.
        host.SetCloseGuard(() => editor.IsDirty, editing.ConfirmAndDiscardAsync);
        host.Closing += OnClosing;
        host.Closed += OnClosed;
    }

    // The window closes for good: unsaved editor changes were already discarded or kept by
    // the close guard. Changes a failed save left only in memory get one more silent attempt
    // first (deliberate deviation), before anything that might throw.
    private void OnClosing()
    {
        configuration.SaveIfPending();
        automaticSwitching.Stop();
        // One operation; a failed write propagates, as in the original.
        using (colors.Batch(throwOnFailure: true))
        {
            preview.Suspend();
            colors.RestoreWindows();
        }
    }

    private void OnClosed()
    {
        hotkeys.UnregisterAll();
        host.RemoveMessageHook();
        topology.Stop();
        automaticSwitching.Dispose();
        nvidia.Dispose();
        singleInstance?.UnregisterShowHandler(tray.PostShowMainWindow);
        tray.Dispose();
    }
}
