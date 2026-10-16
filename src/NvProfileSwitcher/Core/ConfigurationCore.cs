using System.Text.Json;

namespace NvProfileSwitcher;

internal static class ConfigurationCore
{
    internal static bool TryValidateImportedConfiguration(string json, out AppConfig config)
    {
        config = null!;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasArray(root, "Windows Profiles") ||
                !HasArray(root, "Application Profiles") ||
                !HasBoolean(root, nameof(AppConfig.StartWithWindows)) ||
                !HasBoolean(root, nameof(AppConfig.StartMinimized)) ||
                !HasBoolean(root, nameof(AppConfig.MinimizeToTray)) ||
                !HasBoolean(root, nameof(AppConfig.AlwaysOnTop)) ||
                !HasBoolean(root, nameof(AppConfig.CheckForUpdates)) ||
                !HasBoolean(root, nameof(AppConfig.RememberGlobalSwitching)) ||
                !HasBoolean(root, nameof(AppConfig.RememberOverride)) ||
                !HasBoolean(root, nameof(AppConfig.GlobalSwitchingEnabled)) ||
                !HasOverrideMode(root, nameof(AppConfig.OverrideMode)) ||
                !HasNullableGuid(root, nameof(AppConfig.OverrideProfileId)) ||
                !HasInteger(root, nameof(AppConfig.ShowHideHotkey)) ||
                !HasInteger(root, nameof(AppConfig.WindowsOverrideHotkey)) ||
                !HasInteger(root, nameof(AppConfig.ResumeAutomaticHotkey)) ||
                !HasInteger(root, nameof(AppConfig.GlobalSwitchingHotkey)) ||
                !HasOptionalStringArray(root, "Non-unique Monitor Ids"))
                return false;

            foreach (var display in root.GetProperty("Windows Profiles").EnumerateArray())
                if (!ValidateDisplayProfileJson(display, requireName: true)) return false;

            var profileIds = new HashSet<Guid>();
            foreach (var profile in root.GetProperty("Application Profiles").EnumerateArray())
            {
                if (profile.ValueKind != JsonValueKind.Object ||
                    !HasGuid(profile, nameof(ApplicationProfileConfig.Id), out var profileId) ||
                    !profileIds.Add(profileId) ||
                    !HasString(profile, nameof(ApplicationProfileConfig.Name)) ||
                    !HasExecutablePaths(profile) ||
                    !HasBoolean(profile, nameof(ApplicationProfileConfig.Enabled)) ||
                    !HasInteger(profile, nameof(ApplicationProfileConfig.Hotkey)) ||
                    !HasArray(profile, "Display Profiles")) return false;

                if (profile.TryGetProperty("Exe Paths", out var exePaths) &&
                    (exePaths.ValueKind != JsonValueKind.Array || exePaths.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String)))
                    return false;

                foreach (var display in profile.GetProperty("Display Profiles").EnumerateArray())
                    if (!ValidateDisplayProfileJson(display, requireName: false)) return false;
            }

            var parsed = JsonSerializer.Deserialize(json, NvProfileSwitcherJsonContext.Default.AppConfig);
            if (parsed is null || parsed.WindowsProfiles is null || parsed.ApplicationProfiles is null) return false;
            if (parsed.ApplicationProfiles.Any(p => p is null || p.DisplayProfiles is null)) return false;
            if (parsed.OverrideMode == OverrideMode.Profile)
            {
                if (parsed.OverrideProfileId is not Guid overrideId ||
                    !parsed.ApplicationProfiles.Any(p => p.Id == overrideId)) return false;
            }
            else if (parsed.OverrideProfileId is not null) return false;
            config = parsed;
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static bool ValidateDisplayProfileJson(JsonElement display, bool requireName) =>
        display.ValueKind == JsonValueKind.Object &&
        (!requireName || HasString(display, nameof(ProfileDisplaySettings.Name))) &&
        HasString(display, nameof(ProfileDisplaySettings.DisplayName)) &&
        HasString(display, nameof(ProfileDisplaySettings.MonitorId)) &&
        HasNumber(display, nameof(ProfileDisplaySettings.Brightness)) &&
        HasNumber(display, nameof(ProfileDisplaySettings.Contrast)) &&
        HasNumber(display, nameof(ProfileDisplaySettings.Gamma)) &&
        HasInteger(display, nameof(ProfileDisplaySettings.DigitalVibrance)) &&
        HasInteger(display, nameof(ProfileDisplaySettings.Hue));

    private static bool HasExecutablePaths(JsonElement profile) =>
        profile.TryGetProperty("Exe Paths", out var paths) &&
        paths.ValueKind == JsonValueKind.Array &&
        paths.GetArrayLength() > 0 &&
        paths.EnumerateArray().All(value => value.ValueKind == JsonValueKind.String);

    private static bool HasGuid(JsonElement element, string name, out Guid id)
    {
        id = Guid.Empty;
        return element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String &&
               Guid.TryParse(value.GetString(), out id) &&
               id != Guid.Empty;
    }

    private static bool HasOverrideMode(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        Enum.TryParse<OverrideMode>(value.GetString(), ignoreCase: false, out _);

    private static bool HasNullableGuid(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.Null) return true;
        return value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id) && id != Guid.Empty;
    }

    // Absent or null in configurations without shared monitor identities.
    private static bool HasOptionalStringArray(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) ||
        value.ValueKind == JsonValueKind.Null ||
        (value.ValueKind == JsonValueKind.Array &&
         value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())));

    private static bool HasArray(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array;
    private static bool HasString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String;
    private static bool HasBoolean(JsonElement element, string name) => element.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False);
    private static bool HasNumber(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out _);
    private static bool HasInteger(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _);
}
