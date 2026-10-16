using System.Runtime.ExceptionServices;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;

namespace NvProfileSwitcher.Services;

/// <summary>
/// The only writer of display colors. It holds what each display should show: the saved values
/// of the base profile, which <see cref="ActiveProfileService"/> chooses, or the values of the
/// live preview on the previewed display. Changes only mark the displays they affect; the
/// displays are written when the outermost <see cref="Batch"/> ends, or at once outside a batch,
/// so each display receives its final values once per operation and never an intermediate
/// state. Nothing is written while global switching is paused unless the operation forces it,
/// and the preview is never shown then.
/// </summary>
internal sealed class DisplayColorController(NvidiaService nvidia, ProfileStore profiles, SwitchingState switching)
{
    private readonly HashSet<string> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> written = new(StringComparer.OrdinalIgnoreCase);
    private bool pendingAll;
    private bool forced;
    private int depth;
    private bool throwOnFailure;
    private bool baseSet;

    // Null is the Windows profile.
    private ProfileListItem? baseProfile;
    private (string MonitorId, ColorValues Values)? preview;

    /// <summary>
    /// Raised when a batch that does not throw could not write a display, so the active profile
    /// is applied again by the next check, as after a failed switch.
    /// </summary>
    public event Action? WriteFailed;

    /// <summary>
    /// Groups changes into one operation: the displays are written once, with their final
    /// values, when the outermost batch ends. A failed write is thrown from there when the
    /// outermost batch says so (<paramref name="throwOnFailure"/>); otherwise it is ignored, as the
    /// original ignored failed applications on those paths, and <see cref="WriteFailed"/> is raised.
    /// Outside a batch, each change is written at once and a failure is thrown.
    /// </summary>
    public BatchScope Batch(bool throwOnFailure = false)
    {
        if (depth++ == 0)
            this.throwOnFailure = throwOnFailure;
        return new BatchScope(this);
    }

    /// <summary>
    /// Makes <paramref name="profile"/> (null for the Windows profile) the profile whose saved
    /// values the displays show. With <paramref name="allDisplays"/>, every display is written
    /// again; otherwise only when the profile changed.
    /// </summary>
    public void SetBase(ProfileListItem? profile, bool allDisplays)
    {
        var changed = !baseSet || !ReferenceEquals(baseProfile, profile);
        baseSet = true;
        baseProfile = profile;
        if (allDisplays || changed)
            pendingAll = true;
        Commit();
    }

    /// <summary>Applies the Windows profile to every display; with <paramref name="force"/>, also while global switching is paused.</summary>
    public void RestoreWindows(bool force = false)
    {
        baseSet = true;
        baseProfile = null;
        pendingAll = true;
        forced |= force;
        Commit();
    }

    /// <summary>Shows <paramref name="values"/> on the display of <paramref name="monitorId"/> instead of the base profile.</summary>
    public void SetPreview(string monitorId, ColorValues values)
    {
        if (preview is { } previous)
            pending.Add(previous.MonitorId);
        preview = (monitorId, values);
        pending.Add(monitorId);
        Commit();
    }

    /// <summary>Ends the preview: its display shows the base profile again.</summary>
    public void ClearPreview()
    {
        if (preview is not { } previous)
            return;

        preview = null;
        pending.Add(previous.MonitorId);
        Commit();
    }

    /// <summary>Writes every display again with what it should show, as after a display topology change.</summary>
    public void RefreshAll()
    {
        pendingAll = true;
        Commit();
    }

    /// <summary>Whether this application wrote colors to the display of <paramref name="monitorId"/> in this session.</summary>
    public bool HasWritten(string monitorId) => written.Contains(monitorId);

    private void EndBatch()
    {
        if (--depth > 0)
            return;

        try
        {
            Commit();
        }
        catch (Exception) when (!throwOnFailure)
        {
            WriteFailed?.Invoke();
        }
    }

    private void Commit()
    {
        if (depth > 0)
            return;

        var all = pendingAll;
        var force = forced;
        var marked = pending.ToList();
        pendingAll = false;
        forced = false;
        pending.Clear();
        if (!nvidia.IsInitialized || !ProfileApplicationCore.CanApply(switching.GlobalPaused, force))
            return;

        var showPreview = !switching.GlobalPaused;
        var source = baseProfile ?? profiles.Windows;
        ExceptionDispatchInfo? failure = null;
        foreach (var display in nvidia.Displays)
        {
            if (!all && !marked.Contains(display.MonitorId, StringComparer.OrdinalIgnoreCase))
                continue;

            int vibrance, hue;
            double brightness, contrast, gamma;
            if (showPreview && preview is { } shown && DisplayCore.SameMonitorId(shown.MonitorId, display.MonitorId))
                (vibrance, brightness, contrast, gamma, hue) =
                    (shown.Values.RoundedVibrance, shown.Values.Brightness, shown.Values.Contrast, shown.Values.Gamma, shown.Values.RoundedHue);
            else if (source is not null && DisplayCore.FindValues(source, display.MonitorId) is { } values)
                (vibrance, brightness, contrast, gamma, hue) =
                    (values.DigitalVibrance, values.Brightness, values.Contrast, values.Gamma, values.Hue);
            else
                continue;

            // A failure on one display does not keep the others from their values; the first
            // one is reported once all were written. The result of each display is not checked.
            written.Add(display.MonitorId);
            try
            {
                nvidia.Apply(display, vibrance, brightness, contrast, gamma, hue);
            }
            catch (Exception exception)
            {
                failure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        failure?.Throw();
    }

    /// <summary>Ends a <see cref="Batch"/>.</summary>
    public readonly struct BatchScope(DisplayColorController controller) : IDisposable
    {
        public void Dispose() => controller.EndBatch();
    }
}
