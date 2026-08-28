using NvAPIWrapper;
using NvDisplay = NvAPIWrapper.Display.Display;
using WinDisplay = WindowsDisplayAPI.Display;
using WindowsDisplayAPI;

namespace GameProfileSwitcher;

public sealed class ColorController : IDisposable
{
    private NvDisplay? _nvPrimary;
    private WinDisplay? _winPrimary;
    private bool _nvInitialized;

    public string Status { get; private set; } = "Not initialized";

    public bool Initialize()
    {
        try
        {
            NVIDIA.Initialize();
            _nvInitialized = true;

            _nvPrimary = GetNvidiaPrimaryDisplay();
            _winPrimary = WinDisplay.GetDisplays().FirstOrDefault(d => d.DisplayScreen?.IsPrimary == true);

            if (_nvPrimary is null)
                throw new InvalidOperationException("Could not map the NVIDIA primary display.");
            if (_winPrimary is null)
                throw new InvalidOperationException("Could not find the Windows primary display.");

            Status = "Ready - primary display mapped";
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
        if (_nvPrimary is null || _winPrimary is null)
            throw new InvalidOperationException("Color controller is not initialized.");

        var vibrance = Math.Clamp(profile.DigitalVibrance, 0, 100);
        var brightness = Math.Clamp(profile.Brightness, 0.0, 1.0);
        var contrast = Math.Clamp(profile.Contrast, 0.0, 1.0);
        var gamma = Math.Clamp(profile.Gamma, 0.50, 3.00);

        // NVIDIA Digital Vibrance through NVAPI.
        _nvPrimary.DigitalVibranceControl.CurrentLevel = vibrance;

        // Brightness / Contrast / Gamma through the Windows hardware gamma ramp.
        // This is the same approach used by several existing NVIDIA color toggle/profile tools.
        _winPrimary.GammaRamp = new DisplayGammaRamp(brightness, contrast, gamma);

        Status = $"Applied: {profile.Name}";
    }

    private static NvDisplay? GetNvidiaPrimaryDisplay()
    {
        var displays = NvDisplay.GetDisplays();
        var paths = NvAPIWrapper.Display.PathInfo.GetDisplaysConfig();

        for (var i = 0; i < paths.Length && i < displays.Length; i++)
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
            try { NVIDIA.Unload(); } catch { }
        }
    }
}
