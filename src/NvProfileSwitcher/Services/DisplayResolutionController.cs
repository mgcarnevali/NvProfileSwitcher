using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Nvidia;

namespace NvProfileSwitcher.Services;

/// <summary>
/// The only writer of display resolutions. It applies the resolutions of the active application
/// profile through the NVIDIA driver (<see cref="NvidiaService.SetResolution"/>), for this session
/// only, and restores each monitor's desktop resolution when the profile stops applying it.
/// <para>
/// For each monitor it changed it keeps the desktop resolution and the one applied, decided by the
/// driver's answer, never by reading the monitor back right after the change. A monitor is
/// restored only while it still shows the resolution applied: a resolution the user set since is
/// theirs and is left alone. Each change is recorded in the recovery file before it is made, so
/// the next start restores what a session that ended without restoring left changed.
/// </para>
/// It returns the monitors whose resolution changed, so their colors are written by the display
/// refresh that follows (<see cref="DisplayColorController.AwaitRefresh"/>), and reports the change
/// (<see cref="ResolutionsChanged"/>) so that refresh is requested.
/// </summary>
internal sealed class DisplayResolutionController(NvidiaService nvidia, SwitchingState switching, ResolutionRecoveryStore recovery)
{
    private readonly Dictionary<string, Owned> owned = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DisplayResolution> target = new(StringComparer.OrdinalIgnoreCase);
    private bool recoveryChecked;

    /// <summary>Raised after a resolution changed, so the displays are refreshed.</summary>
    public event Action? ResolutionsChanged;

    /// <summary>Whether this controller changed the resolution of any monitor (tests and diagnostics).</summary>
    public bool OwnsAnyMonitor => owned.Count > 0;

    /// <summary>
    /// Applies the resolutions of <paramref name="profile"/>, and restores the desktop resolution of
    /// the monitors it does not change: nothing for the Windows profile, a profile without
    /// resolutions or while Global Switching is off. Nothing happens when the target is the one
    /// already applied. Returns the monitors whose resolution changed.
    /// </summary>
    public IReadOnlyCollection<string> Apply(ProfileListItem? profile)
    {
        var next = TargetOf(switching.GlobalPaused ? null : profile);
        if (SameTarget(target, next))
            return [];

        target = next;
        return Transition();
    }

    /// <summary>Restores every monitor this controller changed: closing, Global Switching off. Returns the monitors restored.</summary>
    public IReadOnlyCollection<string> RestoreOriginals()
    {
        target = new Dictionary<string, DisplayResolution>(StringComparer.OrdinalIgnoreCase);
        return owned.Count == 0 ? [] : Transition();
    }

    /// <summary>
    /// After the displays were enumerated again: the first time, it takes over the monitors a
    /// previous session left changed (the recovery file); then it restores the monitors still
    /// waiting to be restored that are connected now. A monitor the current target changes is not
    /// changed again. Returns the monitors restored.
    /// </summary>
    public IReadOnlyCollection<string> OnDisplaysChanged()
    {
        if (!recoveryChecked)
        {
            recoveryChecked = true;
            foreach (var entry in recovery.Load())
                owned.TryAdd(entry.MonitorId, new Owned(entry.Original, entry.Applied));
        }

        if (owned.Count == 0)
            return [];

        var changed = new List<string>();
        foreach (var display in nvidia.Displays)
        {
            if (!target.ContainsKey(display.MonitorId) && owned.TryGetValue(display.MonitorId, out var monitor) && Restore(display, monitor))
                changed.Add(display.MonitorId);
        }

        return Changed(changed);
    }

    /// <summary>The resolutions <paramref name="display"/> offers; none when they cannot be read.</summary>
    public IReadOnlyList<DisplayResolution> SupportedResolutions(NvidiaDisplay display)
    {
        try
        {
            return nvidia.GetSupportedResolutions(display);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private IReadOnlyCollection<string> Transition()
    {
        var changed = new List<string>();
        foreach (var display in nvidia.Displays)
        {
            try
            {
                if (target.TryGetValue(display.MonitorId, out var resolution))
                {
                    if (ApplyTo(display, resolution))
                        changed.Add(display.MonitorId);
                }
                else if (owned.TryGetValue(display.MonitorId, out var monitor) && Restore(display, monitor))
                {
                    changed.Add(display.MonitorId);
                }
            }
            catch (Exception)
            {
                // Unexpected: this monitor keeps its recovery entry, if it has one; the other
                // monitors and the colors go on.
            }
        }

        return Changed(changed);
    }

    // True when the resolution changed.
    private bool ApplyTo(NvidiaDisplay display, DisplayResolution resolution)
    {
        if (nvidia.GetResolution(display) is not { } current)
            return false;

        if (owned.TryGetValue(display.MonitorId, out var previous) && current != previous.Applied)
        {
            // Changed by the user since: their resolution is the desktop one from now on.
            owned.Remove(display.MonitorId);
            previous = null;
            recovery.Save(Entries());
        }

        if (current == resolution)
            return false;

        // Recorded before the change; a change that cannot be recorded is not made.
        owned[display.MonitorId] = new Owned(previous?.Original ?? current, resolution);
        if (!recovery.Save(Entries()) || !nvidia.SetResolution(display, resolution))
        {
            if (previous is null)
                owned.Remove(display.MonitorId);
            else
                owned[display.MonitorId] = previous;
            recovery.Save(Entries());
            return false;
        }

        return true;
    }

    // True when the resolution changed. The entry stays when the monitor cannot be restored now.
    private bool Restore(NvidiaDisplay display, Owned monitor)
    {
        if (nvidia.GetResolution(display) is not { } current)
            return false;

        // Already back, or changed by the user since: nothing to restore.
        if (current == monitor.Original || current != monitor.Applied)
        {
            owned.Remove(display.MonitorId);
            recovery.Save(Entries());
            return false;
        }

        if (!nvidia.SetResolution(display, monitor.Original))
            return false;

        owned.Remove(display.MonitorId);
        recovery.Save(Entries());
        return true;
    }

    private IReadOnlyCollection<string> Changed(List<string> changed)
    {
        if (changed.Count > 0)
            ResolutionsChanged?.Invoke();
        return changed;
    }

    private List<ResolutionRecoveryEntry> Entries() =>
        owned.Select(pair => new ResolutionRecoveryEntry(pair.Key, pair.Value.Original, pair.Value.Applied)).ToList();

    private static Dictionary<string, DisplayResolution> TargetOf(ProfileListItem? profile)
    {
        var resolutions = new Dictionary<string, DisplayResolution>(StringComparer.OrdinalIgnoreCase);
        if (profile is null || profile.IsWindowsProfile)
            return resolutions;

        foreach (var values in profile.DisplayProfiles)
        {
            if (values.Resolution is { IsValid: true } resolution && !string.IsNullOrWhiteSpace(values.MonitorId))
                resolutions[values.MonitorId] = resolution;
        }

        return resolutions;
    }

    private static bool SameTarget(Dictionary<string, DisplayResolution> a, Dictionary<string, DisplayResolution> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && other == pair.Value);

    // A monitor this controller changed: its desktop resolution and the one applied.
    private sealed record Owned(DisplayResolution Original, DisplayResolution Applied);
}
