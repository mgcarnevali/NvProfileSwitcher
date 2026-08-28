using System.Runtime.InteropServices;
using NvAPIWrapper;
using NvDisplay = NvAPIWrapper.Display.Display;

namespace GameProfileSwitcher;

public sealed class ColorController : IDisposable
{
    private NvDisplay? _nvPrimary;
    private bool _nvInitialized;

    public string Status { get; private set; } = "Not initialized";

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool SetDeviceGammaRamp(IntPtr hDC, IntPtr lpRamp);

    public bool Initialize()
    {
        try
        {
            NVIDIA.Initialize();
            _nvInitialized = true;

            _nvPrimary = GetNvidiaPrimaryDisplay();

            if (_nvPrimary is null)
                throw new InvalidOperationException(
                    "Could not find the NVIDIA primary display.");

            Status = "Ready";
            return true;
        }
        catch (Exception ex)
        {
            Status = "Initialization error: " + ex.Message;
            return false;
        }
    }

    public void Apply(GameProfile profile)
    {
        if (_nvPrimary is null)
            throw new InvalidOperationException(
                "Color controller is not initialized.");

        int vibrance = Math.Clamp(profile.DigitalVibrance, 0, 100);
        double brightness = Math.Clamp(profile.Brightness, 0.0, 1.0);
        double contrast = Math.Clamp(profile.Contrast, 0.0, 1.0);
        double gamma = Math.Clamp(profile.Gamma, 0.50, 3.00);

        // NVIDIA Digital Vibrance through NVAPI.
        _nvPrimary.DigitalVibranceControl.CurrentLevel = vibrance;

        // Brightness / Contrast / Gamma through Windows gamma ramp.
        ApplyGammaRamp(brightness, contrast, gamma);

        Status = $"Applied: {profile.Name}";
    }

    private static void ApplyGammaRamp(
        double brightness,
        double contrast,
        double gamma)
    {
        const int rampSize = 256;
        const int channels = 3;

        ushort[] ramp = new ushort[rampSize * channels];

        for (int i = 0; i < rampSize; i++)
        {
            double value = i / 255.0;

            // Gamma
            value = Math.Pow(value, 1.0 / gamma);

            // Contrast around midpoint
            double contrastFactor = contrast / 0.5;
            value = ((value - 0.5) * contrastFactor) + 0.5;

            // Brightness: 0.5 = neutral
            value += brightness - 0.5;

            value = Math.Clamp(value, 0.0, 1.0);

            ushort output = (ushort)Math.Round(value * 65535.0);

            ramp[i] = output;
            ramp[i + 256] = output;
            ramp[i + 512] = output;
        }

        IntPtr dc = GetDC(IntPtr.Zero);

        if (dc == IntPtr.Zero)
            throw new InvalidOperationException("Could not obtain display DC.");

        IntPtr memory = IntPtr.Zero;

        try
        {
            memory = Marshal.AllocHGlobal(ramp.Length * sizeof(ushort));
            Marshal.Copy(
                ramp.Select(v => unchecked((short)v)).ToArray(),
                0,
                memory,
                ramp.Length);

            if (!SetDeviceGammaRamp(dc, memory))
                throw new InvalidOperationException(
                    "Windows rejected the gamma ramp.");
        }
        finally
        {
            if (memory != IntPtr.Zero)
                Marshal.FreeHGlobal(memory);

            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    private static NvDisplay? GetNvidiaPrimaryDisplay()
    {
        var displays = NvDisplay.GetDisplays();
        var paths = NvAPIWrapper.Display.PathInfo.GetDisplaysConfig();

        for (int i = 0; i < paths.Length && i < displays.Length; i++)
        {
            if (paths[i].IsGDIPrimary)
                return displays[i];
        }

        return displays.FirstOrDefault();
    }

    public void Dispose()
    {
        if (_nvInitialized)
        {
            try
            {
                NVIDIA.Unload();
            }
            catch
            {
            }
        }
    }
}
