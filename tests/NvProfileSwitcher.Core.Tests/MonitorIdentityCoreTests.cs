using NvProfileSwitcher.Nvidia;
using Xunit;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.Core.Tests;

public sealed class MonitorIdentityCoreTests
{
    private static readonly IReadOnlySet<string> NoSharedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // A 128-byte EDID with the numeric serial number and, optionally, a serial number descriptor.
    private static byte[] Edid(uint numeric = 0, string? descriptor = null)
    {
        var edid = new byte[128];
        BitConverter.GetBytes(numeric).CopyTo(edid, 12);
        if (descriptor is not null)
        {
            edid[57] = 0xFF;
            var text = System.Text.Encoding.ASCII.GetBytes(descriptor + "\n");
            Array.Copy(text, 0, edid, 59, Math.Min(text.Length, 13));
        }

        return edid;
    }

    private static NvidiaDisplay Connected(int number, string hardware, string? serial) =>
        Display("", $"Monitor {number}", number) with { Monitor = new MonitorDescriptor(hardware, Instance(number), serial) };

    [Fact]
    public void TheDevicePathGivesTheHardwareIdAndTheInstance()
    {
        Assert.Equal(("DEL4321", "5&1a2b3c&0&UID4352"),
            MonitorIdentityCore.ParseDevicePath(@"\\?\DISPLAY#DEL4321#5&1a2b3c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"));
        Assert.Null(MonitorIdentityCore.ParseDevicePath(@"\\?\DISPLAY#DEL4321"));
        Assert.Null(MonitorIdentityCore.ParseDevicePath(@"\\?\DISPLAY##5&1a2b3c&0&UID4352#{guid}"));
        Assert.Null(MonitorIdentityCore.ParseDevicePath(null));
    }

    [Fact]
    public void AnEdidThatCouldNotBeReadIsNotAMonitorWithoutSerial()
    {
        Assert.False(MonitorIdentityCore.IsReadable(null));
        Assert.False(MonitorIdentityCore.IsReadable(new byte[127]));
        Assert.True(MonitorIdentityCore.IsReadable(Edid()));
    }

    [Fact]
    public void TheSerialComesFromTheDescriptorOrElseTheValidNumericSerial()
    {
        Assert.Equal("H4ZN901234", MonitorIdentityCore.Serial(Edid(numeric: 7, descriptor: "H4ZN901234")));
        Assert.Equal("ABC123", MonitorIdentityCore.Serial(Edid(descriptor: "ABC123   ")));
        Assert.Equal("16843010", MonitorIdentityCore.Serial(Edid(numeric: 16843010)));
        Assert.Null(MonitorIdentityCore.Serial(Edid(numeric: 0)));
        Assert.Null(MonitorIdentityCore.Serial(Edid(numeric: 0xFFFFFFFF)));
        Assert.Null(MonitorIdentityCore.Serial(Edid(numeric: 0, descriptor: "   ")));
    }

    [Fact]
    public void AUniqueSerialGivesThePhysicalIdentity()
    {
        var result = MonitorIdentityCore.Identify([Connected(1, "DEL4321", "ABC123")], NoSharedIds);

        var display = Assert.Single(result.Displays);
        Assert.Equal("DEL4321-ABC123", display.MonitorId);
        Assert.Null(display.PhysicalMonitorId);
        Assert.Empty(result.NewlySharedIds);
    }

    // Identical monitors without a serial number are told apart by their connections.
    [Fact]
    public void MonitorsWithoutSerialGetTheirConnectionIdentity()
    {
        var result = MonitorIdentityCore.Identify([Connected(1, "GSM5B7F", null), Connected(2, "GSM5B7F", null)], NoSharedIds);

        Assert.Equal([$"GSM5B7F@{Instance(1)}", $"GSM5B7F@{Instance(2)}"], result.Displays.Select(d => d.MonitorId));
        Assert.All(result.Displays, d => Assert.Null(d.PhysicalMonitorId));
    }

    [Fact]
    public void ASerialTwoMonitorsShareIsReportedAndTheyGetTheirConnectionIdentity()
    {
        var result = MonitorIdentityCore.Identify(
            [Connected(1, "SAM0F12", "1"), Connected(2, "SAM0F12", "1"), Connected(3, "DEL4321", "ABC123")], NoSharedIds);

        Assert.Equal(["SAM0F12-1"], result.NewlySharedIds);
        Assert.Equal([$"SAM0F12@{Instance(1)}", $"SAM0F12@{Instance(2)}", "DEL4321-ABC123"], result.Displays.Select(d => d.MonitorId));
        Assert.Equal(["SAM0F12-1", "SAM0F12-1", null], result.Displays.Select(d => d.PhysicalMonitorId));
    }

    // Once found shared, the identity is not used again even with one of them connected, so
    // the monitors do not move between identities with the topology.
    [Fact]
    public void ASerialFoundSharedBeforeKeepsTheConnectionIdentity()
    {
        var shared = new HashSet<string>(["sam0f12-1"], StringComparer.OrdinalIgnoreCase);

        var result = MonitorIdentityCore.Identify([Connected(1, "SAM0F12", "1")], shared);

        Assert.Equal($"SAM0F12@{Instance(1)}", Assert.Single(result.Displays).MonitorId);
        Assert.Empty(result.NewlySharedIds);
    }

    [Fact]
    public void DisplaysWithoutIdentityAreLeftOutAndCounted()
    {
        var result = MonitorIdentityCore.Identify([Display("", "Projector", 1), Connected(2, "DEL4321", "ABC123")], NoSharedIds);

        Assert.Equal(["DEL4321-ABC123"], result.Displays.Select(d => d.MonitorId));
        Assert.Equal(1, result.UnidentifiedCount);
    }

    [Fact]
    public void ConnectionIdentitiesAreRecognizedWithTheirModel()
    {
        Assert.True(MonitorIdentityCore.IsConnectionId($"GSM5B7F@{Instance(1)}"));
        Assert.False(MonitorIdentityCore.IsConnectionId("GSM5B7F-305NTAB1C2D3"));
        Assert.Equal("GSM5B7F", MonitorIdentityCore.ConnectionHardware($"GSM5B7F@{Instance(1)}"));
    }
}
