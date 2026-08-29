using System.Text.Json;

namespace GameProfileSwitcher;

public static class ProfileStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GameProfileSwitcher");

    public static readonly string FilePath = Path.Combine(Folder, "profiles.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Load()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (!File.Exists(FilePath))
            {
                var settings = CreateDefaults();
                Save(settings);
                return settings;
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions)
                   ?? CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static AppSettings CreateDefaults() => new()
    {
        Profiles = new List<GameProfile>
        {
            new()
            {
                Name = "Escape from Tarkov",
                ExePath = @"C:\Battlestate Games\EFT\EscapeFromTarkov.exe",
                DigitalVibrance = 75,
                Brightness = 0.50,
                Contrast = 0.60,
                Gamma = 1.40,
                Enabled = true
            }
        }
    };
}
