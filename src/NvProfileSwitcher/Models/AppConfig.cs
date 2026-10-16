namespace NvProfileSwitcher;

internal enum OverrideMode { Automatic, Windows, Profile }

internal sealed class AppConfig
{
    [System.Text.Json.Serialization.JsonPropertyName("Windows Profiles")]
    public List<ProfileDisplaySettings> WindowsProfiles { get; set; } = [];

    [System.Text.Json.Serialization.JsonPropertyName("Application Profiles")]
    public List<ApplicationProfileConfig> ApplicationProfiles { get; set; } = [];

    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool CheckForUpdates { get; set; }
    public bool RememberGlobalSwitching { get; set; }
    public bool RememberOverride { get; set; }
    public bool GlobalSwitchingEnabled { get; set; } = true;
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<OverrideMode>))]
    public OverrideMode OverrideMode { get; set; } = OverrideMode.Automatic;
    public Guid? OverrideProfileId { get; set; }
    public int ShowHideHotkey { get; set; }
    public int WindowsOverrideHotkey { get; set; }
    public int ResumeAutomaticHotkey { get; set; }
    public int GlobalSwitchingHotkey { get; set; }

    /// <summary>
    /// Physical monitor identities found shared by two connected monitors, which therefore use
    /// their connection identity; written only when there is any.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("Non-unique Monitor Ids")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? NonUniqueMonitorIds { get; set; }
}

internal sealed class ApplicationProfileConfig
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("Exe Paths")]
    public List<string> ExePaths { get; set; } = [];

    public bool Enabled { get; set; }
    public int Hotkey { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("Display Profiles")]
    public List<ProfileDisplaySettings> DisplayProfiles { get; set; } = [];
}
