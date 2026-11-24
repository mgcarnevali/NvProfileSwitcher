using NvProfileSwitcher.Core;
using NvProfileSwitcher.Platform;

namespace NvProfileSwitcher.Core.Tests;

// A display mode test double: each monitor, by GDI device name, has its supported modes and a
// current mode. Every call is recorded, so tests can check that nothing reached it.
internal sealed class FakeDisplayModeBackend : IDisplayModeBackend
{
    public Dictionary<string, List<DisplayModeInfo>> Supported { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DisplayModeInfo> Current { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every call, in order: "modes \\.\DISPLAY1", "current …", "apply … 1280x960@144".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Modes applied successfully, in order, with the monitor.</summary>
    public List<(string GdiName, DisplayModeInfo Mode)> Applied { get; } = [];

    /// <summary>Monitors whose changes Windows rejects.</summary>
    public HashSet<string> Rejected { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs inside a change, as the WM_DISPLAYCHANGE Windows sends during it would.</summary>
    public Action? DuringApply { get; set; }

    public void AddMonitor(string gdiName, DisplayModeInfo current, params DisplayModeInfo[] supported)
    {
        Current[gdiName] = current;
        Supported[gdiName] = [current, .. supported.Where(mode => mode != current)];
    }

    public IReadOnlyList<DisplayModeInfo> GetModes(string gdiName)
    {
        Calls.Add($"modes {gdiName}");
        return Supported.TryGetValue(gdiName, out var modes) ? modes : [];
    }

    public DisplayModeInfo? GetCurrent(string gdiName)
    {
        Calls.Add($"current {gdiName}");
        return Current.TryGetValue(gdiName, out var mode) ? mode : null;
    }

    public bool Apply(string gdiName, DisplayModeInfo mode)
    {
        Calls.Add($"apply {gdiName} {mode.Width}x{mode.Height}@{mode.RefreshRate}");
        if (Rejected.Contains(gdiName) || !Supported.TryGetValue(gdiName, out var modes) || !modes.Contains(mode))
            return false;

        Current[gdiName] = mode;
        Applied.Add((gdiName, mode));
        DuringApply?.Invoke();
        return true;
    }
}
