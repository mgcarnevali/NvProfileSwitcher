using System.Text.Json;
using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Services;

/// <summary>
/// Loads the configuration into the application state and saves the state back to
/// config.json, with the same file location and format as the original. It never
/// overwrites a configuration it could not load, and it tells the user when a load or a
/// save failed (deliberate deviations: the original replaced an unreadable configuration
/// with the defaults on its next save, and ignored failed saves).
/// </summary>
internal sealed class ConfigurationService(
    string configPath,
    ProfileStore profiles,
    AppSettings settings,
    SwitchingState switching,
    bool isDevelopmentBuild,
    IDialogService dialogs,
    TimeProvider? timeProvider = null)
{
    private const string MessageTitle = "NvProfileSwitcher";
    private const string SaveFailedMessage =
        "Your changes could not be saved to the configuration file.\n\nThey remain active until NvProfileSwitcher is closed.";

    // Set when config.json exists but could not be read, or was invalid and could not be
    // kept as a copy: nothing is saved over it in this session.
    private bool savingBlocked;

    // Set when the last save failed: the state holds changes config.json does not have.
    // It also keeps the failure notice to the first failure of a streak.
    private bool pendingChanges;

    private string? loadProblem;

    // The local time that names the copy of an invalid configuration; the system clock except in tests.
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public static string DefaultConfigPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NvProfileSwitcher", "config.json");

    public string ConfigPath { get; } = configPath;

    /// <summary>
    /// Loads config.json. When it does not exist, the session starts with the Windows
    /// profile and default settings, and the file is not created. When it exists but cannot
    /// be read, the session starts with the defaults and saves nothing, so the file is never
    /// overwritten. When its content is invalid, an exact copy is kept next to it first; the
    /// file is then replaced by the next save, or kept and never saved over if the copy failed.
    /// </summary>
    public void Load()
    {
        AppConfig? config = null;
        var loadedProfiles = ConfigMappingCore.DefaultProfiles();

        if (File.Exists(ConfigPath))
        {
            byte[]? content = null;
            try
            {
                content = File.ReadAllBytes(ConfigPath);
            }
            catch (Exception)
            {
                savingBlocked = true;
                loadProblem = "Your configuration could not be read, so NvProfileSwitcher started with default settings.\n\n" +
                    "Your configuration file was not modified, and changes made in this session will not be saved. " +
                    "Restart NvProfileSwitcher once the file is accessible.\n\n" + ConfigPath;
            }

            if (content is not null)
            {
                try
                {
                    using (var stream = new MemoryStream(content))
                        config = JsonSerializer.Deserialize(stream, NvProfileSwitcherJsonContext.Default.AppConfig);
                    if (config is null)
                        throw new InvalidDataException("config.json is empty or invalid.");
                    loadedProfiles = ConfigMappingCore.CreateProfiles(config);
                }
                catch (Exception)
                {
                    config = null;
                    loadedProfiles = ConfigMappingCore.DefaultProfiles();
                    KeepInvalidConfiguration(content);
                }
            }
        }

        profiles.Load(loadedProfiles);
        profiles.LoadNonUniqueMonitorIds(config?.NonUniqueMonitorIds);
        ConfigMappingCore.ApplyState(config, profiles.ApplicationProfiles, isDevelopmentBuild, settings, switching);
    }

    /// <summary>Tells the user, once, that the configuration could not be loaded.</summary>
    public void ReportLoadProblem()
    {
        if (loadProblem is not { } message)
            return;

        loadProblem = null;
        _ = dialogs.ShowMessageAsync(MessageTitle, message);
    }

    /// <summary>
    /// Saves the current state, replacing config.json through a temporary file, and returns
    /// true only once it was replaced. It never throws. The unsaved new profile is left out
    /// until it is saved (deliberate deviation: the original wrote it with any save while it
    /// existed). A failure is reported unless <paramref name="reportFailure"/> is false
    /// (the caller reports it), and only the first one until a save succeeds again.
    /// </summary>
    public bool Save(bool reportFailure = true) =>
        Write(ConfigMappingCore.CreateConfig(profiles.Profiles, settings, switching, profiles.UnsavedNewProfile, profiles.NonUniqueMonitorIds), reportFailure);

    /// <summary>
    /// Saves the state with <paramref name="replacement"/> in place of <paramref name="profile"/>,
    /// without changing the state: an edited profile is only applied once it was saved. When the
    /// profile is the unsaved new profile, its replacement is written, as it is not excluded.
    /// </summary>
    public bool Save(ProfileListItem profile, ProfileListItem replacement, bool reportFailure)
    {
        var list = profiles.Profiles.Select(p => ReferenceEquals(p, profile) ? replacement : p).ToList();
        return Write(ConfigMappingCore.CreateConfig(list, settings, switching, profiles.UnsavedNewProfile, profiles.NonUniqueMonitorIds), reportFailure);
    }

    /// <summary>Saves again, without reporting, when the last save failed.</summary>
    public bool SaveIfPending() => !pendingChanges || Save(reportFailure: false);

    /// <summary>
    /// Replaces config.json with the imported JSON exactly as it was read, through a temporary
    /// file, and applies the imported configuration to the application state. The active
    /// profile name is left as it is. The file then matches the state, so the session saves
    /// normally again.
    /// </summary>
    public async Task ImportAsync(string json, AppConfig config)
    {
        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = ConfigPath + ".import.tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, ConfigPath, true);
        savingBlocked = false;
        pendingChanges = false;

        profiles.Load(ConfigMappingCore.CreateProfiles(config));
        profiles.LoadNonUniqueMonitorIds(config.NonUniqueMonitorIds);
        ConfigMappingCore.ApplyState(config, profiles.ApplicationProfiles, isDevelopmentBuild, settings, switching);
    }

    /// <summary>Copies config.json over <paramref name="destination"/>; the original saved before choosing it.</summary>
    public async Task ExportAsync(Stream destination)
    {
        await using var source = File.OpenRead(ConfigPath);
        destination.SetLength(0);
        await source.CopyToAsync(destination);
        await destination.FlushAsync();
    }

    private bool Write(AppConfig config, bool reportFailure)
    {
        // Nothing is written over a configuration that could not be loaded; the load notice
        // already told the user, so the failure is not reported again.
        if (savingBlocked)
        {
            pendingChanges = true;
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var tempPath = ConfigPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(config, NvProfileSwitcherJsonContext.Default.AppConfig));
            File.Move(tempPath, ConfigPath, true);
        }
        catch (Exception)
        {
            if (reportFailure && !pendingChanges)
                _ = dialogs.ShowMessageAsync(MessageTitle, SaveFailedMessage);
            pendingChanges = true;
            return false;
        }

        pendingChanges = false;
        return true;
    }

    // Writes the content that could not be loaded, byte for byte, to a new file next to it
    // and checks it, before anything may replace config.json. Without a verified copy the
    // configuration is never saved over.
    private void KeepInvalidConfiguration(byte[] content)
    {
        try
        {
            var directory = Path.GetDirectoryName(ConfigPath) ?? string.Empty;
            var stamp = time.GetLocalNow().DateTime.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            for (var attempt = 0; ; attempt++)
            {
                var name = attempt == 0 ? $"config.invalid-{stamp}.json" : $"config.invalid-{stamp}-{attempt}.json";
                var backupPath = Path.Combine(directory, name);
                try
                {
                    using (var stream = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write))
                    {
                        stream.Write(content);
                        stream.Flush(flushToDisk: true);
                    }
                }
                catch (IOException) when (File.Exists(backupPath) && attempt < 100)
                {
                    continue;
                }

                if (!File.ReadAllBytes(backupPath).AsSpan().SequenceEqual(content))
                    throw new IOException("The copy of the configuration does not match it.");

                loadProblem = "Your configuration file is not valid, so NvProfileSwitcher started with default settings.\n\n" +
                    "The original file was kept for manual recovery as:\n" + backupPath;
                return;
            }
        }
        catch (Exception)
        {
            savingBlocked = true;
            loadProblem = "Your configuration file is not valid, so NvProfileSwitcher started with default settings.\n\n" +
                "A copy of it could not be kept, so your configuration file was not modified and changes made in this " +
                "session will not be saved.\n\n" + ConfigPath;
        }
    }
}
