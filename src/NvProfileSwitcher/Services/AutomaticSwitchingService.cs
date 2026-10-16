using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Platform;
using NvProfileSwitcher.Services.Editing;

namespace NvProfileSwitcher.Services;

/// <summary>
/// Controls automatic switching: the foreground monitor, global switching and the overrides,
/// saving them when they are remembered. The active profile itself is applied by
/// <see cref="ActiveProfileService"/>.
/// </summary>
internal sealed class AutomaticSwitchingService : IDisposable
{
    private readonly IForegroundMonitor monitor;
    private readonly ActiveProfileService activeProfile;
    private readonly DisplayColorController colors;
    private readonly SwitchingState switching;
    private readonly AppSettings settings;
    private readonly ConfigurationService configuration;
    private readonly ProfileStore profiles;
    private readonly LivePreviewController preview;

    // The original creates its foreground watcher on the first window opening; until then
    // global switching changes do not start or stop it.
    private bool monitorCreated;

    public AutomaticSwitchingService(
        IForegroundMonitor monitor,
        ActiveProfileService activeProfile,
        DisplayColorController colors,
        SwitchingState switching,
        AppSettings settings,
        ConfigurationService configuration,
        ProfileStore profiles,
        LivePreviewController preview)
    {
        this.monitor = monitor;
        this.activeProfile = activeProfile;
        this.colors = colors;
        this.switching = switching;
        this.settings = settings;
        this.configuration = configuration;
        this.profiles = profiles;
        this.preview = preview;
        monitor.Changed += activeProfile.Check;
    }

    /// <summary>
    /// Whether foreground changes are being followed: the monitor exists and global switching
    /// is on. A display refresh then reevaluates the active profile.
    /// </summary>
    public bool IsSwitchingActive => monitorCreated && !switching.GlobalPaused;

    /// <summary>Creates the foreground monitor once and starts it unless global switching is paused.</summary>
    public void Start()
    {
        if (monitorCreated) return;

        monitorCreated = true;
        if (!switching.GlobalPaused)
            monitor.Start();
    }

    public void Stop()
    {
        if (monitorCreated) monitor.Stop();
    }

    /// <summary>Turns global switching off when it is on, and on when it is off.</summary>
    public void ToggleGlobal() => SetGlobalEnabled(switching.GlobalPaused);

    public void SetGlobalEnabled(bool enabled)
    {
        if (enabled == !switching.GlobalPaused) return;

        switching.GlobalPaused = !enabled;
        if (settings.RememberGlobalSwitching) configuration.Save();

        if (!enabled)
        {
            Stop();
            colors.RestoreWindows(force: true);
            activeProfile.PublishActiveProfile();
            return;
        }

        if (monitorCreated) monitor.Start();

        // A preview in progress is applied again instead of the real colors.
        if (preview.ReapplyIfPreviewing())
        {
            activeProfile.PublishActiveProfile();
            return;
        }

        activeProfile.Invalidate();
        activeProfile.ReapplyRealColors();
    }

    /// <summary>
    /// Applies the global switching state of an imported configuration, as the original did
    /// after an import: the active profile is cleared first; then a paused state forces the
    /// Windows profile, and otherwise the monitor is started and the active profile checked.
    /// </summary>
    public void ApplyImportedState()
    {
        activeProfile.Invalidate();
        if (switching.GlobalPaused)
        {
            Stop();
            colors.RestoreWindows(force: true);
            activeProfile.PublishActiveProfile();
            return;
        }

        if (monitorCreated) monitor.Start();
        activeProfile.Check();
    }

    // The overrides discard a preview in progress first, as in the original, in the same
    // operation as the switch, so each display is written once.
    public void ToggleWindowsOverride()
    {
        using var batch = colors.Batch();
        preview.Discard();
        switching.OverrideMode = OverrideCore.ToggleWindows(switching.OverrideMode);
        ReevaluateOverride();
    }

    public void ResumeAutomatic()
    {
        using var batch = colors.Batch();
        preview.Discard();
        switching.OverrideMode = OverrideMode.Automatic;
        switching.OverrideProfileIndex = -1;
        ReevaluateOverride();
    }

    public void ToggleProfileOverride(int applicationProfileIndex)
    {
        if (OverrideCore.ToggleProfile(
                switching.OverrideMode,
                switching.OverrideProfileIndex,
                applicationProfileIndex,
                profiles.ApplicationProfiles) is not { } next)
            return;

        using var batch = colors.Batch();
        preview.Discard();
        switching.OverrideMode = next.Mode;
        switching.OverrideProfileIndex = next.ProfileIndex;
        ReevaluateOverride();
    }

    public void Dispose()
    {
        if (monitorCreated) monitor.Dispose();
    }

    private void ReevaluateOverride()
    {
        activeProfile.Invalidate();
        if (settings.RememberOverride) configuration.Save();
        activeProfile.Check();
    }
}
