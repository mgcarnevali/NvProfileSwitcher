namespace GameProfileSwitcher;

public sealed class GameProfile
{
    public string Name { get; set; } = "New Profile";
    public string ExePath { get; set; } = "";
    public int DigitalVibrance { get; set; } = 50;
    public double Brightness { get; set; } = 0.50;
    public double Contrast { get; set; } = 0.50;
    public double Gamma { get; set; } = 1.00;
    public bool Enabled { get; set; } = true;

    public string ProcessName => string.IsNullOrWhiteSpace(ExePath)
        ? ""
        : Path.GetFileNameWithoutExtension(ExePath);

    public override string ToString() => Name;
}

public sealed class AppSettings
{
    public GameProfile DesktopProfile { get; set; } = new()
    {
        Name = "Desktop / Normal",
        DigitalVibrance = 50,
        Brightness = 0.50,
        Contrast = 0.50,
        Gamma = 1.00,
        Enabled = true
    };

    public List<GameProfile> Profiles { get; set; } = new();
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
}
