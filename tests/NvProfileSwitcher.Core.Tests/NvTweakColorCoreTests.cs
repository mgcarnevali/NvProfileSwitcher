using Microsoft.Win32;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// The NVIDIA Control Panel settings in NVTweak: nine DWORDs, brightness, contrast and gamma × 100
// for red, green and blue. They are used only when all nine are valid and each attribute has
// the same value for the three channels.
public sealed class NvTweakColorCoreTests
{
    internal static NvTweakValue?[] Values(int brightness, int contrast, int gamma) =>
    [
        Dword(brightness), Dword(brightness), Dword(brightness),
        Dword(contrast), Dword(contrast), Dword(contrast),
        Dword(gamma), Dword(gamma), Dword(gamma)
    ];

    internal static NvTweakValue Dword(int value) => new(RegistryValueKind.DWord, value);

    [Fact]
    public void TheNineValuesGiveBrightnessContrastAndGamma()
    {
        Assert.Equal(new ControlPanelColors(105, 110, 1.3), NvTweakColorCore.Parse(Values(105, 110, 130)));
        Assert.Equal(new ControlPanelColors(110, 105, 1.2), NvTweakColorCore.Parse(Values(110, 105, 120)));
        Assert.Equal(new ControlPanelColors(95, 95, 1.1), NvTweakColorCore.Parse(Values(95, 95, 110)));
        Assert.Equal(new ControlPanelColors(100, 100, 1.4), NvTweakColorCore.Parse(Values(100, 100, 140)));
    }

    [Theory]
    [InlineData(80, 80, 30)]
    [InlineData(120, 120, 280)]
    public void TheLimitsAreAccepted(int brightness, int contrast, int gamma) =>
        Assert.Equal(new ControlPanelColors(brightness, contrast, gamma / 100.0), NvTweakColorCore.Parse(Values(brightness, contrast, gamma)));

    [Theory]
    [InlineData(79, 100, 100)]
    [InlineData(121, 100, 100)]
    [InlineData(100, 79, 100)]
    [InlineData(100, 121, 100)]
    [InlineData(100, 100, 29)]
    [InlineData(100, 100, 281)]
    [InlineData(-1, 100, 100)]
    [InlineData(100, 100, 0)]
    public void AValueOutOfRangeIsNotUsed(int brightness, int contrast, int gamma) =>
        Assert.Null(NvTweakColorCore.Parse(Values(brightness, contrast, gamma)));

    // Each of the nine values on its own: a different channel, a missing value or another
    // type discards all of them; nothing is averaged or taken in part.
    public static TheoryData<int> EachValue => new() { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    [Theory]
    [MemberData(nameof(EachValue))]
    public void ChannelsThatDifferAreNotUsed(int index)
    {
        var values = Values(100, 100, 100);
        values[index] = Dword(101);
        Assert.Null(NvTweakColorCore.Parse(values));
    }

    [Theory]
    [MemberData(nameof(EachValue))]
    public void AMissingValueDiscardsAll(int index)
    {
        var values = Values(105, 110, 130);
        values[index] = null;
        Assert.Null(NvTweakColorCore.Parse(values));
    }

    [Theory]
    [MemberData(nameof(EachValue))]
    public void AValueOfAnotherTypeDiscardsAll(int index)
    {
        foreach (var wrong in new NvTweakValue[]
                 {
                     new(RegistryValueKind.String, "105"),
                     new(RegistryValueKind.QWord, 105L),
                     new(RegistryValueKind.Binary, new byte[] { 105, 0, 0, 0 }),
                     new(RegistryValueKind.DWord, null),
                     new(RegistryValueKind.DWord, "105")
                 })
        {
            var values = Values(105, 105, 105);
            values[index] = wrong;
            Assert.Null(NvTweakColorCore.Parse(values));
        }
    }

    [Fact]
    public void AMissingKeyOrAnotherNumberOfValuesIsNotUsed()
    {
        Assert.Null(NvTweakColorCore.Parse(null));
        Assert.Null(NvTweakColorCore.Parse([]));
        Assert.Null(NvTweakColorCore.Parse(Values(100, 100, 100)[..8]));
        Assert.Null(NvTweakColorCore.Parse([.. Values(100, 100, 100), Dword(100)]));
    }

    [Fact]
    public void TheKeyAndValueNamesFollowTheDeviceId()
    {
        Assert.Equal(@"Software\NVIDIA Corporation\Global\NVTweak\Devices\3107697358-0\Color", NvTweakColorCore.KeyPath(3107697358));
        Assert.Equal(
            ["3538946", "3538947", "3538948", "3538949", "3538950", "3538951", "3538952", "3538953", "3538954"],
            Enumerable.Range(0, 9).Select(NvTweakColorCore.ValueName));
    }

    // NvAPI_SYS_GetLUIDFromDisplayID reports the device id in the second word of the LUID, with
    // its top four bits set.
    [Fact]
    public void TheDeviceIdIsTheSecondWordOfTheLuidWithoutItsTopBits()
    {
        Assert.Equal(3107697358u, NvTweakColorCore.DeviceIdFromLuid(3107697358u ^ 0xF0000000u));
        Assert.Equal(0x0123_4567u, NvTweakColorCore.DeviceIdFromLuid(0xF123_4567u));
    }
}
