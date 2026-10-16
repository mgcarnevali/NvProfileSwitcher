using Xunit;

namespace NvProfileSwitcher.Core.Tests;

public sealed class DisplayCoreFindValuesTests
{
    private static ProfileListItem Profile()
    {
        var profile = new ProfileListItem("Game");
        profile.DisplayProfiles.Add(new ProfileDisplaySettings { MonitorId = "DEL4321-ABC123", DigitalVibrance = 80 });
        profile.DisplayProfiles.Add(new ProfileDisplaySettings { MonitorId = "GSM5B7F-305NTAB1C2D3", DigitalVibrance = 60 });
        return profile;
    }

    [Fact]
    public void FindsTheValuesOfAMonitorIgnoringCase() =>
        Assert.Equal(60, DisplayCore.FindValues(Profile(), "gsm5b7f-305ntab1c2d3")?.DigitalVibrance);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("SAM0F12-H4ZN901234")]
    public void ReturnsNullForBlankOrUnknownMonitors(string? monitorId) =>
        Assert.Null(DisplayCore.FindValues(Profile(), monitorId));
}
