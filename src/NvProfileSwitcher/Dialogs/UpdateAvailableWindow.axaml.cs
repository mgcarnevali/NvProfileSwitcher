using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NvProfileSwitcher;

public partial class UpdateAvailableWindow : Window
{
    private Func<IProgress<string>, Task<bool>>? install;

    public UpdateAvailableWindow()
    {
        InitializeComponent();
    }

    internal void Configure(UpdateRelease updateRelease, Func<IProgress<string>, Task<bool>> installRelease)
    {
        install = installRelease;
        HeadingText.Text = $"NvProfileSwitcher {updateRelease.Version} is available";
        CurrentVersionText.Text = $"You are currently running version {BuildInfo.Version}.";
    }

    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        if (install is null) return;

        UpdateButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        ProgressText.IsVisible = true;
        var progress = new Progress<string>(message => ProgressText.Text = message);

        try
        {
            // Not closing: the update was cancelled, and the dialog is as it was before.
            if (!await install(progress))
            {
                ProgressText.IsVisible = false;
                UpdateButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            ProgressText.Text = "Update failed. Your current installation was not changed.\n" + ex.Message;
            UpdateButton.IsEnabled = true;
            LaterButton.IsEnabled = true;
        }
    }

    private void Later_Click(object? sender, RoutedEventArgs e) => Close();
}
