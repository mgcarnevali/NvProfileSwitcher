using NvProfileSwitcher.Nvidia;
using System.ComponentModel;
using NvProfileSwitcher.Services;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

public sealed class NvidiaServiceTests
{
    private readonly FakeBackendFactory factory = new();

    private NvidiaService CreateService() => new(factory.Create);

    [Fact]
    public void InitialStateMatchesTheOriginalStatusBar()
    {
        using var nvidia = CreateService();

        Assert.True(nvidia.IsAvailable);
        Assert.Equal("Unknown", nvidia.DriverVersion);
        Assert.False(nvidia.IsInitialized);
    }

    [Fact]
    public void InitializationReportsAvailabilityAndDriverVersion()
    {
        using var nvidia = CreateService();
        factory.Configure = backend => backend.DriverVersion = "560.94";

        nvidia.Initialize();

        Assert.True(nvidia.IsInitialized);
        Assert.True(nvidia.IsAvailable);
        Assert.Equal("560.94", nvidia.DriverVersion);
    }

    // Each Opened initializes again. The new instance is created first and the previous one
    // is released afterwards, so exactly one instance stays alive and none is left behind.
    [Fact]
    public void EachInitializationReplacesThePreviousInstanceAfterTheNewOneSucceeded()
    {
        var nvidia = CreateService();

        nvidia.Initialize();
        nvidia.Initialize();
        nvidia.Initialize();

        Assert.Equal(["create 1", "create 2", "dispose 1", "create 3", "dispose 2"], factory.Log);
        Assert.Equal([1, 1, 0], factory.Created.Select(b => b.DisposeCount));

        nvidia.Dispose();
        Assert.Equal([1, 1, 1], factory.Created.Select(b => b.DisposeCount));
    }

    [Fact]
    public void FailedInitializationKeepsTheCurrentInstanceWithoutReleasingAnything()
    {
        using var nvidia = CreateService();
        nvidia.Initialize();
        factory.NextFailure = new InvalidOperationException("NvAPI_Initialize failed: -1");

        Assert.Throws<InvalidOperationException>(nvidia.Initialize);

        Assert.True(nvidia.IsInitialized);
        Assert.Equal(["create 1", "create failed"], factory.Log);
        Assert.Equal(0, factory.Created[0].DisposeCount);
    }

    [Fact]
    public void UnavailableReportKeepsTheCurrentInstanceInUse()
    {
        using var nvidia = CreateService();
        nvidia.Initialize();
        factory.Created[0].DriverVersion = "566.03";

        nvidia.ReportUnavailable();

        Assert.False(nvidia.IsAvailable);
        Assert.Equal("Unknown", nvidia.DriverVersion);
        Assert.True(nvidia.IsInitialized);

        nvidia.RefreshDriverVersion();
        Assert.Equal("566.03", nvidia.DriverVersion);
        Assert.False(nvidia.IsAvailable);
    }

    [Fact]
    public void FailedFirstInitializationIsRetriedByTheNextOne()
    {
        using var nvidia = CreateService();
        factory.NextFailure = new DllNotFoundException("nvapi64.dll");

        Assert.Throws<DllNotFoundException>(nvidia.Initialize);
        Assert.False(nvidia.IsInitialized);

        nvidia.Initialize();
        Assert.True(nvidia.IsInitialized);
        Assert.Single(factory.Created);
    }

    [Fact]
    public void DriverVersionRefreshIgnoresUnknownAndUnchangedVersions()
    {
        using var nvidia = CreateService();
        nvidia.RefreshDriverVersion();
        Assert.Equal("Unknown", nvidia.DriverVersion);

        nvidia.Initialize();
        var changes = new List<string?>();
        nvidia.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        var backend = factory.Created[0];

        backend.DriverVersion = "Unknown";
        nvidia.RefreshDriverVersion();
        nvidia.RefreshDriverVersion();
        Assert.Equal("581.57", nvidia.DriverVersion);
        Assert.Empty(changes);

        backend.DriverVersion = "582.10";
        nvidia.RefreshDriverVersion();
        Assert.Equal("582.10", nvidia.DriverVersion);
        Assert.Equal([nameof(NvidiaService.DriverVersion)], changes);
    }

    // The displays come from Windows: without the API they are there, without colors; once it is
    // initialized, the displays it controls get their handle with the next refresh.
    [Fact]
    public void DisplaysAreEnumeratedWithOrWithoutTheApi()
    {
        using var nvidia = CreateService();
        var display = FakeDisplayBackend.Display("DEL4321-ABC123", "DELL U2720Q");
        factory.Configure = backend => backend.Displays.Add(display);
        var displays = new DisplayService(factory, nvidia);

        displays.Refresh();
        var withoutApi = Assert.Single(displays.Displays);
        Assert.False(withoutApi.CanAdjustColors);
        Assert.Null(nvidia.GetVibrance(withoutApi));
        Assert.Null(nvidia.GetHue(withoutApi));
        Assert.False(nvidia.Apply(withoutApi, 60, 100, 100, 1, 0));

        nvidia.Initialize();
        displays.Refresh();
        Assert.Equal(display, Assert.Single(displays.Displays));
        Assert.True(displays.Displays[0].CanAdjustColors);
    }

