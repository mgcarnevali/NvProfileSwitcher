using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NvProfileSwitcher;

/// <summary>The two Remember settings, applied and saved only with Save.</summary>
internal readonly record struct AdvancedSettingsResult(
    bool RememberGlobalSwitching,
    bool RememberOverride);

/// <summary>
/// The current Remember settings and the immediate actions of the dialog. <see cref="Import"/>
/// returns whether an imported configuration was applied; the dialog then shows the imported
/// Remember settings, read with <see cref="CurrentState"/>, so a later Save keeps them.
/// </summary>
internal sealed record AdvancedSettingsOptions(
    AdvancedSettingsResult Initial,
    Func<AdvancedSettingsResult> CurrentState,
    Func<Task> CheckForUpdates,
    Func<Task> ManageDisplays,
    Func<Task<bool>> Import,
    Func<Task> Export);

internal partial class AdvancedSettingsWindow : Window
{
    private readonly AdvancedSettingsOptions? options;

    // Used by the XAML previewer and loader only.
    public AdvancedSettingsWindow() => InitializeComponent();

    public AdvancedSettingsWindow(AdvancedSettingsOptions options)
    {
        InitializeComponent();
        this.options = options;
        ShowState(options.Initial);
    }

    private void ShowState(AdvancedSettingsResult state)
    {
        RememberGlobalSwitchingCheckBox.IsChecked = state.RememberGlobalSwitching;
        RememberOverrideCheckBox.IsChecked = state.RememberOverride;
    }

    private async void CheckNow_Click(object? sender, RoutedEventArgs e)
    {
        if (options is null) return;
        CheckNowButton.IsEnabled = false;
        try
        {
            await options.CheckForUpdates();
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    private async void ManageDisplays_Click(object? sender, RoutedEventArgs e)
    {
        if (options is not null)
            await options.ManageDisplays();
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        if (options is not null && await options.Import())
            ShowState(options.CurrentState());
    }

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (options is not null)
            await options.Export();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void Save_Click(object? sender, RoutedEventArgs e) =>
        Close(new AdvancedSettingsResult(
            RememberGlobalSwitchingCheckBox.IsChecked == true,
            RememberOverrideCheckBox.IsChecked == true));
}
