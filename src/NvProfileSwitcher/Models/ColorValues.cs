namespace NvProfileSwitcher.Models;

/// <summary>
/// The five color values shown by the Profile Settings sliders, with the slider ranges.
/// Vibrance and hue keep the slider value; they are rounded where the original rounded them.
/// </summary>
internal readonly record struct ColorValues(double Brightness, double Contrast, double Gamma, double Vibrance, double Hue)
{
    public const double MinimumBrightness = 80, MaximumBrightness = 120;
    public const double MinimumContrast = 80, MaximumContrast = 120;
    public const double MinimumGamma = 0.3, MaximumGamma = 2.8;
    public const double MinimumVibrance = 0, MaximumVibrance = 100;
    public const double MinimumHue = 0, MaximumHue = 359;

    /// <summary>The NVIDIA neutral values, which are also the sliders' initial values.</summary>
    public static ColorValues Defaults { get; } = new(100, 100, 1, 50, 0);

    /// <summary>The vibrance as saved and applied: <c>(int)Math.Round</c>, as in the original.</summary>
    public int RoundedVibrance => (int)Math.Round(Vibrance);

    /// <summary>The hue as saved and applied: <c>(int)Math.Round</c>, as in the original.</summary>
    public int RoundedHue => (int)Math.Round(Hue);

    /// <summary>The values as the sliders hold them: each one limited to its slider range.</summary>
    public ColorValues ClampedToSliders() => new(
        Math.Clamp(Brightness, MinimumBrightness, MaximumBrightness),
        Math.Clamp(Contrast, MinimumContrast, MaximumContrast),
        Math.Clamp(Gamma, MinimumGamma, MaximumGamma),
        Math.Clamp(Vibrance, MinimumVibrance, MaximumVibrance),
        Math.Clamp(Hue, MinimumHue, MaximumHue));
}
