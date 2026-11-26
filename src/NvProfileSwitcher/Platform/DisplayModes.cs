using System.Runtime.InteropServices;
using NvProfileSwitcher.Core;

namespace NvProfileSwitcher.Platform;

/// <summary>Reads and changes the display mode of a monitor, by its GDI device name.</summary>
internal interface IDisplayModeBackend
{
    /// <summary>The modes the monitor supports at its current color depth and orientation, progressive only.</summary>
    IReadOnlyList<DisplayModeInfo> GetModes(string gdiName);

    /// <summary>The monitor's current mode, or null when it cannot be read.</summary>
    DisplayModeInfo? GetCurrent(string gdiName);

    /// <summary>
    /// Validates <paramref name="mode"/> and applies it for this session only: it is never written
    /// to the registry, so Windows returns to its saved mode when the user signs out or restarts.
    /// True when Windows applied it.
    /// </summary>
    bool Apply(string gdiName, DisplayModeInfo mode);
}

/// <summary>
/// <see cref="IDisplayModeBackend"/> over ChangeDisplaySettingsExW and EnumDisplaySettingsExW.
/// Only the resolution and refresh rate change: a request declares those three fields alone, so
/// position, orientation and color depth stay as they are, and the scaling is the driver's own
/// choice for the new mode, as the user configured it.
/// </summary>
internal sealed class Win32DisplayModeBackend : IDisplayModeBackend
{
    private const int EnumCurrentSettings = -1;
    internal const uint DmPelsWidth = 0x00080000;
    internal const uint DmPelsHeight = 0x00100000;
    internal const uint DmDisplayFrequency = 0x00400000;
    private const uint DmInterlaced = 0x00000002;
    private const uint CdsTest = 0x00000002;
    private const int DispChangeSuccessful = 0;

    public IReadOnlyList<DisplayModeInfo> GetModes(string gdiName)
    {
        var current = DevMode.Create();
        if (!EnumDisplaySettingsEx(gdiName, EnumCurrentSettings, ref current, 0))
            return [];

        var modes = new List<DisplayModeInfo>();
        for (var index = 0; ; index++)
        {
            var mode = DevMode.Create();
            if (!EnumDisplaySettingsEx(gdiName, index, ref mode, 0))
                break;
            if (mode.dmBitsPerPel != current.dmBitsPerPel || (mode.dmDisplayFlags & DmInterlaced) != 0 || mode.dmDisplayFrequency <= 1)
                continue;

            var info = new DisplayModeInfo((int)mode.dmPelsWidth, (int)mode.dmPelsHeight, (int)mode.dmDisplayFrequency);
            if (!modes.Contains(info))
                modes.Add(info);
        }

        return modes;
    }

    public DisplayModeInfo? GetCurrent(string gdiName)
    {
        var mode = DevMode.Create();
        return EnumDisplaySettingsEx(gdiName, EnumCurrentSettings, ref mode, 0)
            ? new DisplayModeInfo((int)mode.dmPelsWidth, (int)mode.dmPelsHeight, (int)mode.dmDisplayFrequency)
            : null;
    }

    public bool Apply(string gdiName, DisplayModeInfo mode)
    {
        var devMode = Request(mode);

        // Validated first; then applied without CDS_UPDATEREGISTRY, for this session only.
        return ChangeDisplaySettingsEx(gdiName, ref devMode, IntPtr.Zero, CdsTest, IntPtr.Zero) == DispChangeSuccessful &&
               ChangeDisplaySettingsEx(gdiName, ref devMode, IntPtr.Zero, 0, IntPtr.Zero) == DispChangeSuccessful;
    }

    /// <summary>
    /// The request for <paramref name="mode"/>: its resolution and refresh rate, and no other field.
    /// It is never built from the current mode: a copy carries that mode's fields, and a driver may
    /// reject them for the new one (issue #41: the GeForce scaling of a 1440×1080 mode made the
    /// request for 1920×1080 invalid, so the desktop mode was never restored).
    /// </summary>
    internal static DevMode Request(DisplayModeInfo mode)
    {
        var devMode = DevMode.Create();
        devMode.dmPelsWidth = (uint)mode.Width;
        devMode.dmPelsHeight = (uint)mode.Height;
        devMode.dmDisplayFrequency = (uint)mode.RefreshRate;
        devMode.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;
        return devMode;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DevMode lpDevMode, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DevMode lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevMode
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

        public static DevMode Create() => new()
        {
            dmDeviceName = string.Empty,
            dmFormName = string.Empty,
            dmSize = (ushort)Marshal.SizeOf<DevMode>()
        };
    }
}
