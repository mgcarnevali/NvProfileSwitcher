using NvProfileSwitcher.Core;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

public sealed class DriverVersionCoreTests
{
    [Theory]
    [InlineData(32, 0, 16, 1742, "617.42")]
    [InlineData(32, 0, 15, 6636, "566.36")]
    [InlineData(31, 0, 15, 5222, "552.22")]
    [InlineData(32, 0, 15, 561, "505.61")]
    public void TheWindowsFileVersionGivesTheNvidiaVersion(int major, int minor, int build, int revision, string expected) =>
        Assert.Equal(expected, DriverVersionCore.FromFileVersion(major, minor, build, revision));

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(32, 0, 16, 10000)]
    [InlineData(32, 0, -1, 1742)]
    [InlineData(32, 0, 10, 0)]
    public void AVersionWithoutThatFormHasNone(int major, int minor, int build, int revision) =>
        Assert.Null(DriverVersionCore.FromFileVersion(major, minor, build, revision));

    // The driver version Windows records for the display adapter, as for a GeForce RTX 3090 with
    // driver 621.01.
    [Theory]
    [InlineData("NVIDIA", "32.0.16.2101", "621.01")]
    [InlineData("NVIDIA", "32.0.16.1742", "617.42")]
    [InlineData("nvidia", " 32.0.15.6636 ", "566.36")]
    public void TheNvidiaAdapterGivesTheNvidiaVersion(string provider, string version, string expected) =>
        Assert.Equal(expected, DriverVersionCore.FromDisplayAdapters([(provider, version)]));

    [Fact]
    public void OnlyNvidiaAdaptersCount() =>
        Assert.Equal("621.01", DriverVersionCore.FromDisplayAdapters(
        [
            ("Intel Corporation", "32.0.101.6913"),
            ("Microsoft", "10.0.26100.1"),
            ("NVIDIA", "32.0.16.2101"),
            (null, "32.0.16.9999"),
        ]));

    [Fact]
    public void TheNewestNvidiaVersionWins() =>
        Assert.Equal("621.01", DriverVersionCore.FromDisplayAdapters(
        [
            ("NVIDIA", "32.0.16.1742"),
            ("NVIDIA", "32.0.16.2101"),
            ("NVIDIA", "32.0.15.6636"),
        ]));

    // A version that cannot be converted is skipped, not taken as the newest.
    [Fact]
    public void AnInvalidNvidiaVersionIsSkipped() =>
        Assert.Equal("617.42", DriverVersionCore.FromDisplayAdapters(
        [
            ("NVIDIA", "32.0.16.99999"),
            ("NVIDIA", "32.0.16.1742"),
        ]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("32.0.16")]
    [InlineData("32.0.16.2101.1")]
    [InlineData("32.0.16.x")]
    [InlineData("32.0.16.-1")]
    [InlineData("32.0.16.10000")]
    [InlineData("0.0.0.0")]
    public void AnAdapterVersionWithoutThatFormHasNone(string? version) =>
        Assert.Null(DriverVersionCore.FromDisplayAdapters([("NVIDIA", version)]));

    [Fact]
    public void WithoutAnNvidiaAdapterThereIsNone()
    {
        Assert.Null(DriverVersionCore.FromDisplayAdapters([]));
        Assert.Null(DriverVersionCore.FromDisplayAdapters([("Intel Corporation", "32.0.101.6913"), ("NVIDIA", null)]));
    }
}
