using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace NvProfileSwitcher.Shell;

/// <summary>Shows dialogs owned by the main window and opens links.</summary>
internal interface IDialogService
{
    /// <summary>Shows a message with an OK button.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>
    /// Offers the release; <paramref name="install"/> installs it and returns whether the
    /// application is closing for it.
    /// </summary>
    Task ShowUpdateAvailableAsync(UpdateRelease release, Func<IProgress<string>, Task<bool>> install);

    Task ShowAboutAsync();

    /// <summary>Opens the link with the system launcher of the main window.</summary>
    Task OpenUriAsync(Uri uri);

    /// <summary>Asks whether to discard the unsaved changes, or the unsaved new profile.</summary>
    Task<bool> ConfirmDiscardAsync(bool isNewProfile);

    /// <summary>Asks whether to remove the profile.</summary>
    Task<bool> ConfirmRemoveProfileAsync(string profileName);

    /// <summary>Asks which profile to copy color settings from; the chosen index, or null.</summary>
    Task<int?> ChooseCopySourceAsync(IReadOnlyList<string> profileNames);

    /// <summary>Lets the user pick a running application; its executable path, or null.</summary>
    Task<string?> PickRunningAppAsync();

    /// <summary>Lets the user pick an executable file; its local path, or null.</summary>
    Task<string?> PickExecutableAsync();

    /// <summary>
    /// Shows the Advanced Settings dialog over the main window; the Remember settings chosen
    /// with Save, or null. While it is open, it owns every other dialog and file picker.
    /// </summary>
    Task<AdvancedSettingsResult?> ShowAdvancedSettingsAsync(AdvancedSettingsOptions options);

    /// <summary>Shows the saved displays; <paramref name="remove"/> removes a confirmed one.</summary>
    Task ShowManageDisplaysAsync(IReadOnlyList<SavedDisplayInfo> displays, Action<string> remove);

    /// <summary>Lets the user pick a configuration file to import; a function that opens it for reading, or null.</summary>
    Task<Func<Task<Stream>>?> PickImportFileAsync();

    /// <summary>Lets the user choose where to export the configuration; a function that opens it for writing, or null.</summary>
    Task<Func<Task<Stream>>?> PickExportFileAsync();
}

internal sealed class DialogService(MainWindowHost host) : IDialogService
{
    private static readonly FilePickerFileType JsonConfigurationFileType = new("JSON configuration")
    {
        Patterns = ["*.json"]
    };

    // The open Advanced Settings dialog, which owns the dialogs opened from it.
    private Window? advancedSettings;

