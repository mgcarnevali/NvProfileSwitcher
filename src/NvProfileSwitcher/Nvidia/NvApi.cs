using System.Runtime.InteropServices;
using NvProfileSwitcher.Core;

namespace NvProfileSwitcher.Nvidia;

internal sealed class NvApi : IDisplayColorBackend
{
    private const uint InitializeHash = 0x0150E828;
    private const uint UnloadHash = 0xD22BDD7E;
    private const uint GetDvcHash = 0x0E45002D;
    private const uint SetDvcHash = 0x4A82C2B1;
    private const uint GetHueHash = 0x95B64341;
    private const uint SetHueHash = 0xF5A0F22C;
    private const uint SetTargetGammaHash = 0x7082A053;
    private const uint GetAssociatedDisplayHandleHash = 0x35C29134;
    private const uint GetDisplayIdByNameHash = 0xAE457190;
    private const uint GetDriverVersionHash = 0x2926AAAD;
    private const uint GetLuidFromDisplayIdHash = 0xD4A859F2;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint QueryInterface(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SimpleFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetAssociatedDisplayHandleFn([MarshalAs(UnmanagedType.LPStr)] string name, out nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDisplayIdByNameFn([MarshalAs(UnmanagedType.LPStr)] string name, out uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private unsafe delegate int GetDvcFn(nint display, uint outputId, DvcInfoEx* info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private unsafe delegate int SetDvcFn(nint display, uint outputId, DvcInfoEx* info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private unsafe delegate int GetHueFn(nint display, uint outputId, HueInfo* info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetHueFn(nint display, uint outputId, uint angle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private unsafe delegate int SetTargetGammaFn(uint displayId, GammaCorrectionEx* data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private unsafe delegate int GetDriverVersionFn(uint* version, byte* branch);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private unsafe delegate int GetLuidFromDisplayIdFn(uint displayId, uint flags, uint* luid);

    [StructLayout(LayoutKind.Sequential)] private struct DvcInfoEx { public uint version; public int currentLevel, minLevel, maxLevel, defaultLevel; }
    [StructLayout(LayoutKind.Sequential)] private struct HueInfo { public uint version, currentAngle, defaultAngle; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private unsafe struct GammaCorrectionEx { public uint version; public fixed float gammaRamp[1024 * 3]; public uint unknown; }

    private readonly nint module;
    private readonly QueryInterface query;
    private readonly SimpleFn unload;
    private readonly GetAssociatedDisplayHandleFn getHandle;
    private readonly GetDisplayIdByNameFn getId;
    private readonly GetDvcFn getDvc;
    private readonly SetDvcFn setDvc;
    private readonly GetHueFn getHue;
    private readonly SetHueFn setHue;
    private readonly SetTargetGammaFn setGamma;
    private readonly GetDriverVersionFn? getDriverVersion;
    private readonly GetLuidFromDisplayIdFn? getLuidFromDisplayId;
    private bool disposed;

    public NvApi() : this("nvapi64.dll") { }

    // The library is a parameter only so tests can load a library without the NVAPI exports.
    internal NvApi(string library)
    {
        module = NativeLibrary.Load(library);
        try
        {
            query = Marshal.GetDelegateForFunctionPointer<QueryInterface>(NativeLibrary.GetExport(module, "nvapi_QueryInterface"));
            var init = Resolve<SimpleFn>(InitializeHash);
            unload = Resolve<SimpleFn>(UnloadHash);
            getHandle = Resolve<GetAssociatedDisplayHandleFn>(GetAssociatedDisplayHandleHash);
            getId = Resolve<GetDisplayIdByNameFn>(GetDisplayIdByNameHash);
            getDvc = Resolve<GetDvcFn>(GetDvcHash);
            setDvc = Resolve<SetDvcFn>(SetDvcHash);
            getHue = Resolve<GetHueFn>(GetHueHash);
            setHue = Resolve<SetHueFn>(SetHueHash);
            setGamma = Resolve<SetTargetGammaFn>(SetTargetGammaHash);
            getDriverVersion = TryResolve<GetDriverVersionFn>(GetDriverVersionHash);
            getLuidFromDisplayId = TryResolve<GetLuidFromDisplayIdFn>(GetLuidFromDisplayIdHash);
            var status = init();
            if (status != 0) throw new NvApiInitializationException(status);
        }
        catch
        {
            // NVAPI was not initialized, so NvAPI_Unload must not be called; only release
            // the module reference taken above so a failed attempt leaves nothing loaded.
            NativeLibrary.Free(module);
            throw;
        }
    }

    private T Resolve<T>(uint hash) where T : Delegate
    {
        var p = query(hash);
        if (p == 0) throw new InvalidOperationException($"NvAPI function 0x{hash:X8} not found");
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    private T? TryResolve<T>(uint hash) where T : Delegate
    {
        var p = query(hash);
        return p == 0 ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    public string DriverVersion
    {
        get
        {
            if (getDriverVersion is null) return "Unknown";
            unsafe
            {
                uint version = 0;
                byte* branch = stackalloc byte[64];
                if (getDriverVersion(&version, branch) != 0 || version == 0) return "Unknown";
                return $"{version / 100}.{version % 100:D2}";
            }
        }
    }

    /// <summary>
    /// The NVIDIA handle and id of the display shown on <paramref name="gdiName"/>, or null when
    /// the NVIDIA driver does not control it (a monitor on another adapter). The API takes the
    /// name with or without the "\\.\" prefix depending on the driver, so both are tried.
    /// </summary>
    public NvidiaHandle? ResolveDisplay(string gdiName)
    {
        var nvName = gdiName.StartsWith(@"\\.\", StringComparison.Ordinal) ? @"\\" + gdiName[4..] : gdiName;
        if (getHandle(nvName, out var handle) != 0 && getHandle(gdiName, out handle) != 0) return null;
        if (getId(nvName, out var id) != 0 && getId(gdiName, out id) != 0) return null;
        return new NvidiaHandle(handle, id);
    }

    public unsafe int? GetVibrance(ConnectedDisplay display)
    {
        if (display.Nvidia is not { } nvidia) return null;
        var d = default(DvcInfoEx);
        d.version = (uint)(sizeof(DvcInfoEx) | (1 << 16));
        if (getDvc(nvidia.Handle, 0, &d) != 0) return null;
        if (d.currentLevel >= d.defaultLevel)
        {
            var span = d.maxLevel - d.defaultLevel;
            return span == 0 ? 50 : Math.Clamp((int)Math.Round(50.0 + 50.0 * (d.currentLevel - d.defaultLevel) / span), 0, 100);
        }
        else
        {
            var span = d.defaultLevel - d.minLevel;
            return span == 0 ? 50 : Math.Clamp((int)Math.Round(50.0 - 50.0 * (d.defaultLevel - d.currentLevel) / span), 0, 100);
        }
    }

    public unsafe bool SetVibrance(ConnectedDisplay display, int percent)
    {
        if (display.Nvidia is not { } nvidia) return false;
        var d = default(DvcInfoEx);
        d.version = (uint)(sizeof(DvcInfoEx) | (1 << 16));
        if (getDvc(nvidia.Handle, 0, &d) != 0) return false;
        percent = Math.Clamp(percent, 0, 100);
        d.currentLevel = percent >= 50
            ? (int)Math.Round(d.defaultLevel + ((percent - 50) / 50.0) * (d.maxLevel - d.defaultLevel))
            : (int)Math.Round(d.defaultLevel - ((50 - percent) / 50.0) * (d.defaultLevel - d.minLevel));
        d.currentLevel = Math.Clamp(d.currentLevel, d.minLevel, d.maxLevel);
        return setDvc(nvidia.Handle, 0, &d) == 0;
    }

    public unsafe uint? GetNvTweakDeviceId(ConnectedDisplay display)
    {
        if (getLuidFromDisplayId is null || display.Nvidia is not { } nvidia) return null;
        // NvAPI_SYS_GetLUIDFromDisplayID writes a 16-byte LUID; its second 32-bit word names the display's key.
        uint* luid = stackalloc uint[4];
        return getLuidFromDisplayId(nvidia.DisplayId, 1, luid) == 0 ? NvTweakColorCore.DeviceIdFromLuid(luid[1]) : null;
    }

    public unsafe int? GetHue(ConnectedDisplay display)
    {
        if (display.Nvidia is not { } nvidia) return null;
        var h = default(HueInfo);
        h.version = (uint)(sizeof(HueInfo) | (1 << 16));
        return getHue(nvidia.Handle, 0, &h) == 0 ? (int)(h.currentAngle % 360) : null;
    }

    public bool SetHue(ConnectedDisplay display, int angle) =>
        display.Nvidia is { } nvidia && setHue(nvidia.Handle, 0, (uint)(((angle % 360) + 360) % 360)) == 0;

    public unsafe bool SetGamma(ConnectedDisplay display, double brightness, double contrast, double gamma)
    {
        if (display.Nvidia is not { } nvidia) return false;
        var data = default(GammaCorrectionEx);
        data.version = (uint)(sizeof(GammaCorrectionEx) | (1 << 16));
        data.unknown = 1;
        var b = Math.Clamp(brightness, 80.0, 120.0);
        var c = Math.Clamp(contrast, 80.0, 120.0);
        var g = Math.Clamp(gamma, 0.3, 2.8);
        var contrastNorm = (c - 100.0) / 100.0;
        var brightnessShift = (b - 100.0) / 100.0;
        var gammaInv = 1.0 / g;
        for (var i = 0; i < 1024; i++)
        {
            var x = i / 1023.0;
            var v = contrastNorm <= 0.0
                ? (contrastNorm + 1.0) * (x - 0.5)
                : (x - 0.5) / Math.Max(1.0 - contrastNorm, 1e-6);
            v = Math.Clamp(v + brightnessShift + 0.5, 0.0, 1.0);
            v = Math.Clamp(Math.Pow(v, gammaInv), 0.0, 1.0);
            var f = (float)v;
            data.gammaRamp[i * 3] = f;
            data.gammaRamp[i * 3 + 1] = f;
            data.gammaRamp[i * 3 + 2] = f;
        }
        return setGamma(nvidia.DisplayId, &data) == 0;
    }

    public bool Apply(ConnectedDisplay display, int vibrance, double brightness, double contrast, double gamma, int hue) =>
        SetVibrance(display, vibrance) && SetHue(display, hue) && SetGamma(display, brightness, contrast, gamma);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { unload(); } catch { }
        NativeLibrary.Free(module);
    }
}

/// <summary>NvAPI_Initialize failed; <see cref="Status"/> is the NVAPI status it returned.</summary>
internal sealed class NvApiInitializationException(int status) : InvalidOperationException($"NvAPI_Initialize failed: {status}")
{
    /// <summary>NVAPI_NVIDIA_DEVICE_NOT_FOUND: the library is installed but no NVIDIA GPU is active.</summary>
    public const int NvidiaDeviceNotFound = -6;

    public int Status { get; } = status;
}
