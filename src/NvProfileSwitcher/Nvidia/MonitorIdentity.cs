using Microsoft.Win32;
using System.Runtime.InteropServices;
using NvProfileSwitcher.Core;

namespace NvProfileSwitcher.Nvidia;

internal static class MonitorIdentity
{
    private const uint QdcOnlyActivePaths = 0x2;
    private const int ErrorSuccess = 0;
    private const int GetSourceName = 1;
    private const int GetTargetName = 2;

    /// <summary>
    /// The monitor shown on a GDI display: its hardware id and device instance from the active
    /// display path, and its serial number from the EDID Windows keeps in the registry. Null
    /// when any of them could not be read, which is not a monitor without a serial number.
    /// </summary>
    public static MonitorDescriptor? ReadForGdi(string gdi)
    {
        if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != ErrorSuccess)
            return null;
        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];
        if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != ErrorSuccess)
            return null;

        for (var i = 0; i < pathCount; i++)
        {
            var src = new DisplayConfigSourceDeviceName();
            src.header.type = GetSourceName;
            src.header.size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>();
            src.header.adapterId = paths[i].sourceInfo.adapterId;
            src.header.id = paths[i].sourceInfo.id;
            if (DisplayConfigGetDeviceInfo(ref src) != ErrorSuccess ||
                !string.Equals(src.viewGdiDeviceName, gdi, StringComparison.OrdinalIgnoreCase))
                continue;

            var tgt = new DisplayConfigTargetDeviceName();
            tgt.header.type = GetTargetName;
            tgt.header.size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>();
            tgt.header.adapterId = paths[i].targetInfo.adapterId;
            tgt.header.id = paths[i].targetInfo.id;
            if (DisplayConfigGetDeviceInfo(ref tgt) != ErrorSuccess)
                return null;

            if (MonitorIdentityCore.ParseDevicePath(tgt.monitorDevicePath) is not { } parsed)
                return null;
            var (hardware, instance) = parsed;
            var edid = ReadEdid(hardware, instance);
            if (!MonitorIdentityCore.IsReadable(edid))
                return null;
            return new MonitorDescriptor(hardware, instance, MonitorIdentityCore.Serial(edid!));
        }
        return null;
    }

    private static byte[]? ReadEdid(string hardware, string instance)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{hardware}\{instance}\Device Parameters");
        return key?.GetValue("EDID") as byte[];
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);
    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements, [Out] DisplayConfigModeInfo[] modeInfoArray, IntPtr currentTopologyId);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct PathSourceInfo { public Luid adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct PathTargetInfo
    {
        public Luid adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology;
        public uint rotation; public uint scaling; public Rational refreshRate; public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable; public uint statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayConfigPathInfo { public PathSourceInfo sourceInfo; public PathTargetInfo targetInfo; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] private struct PointL { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] private struct Region { public uint cx; public uint cy; }
    [StructLayout(LayoutKind.Sequential)] private struct VideoSignalInfo
    {
        public ulong pixelRate; public Rational hSyncFreq; public Rational vSyncFreq; public Region activeSize; public Region totalSize;
        public uint videoStandard; public uint scanLineOrdering;
    }
    [StructLayout(LayoutKind.Explicit)] private struct ModeUnion
    {
        [FieldOffset(0)] public SourceMode sourceMode;
        [FieldOffset(0)] public TargetMode targetMode;
    }
    [StructLayout(LayoutKind.Sequential)] private struct SourceMode { public uint width; public uint height; public uint pixelFormat; public PointL position; }
    [StructLayout(LayoutKind.Sequential)] private struct TargetMode { public VideoSignalInfo targetVideoSignalInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayConfigModeInfo { public uint infoType; public uint id; public Luid adapterId; public ModeUnion modeInfo; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DeviceInfoHeader { public uint type; public uint size; public Luid adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayConfigSourceDeviceName
    {
        public DeviceInfoHeader header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayConfigTargetDeviceName
    {
        public DeviceInfoHeader header; public uint flags; public uint outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }
}
