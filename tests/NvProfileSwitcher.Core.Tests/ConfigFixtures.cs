using System.Text.Json;
using System.Text.Json.Nodes;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// Configuration files and helpers shared by the configuration tests.
internal static class ConfigFixtures
{
    internal const string GameId = "6f1c2a3b-4d5e-4f60-8a71-9b0c1d2e3f40";
    internal const string TarkovId = "0a1b2c3d-4e5f-4061-8273-8495a6b7c8d9";

    // A configuration exactly as the original save writes it in a release build: indented,
    // properties in declaration order, Name omitted for application displays and non-ASCII
    // or HTML-sensitive characters escaped. Loading and saving it must produce the same bytes.
    internal static readonly string Canonical = """
        {
          "Windows Profiles": [
            {
              "Name": "Display 1",
              "DisplayName": "DELL U2720Q",
              "MonitorId": "DEL4321-ABC123",
              "Brightness": 100,
              "Contrast": 100,
              "Gamma": 1,
              "DigitalVibrance": 50,
              "Hue": 0
            },
            {
              "Name": "Display 2",
              "DisplayName": "LG ULTRAGEAR",
              "MonitorId": "GSM5B7F-305NTAB1C2D3",
              "Brightness": 98,
              "Contrast": 102,
              "Gamma": 0.95,
              "DigitalVibrance": 55,
              "Hue": 0
            }
          ],
          "Application Profiles": [
            {
              "Id": "6f1c2a3b-4d5e-4f60-8a71-9b0c1d2e3f40",
              "Name": "Caf\u00E9 \u0026 Games",
              "Exe Paths": [
                "C:\\Games\\Caf\u00E9\\game\u002B.exe"
              ],
              "Enabled": true,
              "Hotkey": 628,
              "Display Profiles": [
                {
                  "DisplayName": "DELL U2720Q",
                  "MonitorId": "DEL4321-ABC123",
                  "Brightness": 105,
                  "Contrast": 110,
                  "Gamma": 1.15,
                  "DigitalVibrance": 80,
                  "Hue": 10
                },
                {
                  "DisplayName": "LG ULTRAGEAR",
                  "MonitorId": "GSM5B7F-305NTAB1C2D3",
                  "Brightness": 100,
                  "Contrast": 100,
                  "Gamma": 1,
                  "DigitalVibrance": 60,
                  "Hue": 355
                }
              ]
            },
            {
              "Id": "0a1b2c3d-4e5f-4061-8273-8495a6b7c8d9",
              "Name": "Tarkov",
              "Exe Paths": [
                "D:\\Games\\EscapeFromTarkov.exe",
                "D:\\Games\\EscapeFromTarkovArena.exe"
              ],
              "Enabled": false,
              "Hotkey": 0,
              "Display Profiles": []
            }
          ],
          "StartWithWindows": true,
          "StartMinimized": true,
          "MinimizeToTray": true,
          "AlwaysOnTop": false,
          "CheckForUpdates": true,
          "RememberGlobalSwitching": true,
          "RememberOverride": true,
          "GlobalSwitchingEnabled": false,
          "OverrideMode": "Profile",
          "OverrideProfileId": "6f1c2a3b-4d5e-4f60-8a71-9b0c1d2e3f40",
          "ShowHideHotkey": 1648,
          "WindowsOverrideHotkey": 1649,
          "ResumeAutomaticHotkey": 1650,
          "GlobalSwitchingHotkey": 1651
        }
        """;

    // The serializer writes Environment.NewLine, as the original save did on each platform.
    internal static string WithPlatformNewLines(string json) => json.ReplaceLineEndings(Environment.NewLine);

    internal static JsonObject Node(string json) => JsonNode.Parse(json)!.AsObject();

    internal static string Variant(Action<JsonObject> change)
    {
        var node = Node(Canonical);
        change(node);
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // Paths whose values differ between two JSON documents, as "path: before -> after".
    internal static List<string> Differences(string before, string after)
    {
        var differences = new List<string>();
        Compare(JsonNode.Parse(before), JsonNode.Parse(after), "$", differences);
        return differences;
    }

    private static void Compare(JsonNode? a, JsonNode? b, string path, List<string> differences)
    {
        if (a is JsonObject objectA && b is JsonObject objectB)
        {
            foreach (var name in objectA.Select(p => p.Key).Union(objectB.Select(p => p.Key)))
                Compare(objectA[name], objectB[name], $"{path}.{name}", differences);
            return;
        }

        if (a is JsonArray arrayA && b is JsonArray arrayB && arrayA.Count == arrayB.Count)
        {
            for (var i = 0; i < arrayA.Count; i++)
                Compare(arrayA[i], arrayB[i], $"{path}[{i}]", differences);
            return;
        }

        var textA = a?.ToJsonString() ?? "null";
        var textB = b?.ToJsonString() ?? "null";
        if (textA != textB)
            differences.Add($"{path}: {textA} -> {textB}");
    }
}

// A configuration service over a temporary config.json with fresh state objects.
internal sealed class ConfigSession : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "NvProfileSwitcher.Tests", Guid.NewGuid().ToString("N"));

    public ConfigSession(string? json = null, bool isDevelopmentBuild = false, bool createDirectory = true, TimeProvider? time = null)
    {
        Profiles = new ProfileStore(Switching);
        if (createDirectory)
            Directory.CreateDirectory(directory);
        if (json is not null)
            File.WriteAllText(ConfigPath, json);

        Service = new ConfigurationService(ConfigPath, Profiles, Settings, Switching, isDevelopmentBuild, Dialogs, time);
    }

    public string ConfigPath => Path.Combine(directory, "config.json");
    public SwitchingState Switching { get; } = new();
    public ProfileStore Profiles { get; }
    public AppSettings Settings { get; } = new();
    public ConfigurationService Service { get; }
    public FakeDialogService Dialogs { get; } = new();
    public string ConfigDirectory => directory;

    public string SaveAndRead()
    {
        Assert.True(Service.Save());
        Assert.False(File.Exists(ConfigPath + ".tmp"));
        return File.ReadAllText(ConfigPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
