using NvProfileSwitcher.Models;
using Avalonia.Threading;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Services.Editing;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Composition;

/// <summary>
/// Reacts to window activity (activated, deactivated, minimized, restored, hidden and
/// hiding to the tray) by calling services in the original order. It holds no state;
/// each migrated subsystem adds its steps here.
/// </summary>
internal sealed class WindowActivityCoordinator
{
    private readonly MainWindowHost host;
    private readonly NvidiaService nvidia;
    private readonly AppSettings settings;
    private readonly TrayService tray;
    private readonly HotkeyCaptureController capture;
    private readonly LivePreviewController preview;

    public WindowActivityCoordinator(
        MainWindowHost host,
        NvidiaService nvidia,
        AppSettings settings,
        TrayService tray,
        HotkeyCaptureController capture,
        LivePreviewController preview)
    {
        this.host = host;
        this.nvidia = nvidia;
        this.settings = settings;
        this.tray = tray;
        this.capture = capture;
        this.preview = preview;
        host.Activated += OnActivated;
        host.Deactivated += OnDeactivated;
        host.Minimized += OnMinimized;
        host.Restored += OnRestored;
        host.Hidden += preview.Suspend;
        host.HidingToTray += OnHidingToTray;
    }

    private void OnActivated()
    {
        nvidia.RefreshDriverVersion();
        preview.Resume();
    }

    private void OnDeactivated()
    {
        preview.Suspend();
        capture.EndUnlessMessageOpen();
    }

    // As in the original, the preview resumes after the restore has been processed.
    private void OnRestored() => Dispatcher.UIThread.Post(preview.Resume);

    private void OnHidingToTray() => capture.End();

    private void OnMinimized()
    {
        capture.End();
        preview.Suspend();
        if (settings.MinimizeToTray)
            host.HideToTray();
        else
            tray.SetIconVisible(false);
    }
}
