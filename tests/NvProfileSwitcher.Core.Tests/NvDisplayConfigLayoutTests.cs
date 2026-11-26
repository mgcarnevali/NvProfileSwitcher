using NvProfileSwitcher.Nvidia;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// The layouts of the nvapi.h structures NvAPI_DISP_GetDisplayConfig and SetDisplayConfig take,
// on 64-bit Windows: their versions encode their size, so a wrong layout is rejected by the
// driver instead of being misread. The expected sizes are the 64-bit sizeof of each structure.
public sealed class NvDisplayConfigLayoutTests
{
    [Fact]
    public void TheStructuresHaveTheSizesOfNvapiH()
    {
        // NV_DISPLAYCONFIG_PATH_INFO_V2, NV_DISPLAYCONFIG_PATH_TARGET_INFO_V2,
        // NV_DISPLAYCONFIG_SOURCE_MODE_INFO_V1, NV_DISPLAYCONFIG_PATH_ADVANCED_TARGET_INFO.
        Assert.Equal((48, 24, 32, 128), NvApi.DisplayConfigSizes);
    }

    [Fact]
    public void TheVersionsAreThoseOfNvapiH()
    {
        // MAKE_NVAPI_VERSION(type, version) = sizeof(type) | version << 16.
        Assert.Equal((0x20030u, 0x10080u), NvApi.DisplayConfigVersions);
    }
}
