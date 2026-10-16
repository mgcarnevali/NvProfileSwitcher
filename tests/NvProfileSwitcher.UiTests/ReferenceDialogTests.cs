using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace NvProfileSwitcher.UiTests;

// The reference dialogs are copied unchanged; they must load with the application styles
// and resources they depend on.
public sealed class ReferenceDialogTests
{
    [AvaloniaFact]
    public void ManageDisplaysUsesTheCenteredColumnHeaderTheme()
    {
        var theme = Assert.IsType<ControlTheme>(
            Avalonia.Application.Current!.Resources["CenteredTableViewColumnHeaderTheme"]);
        var window = new ManageDisplaysWindow(
            [new SavedDisplayInfo("DEL-123", "Display", connected: false)],
            _ => { });
        window.Show();

        var headers = window.GetVisualDescendants().OfType<TableViewColumnHeader>().ToList();
        Assert.Equal(2, headers.Count);
        Assert.All(headers, header => Assert.Same(theme, header.Theme));
        window.Close();
    }

    [AvaloniaFact]
    public void AboutAndUpdateDialogsLoad()
    {
        var about = new AboutWindow();
        about.Show();
        about.Close();

        var update = new UpdateAvailableWindow();
        update.Show();
        update.Close();
    }
}
