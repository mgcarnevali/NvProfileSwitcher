using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services.Editing;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Services;

/// <summary>
/// Exports and imports the configuration from the Advanced Settings dialog, with the
/// original sequences. The topmost state of the main window is applied through
/// <paramref name="setTopmost"/>, so the service does not depend on the window.
/// </summary>
internal sealed class ConfigurationBackupService(
    IDialogService dialogs,
    ConfigurationService configuration,
    AppSettings settings,
    ProfileEditor editor,
    ProfileEditingService editing,
    LivePreviewController preview,
    HotkeyService hotkeys,
    DisplayProfileSyncService displaySync,
    StartupRegistrationService startupRegistration,
    AutomaticSwitchingService automaticSwitching,
    Action<bool> setTopmost)
{
    private const string InvalidTitle = "Invalid configuration file";
    private const string InvalidMessage = "The selected file is not a valid NvProfileSwitcher configuration.";

    /// <summary>
    /// Saves the configuration, as the original did before choosing the file, and copies it to
    /// the chosen file. When it could not be saved, nothing is exported, as the file would not
    /// match the current settings.
    /// </summary>
    public async Task ExportAsync()
    {
        if (!configuration.Save(reportFailure: false))
        {
            await ShowExportFailedAsync();
            return;
        }

        try
        {
            if (await dialogs.PickExportFileAsync() is not { } openDestination) return;
            await using var destination = await openDestination();
            await configuration.ExportAsync(destination);
        }
        catch (Exception)
        {
            await ShowExportFailedAsync();
        }
    }

    private Task ShowExportFailedAsync() =>
        dialogs.ShowMessageAsync("Export configuration", "The configuration could not be exported.");

    /// <summary>
    /// Imports a validated configuration file and applies it as the original did. Returns
    /// whether the imported configuration was applied to the application state.
    /// </summary>
    /// <remarks>
    /// With unsaved changes, the discard is confirmed before anything changes and "Keep
    /// editing" cancels the import (deliberate deviation: the original imported first and
    /// asked afterwards, and keeping the changes left the editor on a profile that no longer
    /// existed). The active profile is cleared after the hotkeys are registered again, before
    /// the global switching state is applied.
    /// </remarks>
    public async Task<bool> ImportAsync()
    {
        if (await dialogs.PickImportFileAsync() is not { } openSource) return false;

        string json;
        AppConfig importedConfig;
        try
        {
            await using var stream = await openSource();
            using var reader = new StreamReader(stream);
            json = await reader.ReadToEndAsync();
            if (!ConfigurationCore.TryValidateImportedConfiguration(json, out importedConfig))
            {
                await dialogs.ShowMessageAsync(InvalidTitle, InvalidMessage);
                return false;
            }
        }
        catch (Exception)
        {
            await dialogs.ShowMessageAsync(InvalidTitle, InvalidMessage);
            return false;
        }

        if (!await editing.ConfirmAndDiscardAsync())
            return false;

        // The hotkeys are released while the whole list is replaced, so no key is held while the
        // file is written, and registered again for the imported profiles, also when the import
        // fails, before its message.
        var applied = false;
        try
        {
            hotkeys.UnregisterAll();
            try
            {
                preview.Discard();
                await configuration.ImportAsync(json, importedConfig);
                applied = true;
                setTopmost(settings.AlwaysOnTop);
                displaySync.Synchronize();
                startupRegistration.Apply();
                editor.BeginFirstProfile();
            }
            finally
            {
                hotkeys.RegisterAll(showAggregateMessage: true);
            }

            automaticSwitching.ApplyImportedState();
        }
        catch (Exception)
        {
            await dialogs.ShowMessageAsync("Import configuration", "The configuration could not be imported.");
        }

        return applied;
    }
}
