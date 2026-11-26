using NvProfileSwitcher.Nvidia;

namespace NvProfileSwitcher.Core.Tests;

// An NVIDIA API test double that records its creation and release in a shared log.
internal sealed class FakeDisplayBackend : IDisplayColorBackend
{
    private readonly List<string> log;

    public FakeDisplayBackend(int number, List<string> log, FakeDisplayModes? modes = null)
    {
        Number = number;
        this.log = log;
        Modes = modes ?? new FakeDisplayModes();
        log.Add($"create {number}");
    }

    /// <summary>The desktop resolutions, which outlive an instance as the system's do.</summary>
    public FakeDisplayModes Modes { get; }

    public int Number { get; }
    public int DisposeCount { get; private set; }
    public string DriverVersion { get; set; } = "581.57";
    public List<NvidiaDisplay> Displays { get; } = [];
    public Dictionary<string, int?> Vibrance { get; } = [];
    public Dictionary<string, int?> Hue { get; } = [];

    /// <summary>The NVIDIA Control Panel device id of each monitor; none unless set.</summary>
    public Dictionary<string, uint?> NvTweakDeviceIds { get; } = [];
    public Exception? EnumerationFailure { get; set; }
    public int EnumerationCount { get; private set; }

    /// <summary>Every color application, in order, including the ones the driver rejected.</summary>
    public List<ColorApplication> Applied { get; } = [];

    /// <summary>The last values applied to the display: what it shows now, or null when nothing was applied.</summary>
    public ColorApplication? Current(string monitorId) =>
        Applied.Where(a => a.MonitorId == monitorId).Select(a => (ColorApplication?)a).LastOrDefault();

    /// <summary>Displays whose application the driver rejects (Apply returns false).</summary>
    public HashSet<string> RejectedMonitors { get; } = [];

    /// <summary>An exception thrown when applying to a display, as a failing native call would.</summary>
    public Func<NvidiaDisplay, Exception?>? ApplyFailure { get; set; }

    public IReadOnlyList<NvidiaDisplay> EnumerateDisplays()
    {
        EnumerationCount++;
        return EnumerationFailure is null ? Displays.ToArray() : throw EnumerationFailure;
    }

    public int? GetVibrance(NvidiaDisplay display) => Vibrance.GetValueOrDefault(display.MonitorId);

    public int? GetHue(NvidiaDisplay display) => Hue.GetValueOrDefault(display.MonitorId);

    public uint? GetNvTweakDeviceId(NvidiaDisplay display) => NvTweakDeviceIds.GetValueOrDefault(display.MonitorId);

    public bool Apply(NvidiaDisplay display, int vibrance, double brightness, double contrast, double gamma, int hue)
    {
        if (ApplyFailure?.Invoke(display) is { } failure)
            throw failure;
        Applied.Add(new ColorApplication(display.MonitorId, vibrance, brightness, contrast, gamma, hue));
        Modes.Timeline.Add($"colors {display.MonitorId} handle {display.Handle}");
        return !RejectedMonitors.Contains(display.MonitorId);
    }

    public DisplayResolution? GetResolution(NvidiaDisplay display) => Modes.Get(display);

    public bool SetResolution(NvidiaDisplay display, DisplayResolution resolution) => Modes.Set(display, resolution);

    public IReadOnlyList<DisplayResolution> GetSupportedResolutions(NvidiaDisplay display) =>
        Modes.Supported.TryGetValue(display.DisplayId, out var supported) ? supported : [];

    public void Dispose()
    {
        DisposeCount++;
        log.Add($"dispose {Number}");
    }

    /// <summary>
    /// A display whose monitor has the identity <paramref name="monitorId"/>: "{hardware}-{serial}"
    /// for a monitor with a serial number, "{hardware}" for one without (identified by its
    /// connection, the display number), or blank for a monitor whose identity cannot be read.
    /// </summary>
    internal static NvidiaDisplay Display(string monitorId, string name, int number = 1) =>
        new($@"\\.\DISPLAY{number}", name, Handle: number, DisplayId: (uint)number, Primary: number == 1, monitorId)
        {
            Monitor = Monitor(monitorId, number)
        };

    internal static MonitorDescriptor? Monitor(string monitorId, int number)
    {
        if (string.IsNullOrWhiteSpace(monitorId)) return null;
        var dash = monitorId.IndexOf('-');
        return dash < 0
            ? new MonitorDescriptor(monitorId, Instance(number), null)
            : new MonitorDescriptor(monitorId[..dash], Instance(number), monitorId[(dash + 1)..]);
    }

    /// <summary>The device instance of the connection of display <paramref name="number"/>.</summary>
    internal static string Instance(int number) => $"5&1a2b3c4d&0&UID{4350 + number}";
}

internal readonly record struct ColorApplication(
    string MonitorId, int Vibrance, double Brightness, double Contrast, double Gamma, int Hue)
{
    public bool Matches(ProfileDisplaySettings values) =>
        values.DigitalVibrance == Vibrance && values.Brightness == Brightness && values.Contrast == Contrast &&
        values.Gamma == Gamma && values.Hue == Hue;
}

// The desktop resolution of each display, by NVIDIA display id: what the driver reports and
// applies. Every change and every color write is recorded in one timeline, in order.
internal sealed class FakeDisplayModes
{
    public Dictionary<uint, DisplayResolution> Current { get; } = [];
    public Dictionary<uint, List<DisplayResolution>> Supported { get; } = [];

    /// <summary>Displays whose resolution changes the driver rejects.</summary>
    public HashSet<uint> Rejected { get; } = [];

    /// <summary>Accepted changes, in order.</summary>
    public List<(uint DisplayId, DisplayResolution Resolution)> Changes { get; } = [];

    /// <summary>"resolution {id} {w}x{h}" and "colors {monitor} handle {handle}", in order.</summary>
    public List<string> Timeline { get; } = [];

    /// <summary>Runs after an accepted change, as the WM_DISPLAYCHANGE Windows sends would.</summary>
    public Action? AfterChange { get; set; }

    public void Add(uint displayId, DisplayResolution current, params DisplayResolution[] supported)
    {
        Current[displayId] = current;
        Supported[displayId] = [current, .. supported.Where(resolution => resolution != current)];
    }

    public DisplayResolution? Get(NvidiaDisplay display) => Current.TryGetValue(display.DisplayId, out var resolution) ? resolution : null;

    public bool Set(NvidiaDisplay display, DisplayResolution resolution)
    {
        if (Rejected.Contains(display.DisplayId) || !Supported.TryGetValue(display.DisplayId, out var supported) || !supported.Contains(resolution))
            return false;

        Current[display.DisplayId] = resolution;
        Changes.Add((display.DisplayId, resolution));
        Timeline.Add($"resolution {display.DisplayId} {resolution.Width}x{resolution.Height}");
        AfterChange?.Invoke();
        return true;
    }
}

// Creates fake backends in sequence; a creation can be set to fail as NVAPI initialization would.
internal sealed class FakeBackendFactory
{
    /// <summary>The desktop resolutions every created backend shares.</summary>
    public FakeDisplayModes Modes { get; } = new();

    public List<string> Log { get; } = [];
    public List<FakeDisplayBackend> Created { get; } = [];
    public Exception? NextFailure { get; set; }
    public Action<FakeDisplayBackend>? Configure { get; set; }

    public IDisplayColorBackend Create()
    {
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            Log.Add("create failed");
            throw failure;
        }

        var backend = new FakeDisplayBackend(Created.Count + 1, Log, Modes);
        Configure?.Invoke(backend);
        Created.Add(backend);
        return backend;
    }
}
