using Xunit;

namespace NvProfileSwitcher.Core.Tests;

public sealed class ProfileApplicationCoreTests
{
    [Fact]
    public void GlobalOnAllowsPhysicalApplication()
    {
        Assert.True(ProfileApplicationCore.CanApply(globalPaused: false));
    }

    [Fact]
    public void GlobalOffBlocksNormalPhysicalApplication()
    {
        Assert.False(ProfileApplicationCore.CanApply(globalPaused: true));
    }

    [Fact]
    public void GlobalOffAllowsExplicitForcedApplication()
    {
        Assert.True(ProfileApplicationCore.CanApply(globalPaused: true, forceApply: true));
    }
}