    // A display on another adapter is listed with its resolution but without colors, next to the
    // displays the NVIDIA API controls.
    [Fact]
    public void ADisplayTheApiDoesNotControlHasNoColors()
    {
        using var nvidia = CreateService();
        factory.Configure = backend =>
        {
            backend.Displays.Add(FakeDisplayBackend.Display("DEL4321-ABC123", "DELL U2720Q"));
            backend.Displays.Add(FakeDisplayBackend.Display("BOE0A1B", "Built-in display", 2));
            backend.NotControlled.Add("BOE0A1B");
        };
        nvidia.Initialize();
        var displays = new DisplayService(factory, nvidia);

        displays.Refresh();

        Assert.Equal([true, false], displays.Displays.Select(d => d.CanAdjustColors));
        Assert.EndsWith("Colors unavailable", displays.Displays[1].SelectorLabel);
        Assert.DoesNotContain("Colors unavailable", displays.Displays[0].SelectorLabel);
    }

    [Theory]
    [InlineData("dll", "NotDetected")]
    [InlineData("no-gpu", "NotDetected")]
    [InlineData("init", "Unavailable")]
    [InlineData("other", "Unavailable")]
    public void AFailedInitializationTellsWhetherTheDriverIsMissing(string kind, string expected)
    {
        using var nvidia = CreateService();
        factory.NextFailure = kind switch
        {
            "dll" => new DllNotFoundException("nvapi64.dll"),
            "no-gpu" => new NvApiInitializationException(NvApiInitializationException.NvidiaDeviceNotFound),
            "init" => new NvApiInitializationException(-1),
            _ => new EntryPointNotFoundException("nvapi_QueryInterface"),
        };

        Assert.False(nvidia.TryInitialize());

        Assert.Equal(expected, nvidia.Status.ToString());
        Assert.False(nvidia.IsAvailable);
        Assert.False(nvidia.IsInitialized);
        Assert.Equal("Unknown", nvidia.DriverVersion);
        Assert.True(nvidia.TryInitialize());
        Assert.Equal(NvidiaStatus.Available, nvidia.Status);
    }

    [Fact]
    public void AFailingHandleResolutionLeavesTheDisplayWithoutColors()
    {
        using var nvidia = CreateService();
        factory.Configure = backend => backend.Displays.Add(FakeDisplayBackend.Display("DEL4321-ABC123", "DELL U2720Q", 7));
        nvidia.Initialize();
        factory.Displays.Add(FakeDisplayBackend.Display("GSM5B7F-1", "LG", 2) with { GdiName = "not a display name" });
        var displays = new DisplayService(factory, nvidia);

        displays.Refresh();

        Assert.Equal([true, false], displays.Displays.Select(d => d.CanAdjustColors));
    }

    [Fact]
    public void AvailabilityChangesAreNotified()
    {
        using var nvidia = CreateService();
        var changes = new List<string?>();
        nvidia.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        nvidia.ReportUnavailable();

        Assert.Contains(nameof(NvidiaService.IsAvailable), changes);
        Assert.IsAssignableFrom<INotifyPropertyChanged>(nvidia);
    }

    // The library loaded at start keeps reporting its own driver after another one is installed,
    // so the version comes from the installed driver, and from the API only when it cannot be read.
    [Fact]
    public void TheDriverVersionIsTheInstalledOneWithTheApiAsFallback()
    {
        string? installed = "617.42";
        using var nvidia = new NvidiaService(factory.Create, () => installed);
        factory.Configure = backend => backend.DriverVersion = "616.64";

        nvidia.Initialize();
        Assert.Equal("617.42", nvidia.DriverVersion);

        installed = null;
        nvidia.Initialize();
        Assert.Equal("616.64", nvidia.DriverVersion);
    }

    [Fact]
    public void RefreshingShowsADriverInstalledWhileRunning()
    {
        string? installed = "616.64";
        using var nvidia = new NvidiaService(factory.Create, () => installed);
        factory.Configure = backend => backend.DriverVersion = "616.64";
        nvidia.Initialize();

        installed = "617.42";
        nvidia.RefreshDriverVersion();
        Assert.Equal("617.42", nvidia.DriverVersion);

        installed = null;
        factory.Created[0].DriverVersion = "Unknown";
        nvidia.RefreshDriverVersion();
        Assert.Equal("617.42", nvidia.DriverVersion);
    }

    [Fact]
    public void WithoutTheApiTheInstalledVersionIsNotRead()
    {
        var reads = 0;
        using var nvidia = new NvidiaService(factory.Create, () => { reads++; return "617.42"; });

        nvidia.RefreshDriverVersion();

        Assert.Equal(0, reads);
        Assert.Equal("Unknown", nvidia.DriverVersion);
    }
}
