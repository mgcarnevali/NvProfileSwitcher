using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// The Windows startup entry of the original ApplyStartupRegistration.
public sealed class StartupRegistrationServiceTests
{
    private const string Path = @"C:\Apps\NvProfileSwitcher\NvProfileSwitcher.exe";

    private readonly AppSettings settings = new();
    private readonly FakeStartupRegistry registry = new();

    [Fact]
    public void DisabledStartupDeletesTheEntry()
    {
        settings.StartMinimized = true;

        new StartupRegistrationService(settings, registry, Path).Apply();

        Assert.Equal(["delete NvProfileSwitcher"], registry.Log);
    }

    [Fact]
    public void EnabledStartupWritesTheQuotedExecutablePath()
    {
        settings.StartWithWindows = true;

        new StartupRegistrationService(settings, registry, Path).Apply();

        Assert.Equal([$"set NvProfileSwitcher=\"{Path}\""], registry.Log);
    }

    [Fact]
    public void StartMinimizedAddsTheMinimizedArgument()
    {
        settings.StartWithWindows = true;
        settings.StartMinimized = true;

        new StartupRegistrationService(settings, registry, Path).Apply();

        Assert.Equal([$"set NvProfileSwitcher=\"{Path}\" --minimized"], registry.Log);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void MissingExecutablePathWritesNothing(string? path)
    {
        settings.StartWithWindows = true;

        new StartupRegistrationService(settings, registry, path).Apply();

        Assert.Empty(registry.Log);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RegistryFailuresAreIgnored(bool startWithWindows)
    {
        settings.StartWithWindows = startWithWindows;
        registry.Failure = new UnauthorizedAccessException("denied");

        new StartupRegistrationService(settings, registry, Path).Apply();

        Assert.Empty(registry.Log);
    }
}
