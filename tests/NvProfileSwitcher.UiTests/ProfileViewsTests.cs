using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NvProfileSwitcher.Views;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;

namespace NvProfileSwitcher.UiTests;

// The profile list and editor views bound to the composed application.
public sealed class ProfileViewsTests
{
    private const string Dell = "DEL4321-ABC123";

    private const string Config = """
        {"Application Profiles":[
          {"Name":"Game","Exe Paths":["game.exe","game2.exe"],"Enabled":true,"Hotkey":0},
          {"Name":"Notepad","Exe Paths":[],"Enabled":false,"Hotkey":0}]}
        """;

    private static (TestComposition App, MainWindow Window) Show()
    {
        var app = new TestComposition(
            Config, createWindow: viewModel => new MainWindow { Width = 1180, Height = 720, DataContext = viewModel });
        app.Factory.Configure = backend => backend.Displays.Add(Display(Dell, "DELL U2720Q"));
        app.ShowActive();
        return (app, (MainWindow)app.Window);
    }

    private static T Single<T>(Control root) where T : Control => root.GetVisualDescendants().OfType<T>().Single();

    // The profile name box; the selectors' templates have text boxes of their own.
    private static TextBox NameBox(MainWindow window) =>
        Single<ProfileSettingsView>(window).GetVisualDescendants().OfType<TextBox>().Single(box => box.TemplatedParent is null);

    private static ListBox ProfileList(MainWindow window) => Single<ListBox>(Single<ProfilesView>(window));

    private static List<ListBoxItem> Rows(MainWindow window) =>
        ProfileList(window).GetVisualDescendants().OfType<ListBoxItem>().ToList();

    private static List<Button> RowButtons(ListBoxItem row) => row.GetVisualDescendants().OfType<Button>().ToList();

    [AvaloniaFact]
    public void TheListShowsTheProfilesWithWindowsSelected()
    {
        var (app, window) = Show();
        using var _ = app;

        var rows = Rows(window);
        Assert.Equal(3, rows.Count);
        Assert.True(rows[0].IsSelected);
        var tips = rows.Select(row => ToolTip.GetTip(row.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("profileRow")))).ToList();
        Assert.Equal(["Windows", "Game", "Notepad"], tips);

        // Windows shows its logo and no row buttons; application profiles their buttons.
        Assert.DoesNotContain(rows[0].GetVisualDescendants().OfType<StackPanel>(), p => p.IsVisible && p.Children.OfType<Button>().Any());
        Assert.Equal(3, RowButtons(rows[1]).Count);
        Assert.Equal(["Profile ON", "Profile OFF"], rows.Skip(1).Select(row => ToolTip.GetTip(RowButtons(row)[0])));
        Assert.Equal([false, true], RowButtons(rows[1]).Skip(1).Select(b => b.IsEnabled));
        window.Close();
    }

    [AvaloniaFact]
    public void TheRowButtonsRunTheirActionOnTheirProfile()
    {
        var (app, window) = Show();
        using var _ = app;

        RowButtons(Rows(window)[2])[0].Command!.Execute(RowButtons(Rows(window)[2])[0].CommandParameter);
        Assert.True(app.Profiles.Profiles[2].Enabled);

        var down = RowButtons(Rows(window)[1])[2];
        down.Command!.Execute(down.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Windows", "Notepad", "Game"], app.Profiles.Profiles.Select(p => p.Name));
        Assert.Equal(2, ProfileList(window).SelectedIndex);
        window.Close();
    }

    // The list drops a moved selected item; the moved profile is selected again.
    [AvaloniaFact]
    public void MovingTheSelectedRowKeepsItSelected()
    {
        var (app, window) = Show();
        using var _ = app;
        app.Profiles.Profiles[2].Enabled = true;
        ProfileList(window).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        var down = RowButtons(Rows(window)[1])[2];
        down.Command!.Execute(down.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Windows", "Notepad", "Game"], app.Profiles.Profiles.Select(p => p.Name));
        Assert.Equal(2, ProfileList(window).SelectedIndex);
        window.Close();
    }

    // Choosing an action item keeps the current executable selected.
    [AvaloniaFact]
    public void ChoosingAnExecutableActionKeepsTheSelectedExecutable()
    {
        var (app, window) = Show();
        using var _ = app;
        ProfileList(window).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var combo = Single<ProfileSettingsView>(window).GetVisualDescendants().OfType<ComboBox>().First();

        combo.SelectedIndex = 3;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, combo.SelectedIndex);
        Assert.Equal(1, app.Dialogs.ExecutableRequests);
        window.Close();
    }

    [AvaloniaFact]
    public void SelectingARowLoadsTheEditor()
    {
        var (app, window) = Show();
        using var _ = app;

        ProfileList(window).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        var editor = Single<ProfileSettingsView>(window);
        Assert.Equal("Game", NameBox(window).Text);
        var combos = editor.GetVisualDescendants().OfType<ComboBox>().ToList();
        Assert.Equal("game.exe", ((ExecutableComboItem)combos[0].SelectedItem!).Text);
        Assert.Equal(Dell, ((Nvidia.NvidiaDisplay)combos[1].SelectedItem!).MonitorId);
        Assert.Same(app.Profiles.Profiles[1], app.Editor.Session!.Source);
        window.Close();
    }

    // With unsaved changes and "Keep editing", the list goes back to the edited profile.
    [AvaloniaFact]
    public void KeepEditingSelectsTheEditedRowAgain()
    {
        var (app, window) = Show();
        using var _ = app;
        ProfileList(window).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        NameBox(window).Text = "Changed";
        app.Dialogs.DiscardAnswer = false;

        ProfileList(window).SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, ProfileList(window).SelectedIndex);
        Assert.Equal("Changed", app.Editor.Session!.Draft.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void TheEditorControlsFollowTheDraft()
    {
        var (app, window) = Show();
        using var _ = app;
        ProfileList(window).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var editor = Single<ProfileSettingsView>(window);
        var cancel = editor.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel"));
        Assert.False(cancel.IsVisible);

        var brightness = editor.GetVisualDescendants().OfType<Slider>().First();
        brightness.Value = 110;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(110, DisplayCore.FindValues(app.Editor.Session!.Draft, Dell)!.Brightness);
        Assert.Contains(editor.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "110");
        Assert.True(cancel.IsVisible);
        window.Close();
    }

    // The remove button of an executable item lives in the selector's popup.
    [AvaloniaFact]
    public void TheExecutableRemoveButtonRemovesItsExecutable()
    {
        var (app, window) = Show();
        using var _ = app;
        ProfileList(window).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var combo = Single<ProfileSettingsView>(window).GetVisualDescendants().OfType<ComboBox>().First();

        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        var items = combo.GetLogicalChildrenItems();
        var remove = items[1].GetVisualDescendants().OfType<Button>().Single();
        Assert.True(remove.IsVisible);
        remove.Command!.Execute(remove.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["game.exe"], app.Editor.Session!.Draft.ExecutablePaths);
        window.Close();
    }
}

internal static class ComboBoxTestExtensions
{
    // The item containers, which live in the drop-down popup while it is open.
    public static List<ComboBoxItem> GetLogicalChildrenItems(this ComboBox combo) =>
        Enumerable.Range(0, combo.ItemCount).Select(i => (ComboBoxItem)combo.ContainerFromIndex(i)!).ToList();
}
