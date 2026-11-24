using System.Runtime.InteropServices;

namespace NvProfileSwitcher.Platform;

/// <summary>The monitors connected to the desktop, whatever adapter drives them.</summary>
internal interface IDisplayEnumerator
{
    /// <summary>
    /// The displays attached to the desktop, primary first, each with what Windows reports about
    /// its monitor (null when it could not be read yet) and its current mode. Their monitor ids
    /// and NVIDIA handles are not set here.
    /// </summary>
    IReadOnlyList<ConnectedDisplay> EnumerateDisplays();
}

/// <summary>
/// <see cref="IDisplayEnumerator"/> over EnumDisplayDevicesW, the display configuration and the
/// EDID Windows keeps (<see cref="MonitorIdentity"/>), and EnumDisplaySettingsW. A display on an
/// indirect or virtual output whose monitor cannot be identified is left out: it never will be,
/// so it must not count as a monitor waiting for its identity.
/// </summary>
internal sealed class Win32DisplayEnumerator : IDisplayEnumerator
{
    private const uint AttachedToDesktop = 0x1;
    private const uint PrimaryDevice = 0x4;
    private const uint MirroringDriver = 0x8;
    private const int EnumCurrentSettings = -1;

    public IReadOnlyList<ConnectedDisplay> EnumerateDisplays()
    {
        var result = new List<ConnectedDisplay>();
        for (uint i = 0; ; i++)
        {
            var dd = DisplayDevice.Create();
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & AttachedToDesktop) == 0 || (dd.StateFlags & MirroringDriver) != 0) continue;

            var gdi = dd.DeviceName;
            var monitor = MonitorIdentity.ReadForGdi(gdi, out var virtualOutput);
            if (monitor is null && virtualOutput) continue;

            var mon = DisplayDevice.Create();
            var friendly = EnumDisplayDevices(gdi, 0, ref mon, 0) && !string.IsNullOrWhiteSpace(mon.DeviceString)
                ? mon.DeviceString : (!string.IsNullOrWhiteSpace(dd.DeviceString) ? dd.DeviceString : "Display");
            var primary = (dd.StateFlags & PrimaryDevice) != 0;
            var mode = DisplayMode.Create();
            var hasMode = EnumDisplaySettings(gdi, EnumCurrentSettings, ref mode);
            var width = hasMode ? (int)mode.dmPelsWidth : 0;
            var height = hasMode ? (int)mode.dmPelsHeight : 0;
            var refreshRate = hasMode && mode.dmDisplayFrequency > 1 ? (int)mode.dmDisplayFrequency : 0;
            result.Add(new ConnectedDisplay(gdi, friendly, primary, string.Empty, width, height, refreshRate) { Monitor = monitor });
        }
        return result.OrderByDescending(x => x.Primary).ToArray();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DisplayMode lpDevMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayMode
    {
        private const int CchDeviceName = 32;
        private const int CchFormName = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;

        public static DisplayMode Create() => new()
        {
            dmDeviceName = string.Empty,
            dmFormName = string.Empty,
            dmSize = (ushort)Marshal.SizeOf<DisplayMode>()
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        public static DisplayDevice Create() => new() { cb = Marshal.SizeOf<DisplayDevice>(), DeviceName = "", DeviceString = "", DeviceID = "", DeviceKey = "" };
    }
}
