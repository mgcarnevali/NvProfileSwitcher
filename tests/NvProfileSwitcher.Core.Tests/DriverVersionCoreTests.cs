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
}
