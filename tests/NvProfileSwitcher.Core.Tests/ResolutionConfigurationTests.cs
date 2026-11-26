using System.Text.Json.Nodes;
using Xunit;
using static NvProfileSwitcher.Core.Tests.ConfigFixtures;

namespace NvProfileSwitcher.Core.Tests;

// The optional resolution of an application profile's display values in config.json.
public sealed class ResolutionConfigurationTests
{
    private static string WithResolution(JsonNode? resolution) => Variant(root =>
        root["Application Profiles"]![0]!["Display Profiles"]![0]!["Resolution"] = resolution);

    [Fact]
    public void AConfigurationWithoutResolutionsIsWrittenAsBefore()
    {
        using var session = new ConfigSession(Canonical);
        session.Service.Load();

        Assert.True(session.Service.Save());
        Assert.DoesNotContain("Resolution", File.ReadAllText(session.ConfigPath));
        Assert.Empty(Differences(Canonical, File.ReadAllText(session.ConfigPath)));
    }

    [Fact]
    public void AResolutionIsLoadedAndSavedWithItsDisplay()
    {
        using var session = new ConfigSession(WithResolution(new JsonObject { ["Width"] = 1440, ["Height"] = 1080 }));
        session.Service.Load();

        var game = session.Profiles.ApplicationProfiles[0];
        Assert.Equal(new DisplayResolution(1440, 1080), game.DisplayProfiles[0].Resolution);
        Assert.Null(game.DisplayProfiles[1].Resolution);

        Assert.True(session.Service.Save());
        var saved = Node(File.ReadAllText(session.ConfigPath))["Application Profiles"]![0]!["Display Profiles"]!;
        Assert.Equal(1440, (int)saved[0]!["Resolution"]!["Width"]!);
        Assert.Null(saved[1]!["Resolution"]);
    }

    [Theory]
    [InlineData("""{"Width":0,"Height":1080}""")]
    [InlineData("""{"Width":1440}""")]
    [InlineData("""{"Width":"1440","Height":1080}""")]
    [InlineData("1440")]
    public void AnInvalidResolutionIsRejected(string resolution)
    {
        Assert.False(ConfigurationCore.TryValidateImportedConfiguration(WithResolution(JsonNode.Parse(resolution)), out _));
    }

    [Fact]
    public void ANullResolutionIsAccepted() =>
        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(WithResolution(null), out _));

    [Fact]
    public void AProfileWithAnotherResolutionIsAnotherProfile()
    {
        var a = new ProfileListItem("Game");
        a.DisplayProfiles.Add(new ProfileDisplaySettings { MonitorId = "DEL4321-ABC123" });
        var b = ProfileEditingCore.CloneProfile(a);
        Assert.True(ProfileEditingCore.ProfilesEqual(a, b));

        b.DisplayProfiles[0].Resolution = new DisplayResolution(1440, 1080);
        Assert.False(ProfileEditingCore.ProfilesEqual(a, b));
        Assert.Equal(new DisplayResolution(1440, 1080), ProfileEditingCore.CloneProfile(b).DisplayProfiles[0].Resolution);
        // The colors are the same: no color change to preview.
        Assert.True(ProfileEditingCore.DisplayColorsEqual(a, b));
    }
}