    /// <summary>
    /// Shows the dialog modally over the main window, or as a standalone window centered
    /// on screen while the main window is hidden or minimized.
    /// </summary>
    public async Task ShowDialogOrStandaloneAsync(Window dialog)
    {
        if (advancedSettings is { } advanced)
        {
            await dialog.ShowDialog(advanced);
            return;
        }

        if (host.Owner is { } owner && host.IsShownNormally)
        {
            await dialog.ShowDialog(owner);
            return;
        }

        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Closed += (_, _) => closed.TrySetResult();
        dialog.Show();
        await closed.Task;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var ok = new Button { Content = "OK", MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new CenteredDialogWindow
        {
            Title = title,
            Width = 500,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(22), Spacing = 20,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Classes = { "dialogMessage" } },
                    ok
                }
            }
        };
        ok.Click += (_, _) => dialog.Close();
        await ShowDialogOrStandaloneAsync(dialog);
    }

    public async Task ShowUpdateAvailableAsync(UpdateRelease release, Func<IProgress<string>, Task<bool>> install)
    {
        var dialog = new UpdateAvailableWindow();
        dialog.Configure(release, install);
        await ShowDialogOrStandaloneAsync(dialog);
    }

    public async Task ShowAboutAsync()
    {
        var dialog = new AboutWindow();
        await ShowDialogOrStandaloneAsync(dialog);
    }

    public async Task OpenUriAsync(Uri uri)
    {
        if (host.Owner is { } owner)
            await owner.Launcher.LaunchUriAsync(uri);
    }

    public async Task<bool> ConfirmDiscardAsync(bool isNewProfile)
    {
        var discard = new Button { Content = "Discard", MinWidth = 100 };
        var keep = new Button { Content = "Keep editing", MinWidth = 110 };
        var dialog = ConfirmationDialog(
            isNewProfile ? "Discard profile?" : "Discard changes?",
            isNewProfile
                ? "This profile has not been saved. Do you want to discard it?"
                : "You have unsaved changes to this profile. Do you want to discard them?",
            keep, discard);
        var result = false;
        discard.Click += (_, _) => { result = true; dialog.Close(); };
        keep.Click += (_, _) => dialog.Close();
        await ShowDialogOrStandaloneAsync(dialog);
        return result;
    }

    public async Task<bool> ConfirmRemoveProfileAsync(string profileName)
    {
        var remove = new Button { Content = "Remove", MinWidth = 100 };
        var cancel = new Button { Content = "Cancel", MinWidth = 100 };
        var dialog = ConfirmationDialog(
            "Remove profile?", $"Are you sure you want to remove profile '{profileName}'?", cancel, remove);
        var confirmed = false;
        remove.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await ShowDialogOrStandaloneAsync(dialog);
        return confirmed;
    }

    public async Task<int?> ChooseCopySourceAsync(IReadOnlyList<string> profileNames)
    {
        var sourceCombo = new ComboBox { ItemsSource = profileNames.ToList(), SelectedIndex = 0, MinWidth = 280 };
        var copy = new Button { Content = "Copy", MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var dialog = new CenteredDialogWindow
        {
            Title = "Copy settings",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(22), Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Select the profile whose color settings you want to copy.",
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { "dialogMessage" }
                    },
                    sourceCombo,
                    Buttons(cancel, copy)
                }
            }
        };
        int? selected = null;
        copy.Click += (_, _) =>
        {
            if (sourceCombo.SelectedIndex >= 0 && sourceCombo.SelectedIndex < profileNames.Count)
                selected = sourceCombo.SelectedIndex;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        await ShowDialogOrStandaloneAsync(dialog);
        return selected;
    }

    // Shown only from the visible editor, modal over the main window, as in the original.
    public async Task<string?> PickRunningAppAsync() =>
        host.Owner is { } owner ? await new RunningAppsWindow().ShowDialog<string?>(owner) : null;

    public async Task<string?> PickExecutableAsync()
    {
        if (host.Owner is not { } owner)
            return null;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select application executable",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Windows executable") { Patterns = ["*.exe"] }]
        });
        if (files.Count != 1) return null;
        var path = files[0].TryGetLocalPath();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public async Task<AdvancedSettingsResult?> ShowAdvancedSettingsAsync(AdvancedSettingsOptions options)
    {
        if (host.Owner is not { } owner)
            return null;

        var dialog = new AdvancedSettingsWindow(options);
        advancedSettings = dialog;
        try
        {
            return await dialog.ShowDialog<AdvancedSettingsResult?>(owner);
        }
        finally
        {
            advancedSettings = null;
        }
    }

    public async Task ShowManageDisplaysAsync(IReadOnlyList<SavedDisplayInfo> displays, Action<string> remove)
    {
        if (DialogOwner() is { } owner)
            await new ManageDisplaysWindow(displays, remove).ShowDialog(owner);
    }

    public async Task<Func<Task<Stream>>?> PickImportFileAsync()
    {
        if (DialogOwner() is not { } owner)
            return null;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import configuration",
            AllowMultiple = false,
            FileTypeFilter = [JsonConfigurationFileType]
        });
        if (files.Count != 1) return null;
        var file = files[0];
        return file.OpenReadAsync;
    }

    public async Task<Func<Task<Stream>>?> PickExportFileAsync()
    {
        if (DialogOwner() is not { } owner)
            return null;

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export configuration",
            SuggestedFileName = "NvProfileSwitcher-Config.json",
            DefaultExtension = "json",
            FileTypeChoices = [JsonConfigurationFileType],
            ShowOverwritePrompt = true
        });
        if (file is null) return null;
        return file.OpenWriteAsync;
    }

    // The open Advanced Settings dialog, or else the main window.
    private Window? DialogOwner() => advancedSettings ?? host.Owner;

    private static CenteredDialogWindow ConfirmationDialog(string title, string message, Button first, Button second) => new()
    {
        Title = title,
        Width = 500,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        Content = new StackPanel
        {
            Margin = new Thickness(22), Spacing = 20,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Classes = { "dialogMessage" } },
                Buttons(first, second)
            }
        }
    };

    private static StackPanel Buttons(Button first, Button second) => new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = 8,
        Children = { first, second }
    };
}
