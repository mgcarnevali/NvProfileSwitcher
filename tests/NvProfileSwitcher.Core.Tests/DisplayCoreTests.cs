using Xunit;
namespace NvProfileSwitcher.Core.Tests;

public sealed class DisplayCoreTests
{
    [Fact]
    public void MonitorIdsMatchCaseInsensitivelyButNotWhenMissing()
    {
        Assert.True(DisplayCore.SameMonitorId("DISPLAY#ABC", "display#abc"));
        Assert.False(DisplayCore.SameMonitorId("DISPLAY#ABC", "DISPLAY#XYZ"));
        Assert.False(DisplayCore.SameMonitorId("", "DISPLAY#ABC"));
        Assert.False(DisplayCore.SameMonitorId(null, "DISPLAY#ABC"));
    }

    [Fact]
    public void NewDisplayProfileClonesAllWindowsValues()
    {
        var windows = new ProfileDisplaySettings
        {
            Brightness = 91, Contrast = 103, Gamma = 1.35,
            DigitalVibrance = 72, Hue = 4
        };
        var clone = DisplayCore.CloneValues(windows, "TV", "MONITOR-2");
        Assert.Equal("TV", clone.DisplayName);
        Assert.Equal("MONITOR-2", clone.MonitorId);
        Assert.Equal(windows.Brightness, clone.Brightness);
        Assert.Equal(windows.Contrast, clone.Contrast);
        Assert.Equal(windows.Gamma, clone.Gamma);
        Assert.Equal(windows.DigitalVibrance, clone.DigitalVibrance);
        Assert.Equal(windows.Hue, clone.Hue);
    }
}
