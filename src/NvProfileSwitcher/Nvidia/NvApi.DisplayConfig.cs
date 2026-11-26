using System.Runtime.InteropServices;

namespace NvProfileSwitcher.Nvidia;

// The desktop resolution through the NVIDIA driver's own display configuration:
// NvAPI_DISP_GetDisplayConfig and NvAPI_DISP_SetDisplayConfig (nvapi.h; 64-bit layouts).
// A change replaces only the source resolution of one display's path; every other value of the
// configuration the driver reported is given back as it was: the target's timing, refresh rate,
// scaling and rotation, the position of every display and every other path. It is validated
// first (NV_DISPLAYCONFIG_VALIDATE_ONLY) and applied without NV_DISPLAYCONFIG_SAVE_TO_PERSISTENCE,
// so the saved configuration of Windows and of the driver is never modified.
internal sealed partial class NvApi
{
    private const uint GetDisplayConfigHash = 0x11ABCCF8;
    private const uint SetDisplayConfigHash = 0x5D8CF8DE;
    private const uint ValidateOnly = 0x1;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private unsafe delegate int GetDisplayConfigFn(uint* pathCount, PathInfo* paths);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private unsafe delegate int SetDisplayConfigFn(uint pathCount, PathInfo* paths, uint flags);

    private readonly GetDisplayConfigFn? getDisplayConfig;
    private readonly SetDisplayConfigFn? setDisplayConfig;

    public unsafe DisplayResolution? GetResolution(NvidiaDisplay display)
    {
        using var config = DisplayConfig.Read(getDisplayConfig);
        var path = config is null ? null : config.Find(display.DisplayId);
        if (path is null || path->sourceModeInfo is null)
            return null;
        return new DisplayResolution((int)path->sourceModeInfo->width, (int)path->sourceModeInfo->height);
    }

    public unsafe bool SetResolution(NvidiaDisplay display, DisplayResolution resolution)
    {
        if (setDisplayConfig is null || !resolution.IsValid)
            return false;

        using var config = DisplayConfig.Read(getDisplayConfig);
        if (config is null)
            return false;
        // A path that shows its source on more than one display (clone) is never changed: the
        // other display would change as well.
        var path = config.Find(display.DisplayId);
        if (path is null || path->targetInfoCount != 1 || path->sourceModeInfo is null)
            return false;

        path->sourceModeInfo->width = (uint)resolution.Width;
        path->sourceModeInfo->height = (uint)resolution.Height;
        return setDisplayConfig(config.Count, config.Paths, ValidateOnly) == 0 &&
               setDisplayConfig(config.Count, config.Paths, 0) == 0;
    }

    public IReadOnlyList<DisplayResolution> GetSupportedResolutions(NvidiaDisplay display)
    {
        var current = DisplayMode.Create();
        if (!EnumDisplaySettings(display.GdiName, -1, ref current))
            return [];

        var resolutions = new List<DisplayResolution>();
        for (var index = 0; ; index++)
        {
            var mode = DisplayMode.Create();
            if (!EnumDisplaySettings(display.GdiName, index, ref mode))
                break;
            // The current color depth, progressive modes only.
            if (mode.dmBitsPerPel != current.dmBitsPerPel || (mode.dmDisplayFlags & 0x2) != 0)
                continue;
            var resolution = new DisplayResolution((int)mode.dmPelsWidth, (int)mode.dmPelsHeight);
            if (resolution.IsValid && !resolutions.Contains(resolution))
                resolutions.Add(resolution);
        }

        return resolutions.OrderByDescending(r => (long)r.Width * r.Height).ThenByDescending(r => r.Width).ToArray();
    }

    // ---- nvapi.h structures (x64) ----

    /// <summary>The sizes of the structures, which their versions encode (tested against nvapi.h).</summary>
    internal static unsafe (int Path, int Target, int Source, int Advanced) DisplayConfigSizes =>
        (sizeof(PathInfo), sizeof(PathTargetInfo), sizeof(SourceModeInfo), sizeof(AdvancedTargetInfo));

    /// <summary>NV_DISPLAYCONFIG_PATH_INFO_VER2 and NV_DISPLAYCONFIG_PATH_ADVANCED_TARGET_INFO_VER1.</summary>
    internal static unsafe (uint Path, uint Advanced) DisplayConfigVersions =>
        ((uint)sizeof(PathInfo) | (2u << 16), (uint)sizeof(AdvancedTargetInfo) | (1u << 16));

