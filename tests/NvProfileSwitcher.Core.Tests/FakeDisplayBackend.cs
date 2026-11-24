using NvProfileSwitcher.Nvidia;
using NvProfileSwitcher.Platform;

namespace NvProfileSwitcher.Core.Tests;

// An NVIDIA API test double that records its creation and release in a shared log. The
// displays are the ones Windows reports (FakeBackendFactory.Displays); the API controls all of
// them except the monitors in NotControlled.
internal sealed class FakeDisplayBackend : IDisplayColorBackend
{
    private readonly List<string> log;
    private readonly FakeBackendFactory factory;

    public FakeDisplayBackend(int number, List<string> log, FakeBackendFactory factory)
    {
        Number = number;
        this.log = log;
        this.factory = factory;
        log.Add($"create {number}");
    }

    public int Number { get; }
    public int DisposeCount { get; private set; }
    public string DriverVersion { get; set; } = "581.57";
    /// <summary>The displays Windows reports: the factory's, shared by every instance.</summary>
    public List<ConnectedDisplay> Displays => factory.Displays;

    /// <summary>Monitors the NVIDIA driver does not control, as on another adapter.</summary>
    public HashSet<string> NotControlled { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int?> Vibrance { get; } = [];
    public Dictionary<string, int?> Hue { get; } = [];

    /// <summary>The NVIDIA Control Panel device id of each monitor; none unless set.</summary>
    public Dictionary<string, uint?> NvTweakDeviceIds { get; } = [];
    public Exception? EnumerationFailure { get; set; }
    public int EnumerationCount { get; internal set; }

    /// <summary>Every color application, in order, including the ones the driver rejected.</summary>
    public List<ColorApplication> Applied { get; } = [];

    /// <summary>The last values applied to the display: what it shows now, or null when nothing was applied.</summary>
    public ColorApplication? Current(string monitorId) =>
        Applied.Where(a => a.MonitorId == monitorId).Select(a => (ColorApplication?)a).LastOrDefault();

    /// <summary>Displays whose application the driver rejects (Apply returns false).</summary>
    public HashSet<string> RejectedMonitors { get; } = [];

    /// <summary>An exception thrown when applying to a display, as a failing native call would.</summary>
    public Func<ConnectedDisplay, Exception?>? ApplyFailure { get; set; }

    public NvidiaHandle? ResolveDisplay(string gdiName)
    {
        var display = Displays.FirstOrDefault(d => d.GdiName == gdiName);
        if (display is null || NotControlled.Contains(display.Monitor is { } monitor ? Identity(monitor) : string.Empty))
            return null;
        var number = int.Parse(gdiName[@"\\.\DISPLAY".Length..], System.Globalization.CultureInfo.InvariantCulture);
        return new NvidiaHandle(number, (uint)number);
    }

    public int? GetVibrance(ConnectedDisplay display) => Vibrance.GetValueOrDefault(display.MonitorId);

    public int? GetHue(ConnectedDisplay display) => Hue.GetValueOrDefault(display.MonitorId);

    public uint? GetNvTweakDeviceId(ConnectedDisplay display) => NvTweakDeviceIds.GetValueOrDefault(display.MonitorId);

    public bool Apply(ConnectedDisplay display, int vibrance, double brightness, double contrast, double gamma, int hue)
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
    internal static ConnectedDisplay Display(string monitorId, string name, int number = 1) =>
        new($@"\\.\DISPLAY{number}", name, Primary: number == 1, monitorId)
        {
            Monitor = Monitor(monitorId, number),
            // Controlled by the NVIDIA API, as the displays of most tests; DisplayService sets the
            // handle the API actually reports.
            Nvidia = new NvidiaHandle(number, (uint)number)
        };

    // The id a test gives a monitor: "{hardware}-{serial}", or "{hardware}" without a serial.
    private static string Identity(MonitorDescriptor monitor) =>
        monitor.Serial is null ? monitor.Hardware : $"{monitor.Hardware}-{monitor.Serial}";

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

// Creates fake backends in sequence; a creation can be set to fail as NVAPI initialization would,
// once (NextFailure) or every time (Failure: no NVIDIA driver). It is also Windows: the displays
// connected to the desktop, whatever adapter drives them. Each creation configures them again, as
// each instance used to enumerate its own; Configure also runs on the first enumeration when no
// instance could be created, so the same displays are connected without the NVIDIA API.
internal sealed class FakeBackendFactory : IDisplayEnumerator
{
    private bool configured;

    public List<string> Log { get; } = [];
    public List<FakeDisplayBackend> Created { get; } = [];
    public Exception? NextFailure { get; set; }

    /// <summary>Thrown by every creation, as when no NVIDIA driver is installed.</summary>
    public Exception? Failure { get; set; }
    public Action<FakeDisplayBackend>? Configure { get; set; }

    /// <summary>The displays connected to the desktop.</summary>
    public List<ConnectedDisplay> Displays { get; } = [];

    /// <summary>Enumerations made while no instance existed.</summary>
    public int EnumerationCount { get; private set; }

    public IDisplayColorBackend Create()
    {
        if ((NextFailure ?? Failure) is { } failure)
        {
            NextFailure = null;
            Log.Add("create failed");
            throw failure;
        }

        var backend = new FakeDisplayBackend(Created.Count + 1, Log, this);
        Displays.Clear();
        configured = true;
        Configure?.Invoke(backend);
        Created.Add(backend);
        return backend;
    }

    public IReadOnlyList<ConnectedDisplay> EnumerateDisplays()
    {
        if (Created.Count == 0)
        {
            if (!configured)
            {
                configured = true;
                Configure?.Invoke(new FakeDisplayBackend(0, [], this));
            }

            EnumerationCount++;
            return Displays.ToArray();
        }

        var current = Created[^1];
        current.EnumerationCount++;
        return current.EnumerationFailure is null ? Displays.ToArray() : throw current.EnumerationFailure;
    }
}
