using NvProfileSwitcher.Nvidia;

namespace NvProfileSwitcher.Core.Tests;

// An NVIDIA API test double that records its creation and release in a shared log.
internal sealed class FakeDisplayBackend : IDisplayColorBackend
{
    private readonly List<string> log;

    public FakeDisplayBackend(int number, List<string> log)
    {
        Number = number;
        this.log = log;
        log.Add($"create {number}");
    }

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
        return !RejectedMonitors.Contains(display.MonitorId);
    }

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

// Creates fake backends in sequence; a creation can be set to fail as NVAPI initialization would.
internal sealed class FakeBackendFactory
{
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

        var backend = new FakeDisplayBackend(Created.Count + 1, Log);
        Configure?.Invoke(backend);
        Created.Add(backend);
        return backend;
    }
}