    // NV_DISPLAYCONFIG_PATH_INFO_V2.
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct PathInfo
    {
        public uint version;
        public uint sourceId;
        public uint targetInfoCount;
        public PathTargetInfo* targetInfo;
        public SourceModeInfo* sourceModeInfo;
        public uint flags;
        public void* osAdapterId;
    }

    // NV_DISPLAYCONFIG_PATH_TARGET_INFO_V2.
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct PathTargetInfo
    {
        public uint displayId;
        public AdvancedTargetInfo* details;
        public uint targetId;
    }

    // NV_DISPLAYCONFIG_SOURCE_MODE_INFO_V1: NV_SCREEN_INFO, NV_FORMAT, NV_POSITION, spanning, flags.
    [StructLayout(LayoutKind.Sequential)]
    private struct SourceModeInfo
    {
        public uint width;
        public uint height;
        public uint colorDepth;
        public int colorFormat;
        public int positionX;
        public int positionY;
        public int spanningOrientation;
        public uint flags;
    }

    // NV_DISPLAYCONFIG_PATH_ADVANCED_TARGET_INFO.
    [StructLayout(LayoutKind.Sequential)]
    private struct AdvancedTargetInfo
    {
        public uint version;
        public int rotation;
        public int scaling;
        public uint refreshRate1K;
        public uint flags;
        public int connector;
        public uint tvFormat;
        public int timingOverride;
        public Timing timing;
    }

    // NV_TIMING.
    [StructLayout(LayoutKind.Sequential)]
    private struct Timing
    {
        public ushort hVisible, hBorder, hFrontPorch, hSyncWidth, hTotal;
        public byte hSyncPol;
        public ushort vVisible, vBorder, vFrontPorch, vSyncWidth, vTotal;
        public byte vSyncPol;
        public ushort interlaced;
        public uint pclk;
        public TimingExt etc;
    }

    // NV_TIMINGEXT.
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct TimingExt
    {
        public uint flag;
        public ushort rr;
        public uint rrx1k;
        public uint aspect;
        public ushort rep;
        public uint status;
        public fixed byte name[40];
    }

    // The display configuration as the driver reports it, in native memory released on dispose:
    // the paths, then for each its source mode and its targets with their details.
    private sealed unsafe class DisplayConfig : IDisposable
    {
        private readonly List<nint> allocations = [];

        public uint Count { get; private set; }

        public PathInfo* Paths { get; private set; }

        // The three calls nvapi.h describes: the path count, each path's target count, then every value.
        public static DisplayConfig? Read(GetDisplayConfigFn? get)
        {
            if (get is null)
                return null;

            var config = new DisplayConfig();
            try
            {
                uint count = 0;
                if (get(&count, null) != 0 || count == 0)
                {
                    config.Dispose();
                    return null;
                }

                var paths = (PathInfo*)config.Allocate(count * (nuint)sizeof(PathInfo));
                for (var i = 0; i < count; i++)
                    paths[i].version = DisplayConfigVersions.Path;
                if (get(&count, paths) != 0)
                {
                    config.Dispose();
                    return null;
                }

                for (var i = 0; i < count; i++)
                {
                    paths[i].sourceModeInfo = (SourceModeInfo*)config.Allocate((nuint)sizeof(SourceModeInfo));
                    var targets = (PathTargetInfo*)config.Allocate(Math.Max(paths[i].targetInfoCount, 1u) * (nuint)sizeof(PathTargetInfo));
                    for (var t = 0; t < paths[i].targetInfoCount; t++)
                    {
                        targets[t].details = (AdvancedTargetInfo*)config.Allocate((nuint)sizeof(AdvancedTargetInfo));
                        targets[t].details->version = DisplayConfigVersions.Advanced;
                    }

                    paths[i].targetInfo = targets;
                }

                if (get(&count, paths) != 0)
                {
                    config.Dispose();
                    return null;
                }

                config.Count = count;
                config.Paths = paths;
                return config;
            }
            catch
            {
                config.Dispose();
                throw;
            }
        }

        // The path whose single or first target is the display.
        public PathInfo* Find(uint displayId)
        {
            for (var i = 0; i < Count; i++)
            {
                var path = &Paths[i];
                for (var t = 0; t < path->targetInfoCount; t++)
                {
                    if (path->targetInfo[t].displayId == displayId)
                        return path;
                }
            }

            return null;
        }

        private void* Allocate(nuint size)
        {
            var memory = NativeMemory.AllocZeroed(size);
            allocations.Add((nint)memory);
            return memory;
        }

        public void Dispose()
        {
            foreach (var memory in allocations)
                NativeMemory.Free((void*)memory);
            allocations.Clear();
            Paths = null;
            Count = 0;
        }
    }
}
