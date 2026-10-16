using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NvProfileSwitcher;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {BuildInfo.AboutVersion}";
    }

    private async void GitHub_Click(object? sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri("https://github.com/mgcarnevali/NvProfileSwitcher"));
    }

    private async void Support_Click(object? sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri("https://ko-fi.com/mgcarnevali"));
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
