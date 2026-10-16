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
        Assert.Empty(nvidia.Displays);
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

    [Fact]
    public void DisplaysAreEnumeratedOnlyWhenInitialized()
    {
        using var nvidia = CreateService();
        var display = FakeDisplayBackend.Display("DEL4321-ABC123", "DELL U2720Q");
        factory.Configure = backend => backend.Displays.Add(display);

        Assert.Throws<InvalidOperationException>(() => nvidia.RefreshDisplays());
        Assert.Null(nvidia.GetVibrance(display));
        Assert.Null(nvidia.GetHue(display));

        nvidia.Initialize();
        nvidia.RefreshDisplays();
        Assert.Equal([display], nvidia.Displays);
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
