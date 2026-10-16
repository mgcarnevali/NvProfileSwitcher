using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.Views;

namespace NvProfileSwitcher.UiTests;

// The footer links of the original status bar: GitHub and Support open their pages and
// About shows the About dialog.
public sealed class FooterLinksTests
{
    private static void Click(Window window, string link)
    {
        var text = window.GetVisualDescendants().OfType<StatusBarView>().Single()
            .GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Classes.Contains("footerLink") && t.Text == link);
        var center = text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("GitHub", "https://github.com/mgcarnevali/NvProfileSwitcher")]
    [InlineData("Support", "https://ko-fi.com/mgcarnevali")]
    public void LinkOpensItsPage(string link, string uri)
    {
        var dialogs = new FakeDialogService();
        var window = new MainWindow { DataContext = TestViewModels.MainWindow(dialogs) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Click(window, link);

        Assert.Equal([new Uri(uri)], dialogs.OpenedUris);
        Assert.Equal(0, dialogs.AboutCount);
        window.Close();
    }

    [AvaloniaFact]
    public void AboutShowsTheAboutDialog()
    {
        var dialogs = new FakeDialogService();
        var window = new MainWindow { DataContext = TestViewModels.MainWindow(dialogs) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Click(window, "About");

        Assert.Equal(1, dialogs.AboutCount);
        Assert.Empty(dialogs.OpenedUris);
        window.Close();
    }
}
