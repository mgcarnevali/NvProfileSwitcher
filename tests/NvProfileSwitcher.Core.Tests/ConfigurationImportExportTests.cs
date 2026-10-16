using Xunit;
using static NvProfileSwitcher.Core.Tests.ConfigFixtures;

namespace NvProfileSwitcher.Core.Tests;

// The file side of Backup: the imported JSON replaces config.json as it was read and is
// applied to the state; the export copies config.json.
public sealed class ConfigurationImportExportTests
{
    // Not canonical (no escapes, different indentation), so a rewrite would show.
    private static readonly string Imported = Variant(node =>
    {
        node["AlwaysOnTop"] = true;
        node["RememberGlobalSwitching"] = false;
    });

    [Fact]
    public async Task ImportWritesTheFileAsReadAndAppliesIt()
    {
        using var session = new ConfigSession();
        session.Service.Load();
        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(Imported, out var config));

        await session.Service.ImportAsync(Imported, config);

        Assert.Equal(Imported, File.ReadAllText(session.ConfigPath));
        Assert.False(File.Exists(session.ConfigPath + ".import.tmp"));
        Assert.Equal(["Windows", "Café & Games", "Tarkov"], session.Profiles.Profiles.Select(p => p.Name));
        Assert.True(session.Settings.AlwaysOnTop);
        Assert.True(session.Settings.CheckForUpdates);
        Assert.Equal(1648, session.Settings.ShowHideHotkey);
        // Global switching is only restored when remembered; the override is.
        Assert.False(session.Switching.GlobalPaused);
        Assert.Equal(OverrideMode.Profile, session.Switching.OverrideMode);
        Assert.Equal(0, session.Switching.OverrideProfileIndex);
    }

    [Fact]
    public async Task ImportLeavesTheActiveProfileName()
    {
        using var session = new ConfigSession();
        session.Service.Load();
        Assert.Equal("Windows", session.Switching.ActiveProfileName);
        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(Canonical, out var config));

        await session.Service.ImportAsync(Canonical, config);

        Assert.Equal("Windows", session.Switching.ActiveProfileName);
    }

    // As on load, development builds never check for updates; the file keeps the imported value.
    [Fact]
    public async Task DevelopmentBuildsImportWithoutUpdateChecks()
    {
        using var session = new ConfigSession(isDevelopmentBuild: true);
        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(Canonical, out var config));

        await session.Service.ImportAsync(Canonical, config);

        Assert.False(session.Settings.CheckForUpdates);
        Assert.Equal(Canonical, File.ReadAllText(session.ConfigPath));
    }

    [Fact]
    public async Task ImportCreatesTheConfigurationDirectory()
    {
        using var session = new ConfigSession(createDirectory: false);
        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(Canonical, out var config));

        await session.Service.ImportAsync(Canonical, config);

        Assert.Equal(Canonical, File.ReadAllText(session.ConfigPath));
    }

    [Fact]
    public async Task ExportCopiesTheConfigurationOverTheDestination()
    {
        using var session = new ConfigSession(Canonical);
        using var destination = new MemoryStream();
        destination.Write(new byte[10_000]);

        await session.Service.ExportAsync(destination);

        Assert.Equal(File.ReadAllBytes(session.ConfigPath), destination.ToArray());
    }
}
