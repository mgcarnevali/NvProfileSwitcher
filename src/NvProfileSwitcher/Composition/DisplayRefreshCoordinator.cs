using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using NvProfileSwitcher.Services.Editing;

namespace NvProfileSwitcher.Composition;

/// <summary>
/// Runs the display refresh sequence in the original order, at startup and after every
/// display topology change, and the recovery after a return from sleep or hibernation. It holds
/// no state and only calls services; each migrated subsystem adds its steps here.
/// </summary>
internal sealed class DisplayRefreshCoordinator
{
    private readonly NvidiaService nvidia;
    private readonly ProfileStore profiles;
    private readonly DisplayProfileSyncService displaySync;
    private readonly DisplayTopologyService topology;
    private readonly AutomaticSwitchingService automaticSwitching;
    private readonly ActiveProfileService activeProfile;
    private readonly LivePreviewController preview;
    private readonly DisplayColorController colors;
    private readonly ProfileEditor editor;
    private readonly SwitchingState switching;

    public DisplayRefreshCoordinator(
        NvidiaService nvidia,
        ProfileStore profiles,
        DisplayProfileSyncService displaySync,
        DisplayTopologyService topology,
        AutomaticSwitchingService automaticSwitching,
        ActiveProfileService activeProfile,
        LivePreviewController preview,
        DisplayColorController colors,
        ProfileEditor editor,
        SwitchingState switching)
    {
        this.nvidia = nvidia;
        this.profiles = profiles;
        this.displaySync = displaySync;
        this.topology = topology;
        this.automaticSwitching = automaticSwitching;
        this.activeProfile = activeProfile;
        this.preview = preview;
        this.colors = colors;
        this.editor = editor;
        this.switching = switching;
        topology.TopologyChanged += OnTopologyChanged;
        topology.Resumed += Recover;
    }

    // Installing a driver restarts the display adapter, which changes the display mode: the
    // installed driver version is read again then, also while the window is not active.
    private void OnTopologyChanged()
    {
        nvidia.RefreshDriverVersion();
        Refresh();
    }

    public void Refresh()
    {
        if (!nvidia.IsInitialized)
            return;

        // Read before the displays are synchronized, which may add values to the edited profile.
        var draftWasDirty = editor.IsDirty;
        // One operation: every display is written once, when the refresh ends, with what it
        // shows from then on, so a preview that resumes on its display never gives way to the
        // real colors in between (deliberate deviation: the original reapplied the real colors
        // before the preview resumed, which flashed them on that display). A display that could
        // not be written is written again by the next check.
        using (colors.Batch())
        {
            try
            {
                preview.Discard();
                var newlySharedIds = nvidia.RefreshDisplays(profiles.NonUniqueMonitorIds);
                // The displays may have lost their colors with the topology change: each one is
                // written again with what it shows.
                colors.RefreshAll();
                var remaps = displaySync.Synchronize(newlySharedIds);
                // The editor's display selector follows the new displays; as in the original,
                // choosing its display again may resume the preview.
                editor.RefreshDisplays(nvidia.Displays, remaps);
                if (automaticSwitching.IsSwitchingActive)
                    activeProfile.Reevaluate();
            }
            catch (Exception)
            {
                // As in the original refresh, a failed display refresh leaves the current state.
            }

            // A draft without changes starts over on the refreshed displays, keeping the selected
            // one, so values the synchronization added to its profile do not count as unsaved
            // changes. A draft with changes is never touched (deliberate deviation on topology
            // changes: the original did this only when the window opened).
            if (!draftWasDirty && editor.Session is { } session)
                editor.Begin(session.Source, editor.SelectedDisplay?.MonitorId);
        }

        // A display whose monitor identity Windows could not report yet is left out until it can;
        // no further message arrives then, so the refresh runs once more.
        if (nvidia.UnidentifiedCount > 0)
            topology.RetryOnce();
    }

    /// <summary>
    /// After a return from sleep or hibernation, the NVIDIA API, its display handles and the
    /// colors may no longer be valid: the API is initialized again, the displays are refreshed
    /// and every display is written with what it shows, in one operation. While global switching
    /// is paused, Windows is restored. A failed initialization reports the API as unavailable and
    /// keeps the current instance, as on opening.
    /// </summary>
    public void Recover()
    {
        if (!nvidia.IsInitialized)
            return;

        try
        {
            nvidia.Initialize();
        }
        catch (Exception)
        {
            nvidia.ReportUnavailable();
            return;
        }

        using (colors.Batch())
        {
            Refresh();
            if (switching.GlobalPaused)
                colors.RestoreWindows(force: true);
        }
    }
}
