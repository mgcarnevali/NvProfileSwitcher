using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Platform;

namespace NvProfileSwitcher.Services;

/// <summary>
/// The only writer of display modes. A profile may give each monitor a resolution and a refresh
/// rate, each independent of the other (<see cref="ProfileDisplaySettings.Resolution"/>,
/// <see cref="ProfileDisplaySettings.RefreshRate"/>); while that profile is the one the switching
/// rules apply, its monitors show that mode (<see cref="DisplayModeCore.ChooseMode"/>), and every
/// other target (Windows, a profile without modes, Global Switching off) shows each monitor's
/// desktop mode, resolution and rate. It is separate
/// from the colors: <see cref="ActiveProfileService"/> calls it first, then applies the colors.
/// <para>
/// The desktop mode of a monitor is read right before its first change and kept until the
/// monitor is restored, whatever profiles follow. A monitor is restored only while it still
/// shows a mode this controller applied: a mode the user or a game set since is left alone.
/// Every change is recorded in <see cref="DisplayModeRecoveryStore"/> before it is made, and not
/// made when it cannot be recorded, so a start after a crash can restore it.
/// </para>
/// <para>
/// Modes change only when the target changes, never on a reevaluation of the same target, so a
/// display change (WM_DISPLAYCHANGE) never leads to another change. A configuration without
/// resolutions or refresh rates never reaches the backend.
/// </para>
/// <para>
/// Nothing here throws: a failure on one monitor is left for the next display change, and the
/// other monitors, and the colors applied after this controller, go on.
/// </para>
/// </summary>
internal sealed class DisplayModeController(
    IDisplayModeBackend backend,
    DisplayService connectedDisplays,
    SwitchingState switching,
    DisplayModeRecoveryStore recovery)
{
    private readonly Dictionary<string, Owned> owned = new(StringComparer.OrdinalIgnoreCase);

    // The target last applied, by monitor id; empty for desktop modes. Null before the first one.
    private Dictionary<string, ModeRequest>? target;

    // Monitors already handled for the current target (changed, already there or failed); a
    // display change applies the target only to the monitors that were not handled yet.
    private readonly HashSet<string> handled = new(StringComparer.OrdinalIgnoreCase);

    private bool applying;
    private bool recheck;
    private bool recoveryChecked;

    /// <summary>
    /// Raised after a change of mode: the NVIDIA display handles may no longer be valid and the
    /// colors may have been reset, so the displays are refreshed and every color written again.
    /// </summary>
    public event Action? ModesChanged;

    /// <summary>Raised when a target arrived while another one was being applied, so the active profile is checked again.</summary>
    public event Action? RecheckRequested;

    /// <summary>Whether this controller owns the mode of any monitor (testing and diagnostics).</summary>
    public bool OwnsAnyMonitor => owned.Count > 0;

    /// <summary>
    /// Applies the resolutions and refresh rates of <paramref name="profile"/>, or the desktop modes when it is null,
    /// has none, or Global Switching is off. Nothing happens when the target is the one already applied.
    /// </summary>
    public void Apply(ProfileListItem? profile)
    {
        var next = TargetOf(switching.GlobalPaused ? null : profile);
        if (next.Count == 0 && owned.Count == 0)
        {
            target = next;
            handled.Clear();
            return;
        }

        if (target is not null && SameTarget(target, next))
            return;

        Transition(next);
    }

    /// <summary>Restores every monitor this controller changed: on exit, Global Switching off and Windows Override.</summary>
    public void RestoreOriginals()
    {
        if (owned.Count == 0)
        {
            target = [];
            handled.Clear();
            return;
        }

        Transition([]);
    }

    /// <summary>
    /// After the displays were enumerated again: a monitor that came back gets the current target,
    /// and a monitor still waiting to be restored (disconnected or failed before) is restored.
    /// A connected monitor already handled is never changed again, so a mode a game or the user
    /// set stays; one that was missing from an enumeration counts as new when it returns, since
    /// Windows gives a reconnected monitor its desktop mode.
    /// The first call, at the first enumeration, first takes over what a previous session left
    /// changed, so those monitors are restored as well.
    /// </summary>
    public void OnDisplaysChanged()
    {
        if (!recoveryChecked)
        {
            recoveryChecked = true;
            RecoverPreviousRun();
        }

        if (owned.Count == 0 && (target is null || target.Count == 0))
            return;

        Transition(target ?? [], displaysChanged: true);
    }

    // Takes over, from the recovery file, the monitors a previous session left changed; the
    // display change that follows restores them. Only the existence of the file is checked when
    // there is none. A monitor this session already owns keeps its own entry, and a monitor that
    // is not connected keeps its entry until it is.
    private void RecoverPreviousRun()
    {
        if (!recovery.Exists)
            return;

        foreach (var entry in recovery.Load() ?? [])
            owned.TryAdd(entry.MonitorId, new Owned(entry.Original, entry.Target, entry.Applied));
    }

    /// <summary>
    /// The desktop mode of <paramref name="display"/> (the one it is restored to, while this
    /// controller owns it) and the modes it supports, which the editor offers from; null when they
    /// cannot be read. Read for the display selected in the editor of an application profile.
    /// </summary>
    public DisplayModeOptions? ModeOptions(ConnectedDisplay display)
    {
        if (string.IsNullOrWhiteSpace(display.GdiName))
            return null;

        try
        {
            var desktop = owned.TryGetValue(display.MonitorId, out var monitor) ? monitor.Original : backend.GetCurrent(display.GdiName);
            return desktop is { } mode ? new DisplayModeOptions(mode, backend.GetModes(display.GdiName)) : null;
        }
        catch (Exception)
        {
            // The lists then offer only the defaults and the saved values.
            return null;
        }
    }

    private void Transition(Dictionary<string, ModeRequest> next, bool displaysChanged = false)
    {
        if (applying)
        {
            recheck = true;
            return;
        }

        applying = true;
        var changed = false;
        try
        {
            var displays = connectedDisplays.Displays;
            if (!displaysChanged)
            {
                target = next;
                handled.Clear();
            }
            else
            {
                // A monitor missing now gets the target again when it returns.
                handled.RemoveWhere(id => !displays.Any(display => DisplayCore.SameMonitorId(display.MonitorId, id)));
            }

            foreach (var display in displays)
            {
                if (string.IsNullOrWhiteSpace(display.MonitorId) || string.IsNullOrWhiteSpace(display.GdiName))
                    continue;

                try
                {
                    if (next.TryGetValue(display.MonitorId, out var request))
                    {
                        if (handled.Add(display.MonitorId))
                            changed |= ApplyMode(display, request);
                    }
                    else if (owned.TryGetValue(display.MonitorId, out var monitor))
                    {
                        changed |= Restore(display, monitor);
                    }
                }
                catch (Exception)
                {
                    // Unexpected: this monitor keeps its recovery entry, if it has one, for the next
                    // display change or start; the other monitors and the colors go on. No refresh is
                    // requested for it, so a failure that repeats never loops; a mode that did change
                    // sends its own WM_DISPLAYCHANGE.
                }
            }
        }
        finally
        {
            applying = false;
        }

        if (changed)
            ModesChanged?.Invoke();
        if (recheck)
        {
            recheck = false;
            RecheckRequested?.Invoke();
        }
    }

    // True when the mode changed.
    private bool ApplyMode(ConnectedDisplay display, ModeRequest request)
    {
        owned.TryGetValue(display.MonitorId, out var previous);
        var current = backend.GetCurrent(display.GdiName);
        if (current is not { } now)
            return false;

        // The desktop mode is read before the first change and kept through every profile after it.
        var desktop = previous?.Original ?? now;
        if (DisplayModeCore.ChooseMode(backend.GetModes(display.GdiName), request.Resolution, desktop, request.RefreshRate) is not { } mode)
            return false;
        if (now == mode)
            return false;

        // Recorded before the change; a change that cannot be recorded is not made.
        owned[display.MonitorId] = new Owned(desktop, mode, previous?.Applied);
        if (!SaveRecovery())
        {
            Revert(display.MonitorId, previous);
            return false;
        }

        var applied = backend.Apply(display.GdiName, mode);
        var after = backend.GetCurrent(display.GdiName);
        if (after == desktop && previous is null)
        {
            // Nothing changed: the monitor is not ours.
            owned.Remove(display.MonitorId);
            SaveRecovery();
            return false;
        }

        owned[display.MonitorId] = new Owned(desktop, mode, after ?? (applied ? mode : previous?.Applied));
        SaveRecovery();
        return after != now;
    }

    // True when the mode changed. The entry stays until the monitor shows its desktop mode again.
    private bool Restore(ConnectedDisplay display, Owned monitor)
    {
        var current = backend.GetCurrent(display.GdiName);
        if (current is not { } now)
            return false;

        if (now == monitor.Original || (now != monitor.Applied && now != monitor.Target))
        {
            // Already back, or changed by the user or a game since: nothing to restore.
            owned.Remove(display.MonitorId);
            SaveRecovery();
            return false;
        }

        backend.Apply(display.GdiName, monitor.Original);
        var after = backend.GetCurrent(display.GdiName);
        if (after == monitor.Original)
        {
            owned.Remove(display.MonitorId);
            SaveRecovery();
        }

        return after != now;
    }

    private void Revert(string monitorId, Owned? previous)
    {
        if (previous is null)
            owned.Remove(monitorId);
        else
            owned[monitorId] = previous;
    }

    private bool SaveRecovery() =>
        recovery.Save(owned.Select(pair => new DisplayModeRecoveryEntry(pair.Key, pair.Value.Original, pair.Value.Target, pair.Value.Applied)).ToList());

    // The mode each monitor of the profile asks for: a resolution, a refresh rate, or both.
    private static Dictionary<string, ModeRequest> TargetOf(ProfileListItem? profile)
    {
        var requests = new Dictionary<string, ModeRequest>(StringComparer.OrdinalIgnoreCase);
        if (profile is null || profile.IsWindowsProfile)
            return requests;

        foreach (var values in profile.DisplayProfiles)
        {
            var resolution = values.Resolution is { IsValid: true } valid ? valid : null;
            var rate = values.RefreshRate is { IsValid: true } validRate ? validRate : null;
            if ((resolution is not null || rate is not null) && !string.IsNullOrWhiteSpace(values.MonitorId))
                requests[values.MonitorId] = new ModeRequest(resolution, rate);
        }

        return requests;
    }

    private static bool SameTarget(Dictionary<string, ModeRequest> a, Dictionary<string, ModeRequest> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && other == pair.Value);

    // A resolution, a refresh rate, or both; null keeps the desktop one.
    private sealed record ModeRequest(DisplayResolution? Resolution, RefreshRateSetting? RefreshRate);

    private sealed record Owned(DisplayModeInfo Original, DisplayModeInfo Target, DisplayModeInfo? Applied);
}
