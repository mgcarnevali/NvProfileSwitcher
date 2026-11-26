using System.Runtime.InteropServices;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Platform;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// The request ChangeDisplaySettingsExW receives: the resolution and refresh rate, and nothing
// carried from the current mode (issue #41: a copy of a scaled 1440×1080 mode made the request for
// the 1920×1080 desktop mode invalid on a GeForce, so it was never restored).
public sealed class DisplayModeRequestTests
{
    [Theory]
    [InlineData(1440, 1080, 144)]
    [InlineData(1920, 1080, 60)]
    public void TheRequestDeclaresTheResolutionAndRefreshRateOnly(int width, int height, int rate)
    {
        var request = Win32DisplayModeBackend.Request(new DisplayModeInfo(width, height, rate));

        Assert.Equal(
            Win32DisplayModeBackend.DmPelsWidth | Win32DisplayModeBackend.DmPelsHeight | Win32DisplayModeBackend.DmDisplayFrequency,
            request.dmFields);
        Assert.Equal((uint)width, request.dmPelsWidth);
        Assert.Equal((uint)height, request.dmPelsHeight);
        Assert.Equal((uint)rate, request.dmDisplayFrequency);
        Assert.Equal(Marshal.SizeOf<Win32DisplayModeBackend.DevMode>(), request.dmSize);
        // Nothing else is set: no position, scaling, orientation, color depth or flags.
        Assert.Equal((0, 0), (request.dmPositionX, request.dmPositionY));
        Assert.Equal(0u, request.dmDisplayFixedOutput);
        Assert.Equal(0u, request.dmDisplayOrientation);
        Assert.Equal(0u, request.dmBitsPerPel);
        Assert.Equal(0u, request.dmDisplayFlags);
    }
}
