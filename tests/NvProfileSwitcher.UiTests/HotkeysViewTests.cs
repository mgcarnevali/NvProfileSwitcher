using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NvProfileSwitcher.Views;

namespace NvProfileSwitcher.UiTests;

// The hotkey fields of the Hotkeys panel: input through the capture behavior, the original
// capturing look and the focus after validation messages.
public sealed class HotkeysViewTests
{
    private static (TestComposition App, List<Border> Fields) Show(string? configJson = null)
    {
        var app = new TestComposition(configJson);
        app.Window.Content = new HotkeysView { DataContext = app.HotkeysPanel };
        app.Window.Show();
        app.Window.Activate();
        Dispatcher.UIThread.RunJobs();
        app.Hotkeys.Attach(42);
        var fields = app.Window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("hotkeyField")).ToList();
        Assert.Equal(4, fields.Count);
        return (app, fields);
    }

    private static TextBlock Text(Border field) => field.GetVisualDescendants().OfType<TextBlock>().Single();

    private static Border Caret(Border field) => field.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("hotkeyCaret"));

    private static void Click(Window window, Border field)
    {
        var center = field.TranslatePoint(new Point(field.Bounds.Width / 2, field.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void FieldsShowTheStoredHotkeys()
    {
        var (app, fields) = Show("""{"ShowHideHotkey":624}""");
        using var _ = app;

        Assert.Equal(["CTRL + F1", "None", "None", "None"], fields.Select(field => Text(field).Text));
        Assert.All(fields, field => Assert.True(field.Focusable));
        Assert.All(fields, field => Assert.False(Caret(field).IsVisible));
    }

    [AvaloniaFact]
    public void ClickCapturesAndAKeyPressSetsTheHotkey()
    {
        var (app, fields) = Show();
        using var _ = app;

        Click(app.Window, fields[1]);

        Assert.Contains("capturing", fields[1].Classes);
        Assert.False(Text(fields[1]).IsVisible);
        Assert.True(Caret(fields[1]).IsVisible);
        Assert.True(fields[1].IsFocused);

        app.Window.KeyPress(Key.F2, RawInputModifiers.Control, PhysicalKey.F2, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0x0271, app.Settings.WindowsOverrideHotkey);
        Assert.DoesNotContain("capturing", fields[1].Classes);
        Assert.True(Text(fields[1]).IsVisible);
        Assert.Equal("CTRL + F2", Text(fields[1]).Text);
        Assert.False(Caret(fields[1]).IsVisible);
    }

    [AvaloniaFact]
    public void OnlyFocusByTabStartsACapture()
    {
        var (app, fields) = Show();
        using var _ = app;

        fields[0].Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(app.Capture.CaptureTarget);

        fields[2].Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("capturing", fields[2].Classes);
    }

    [AvaloniaFact]
    public void OtherFieldsLosingTheFocusDoNotEndTheCapture()
    {
        var (app, fields) = Show();
        using var _ = app;
        fields[3].Focus();
        app.Capture.Begin(NvProfileSwitcher.Models.HotkeyField.ShowHide);

        fields[2].Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(NvProfileSwitcher.Models.HotkeyField.ShowHide, app.Capture.CaptureTarget);
    }

    [AvaloniaFact]
    public void LosingTheFocusEndsTheCaptureAndShowsThePreviousValue()
    {
        var (app, fields) = Show("""{"ShowHideHotkey":624}""");
        using var _ = app;
        Click(app.Window, fields[0]);

        fields[3].Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Null(app.Capture.CaptureTarget);
        Assert.DoesNotContain("capturing", fields[0].Classes);
        Assert.Equal("CTRL + F1", Text(fields[0]).Text);
        Assert.True(Text(fields[0]).IsVisible);
    }

    // While the message is open the focus moves away without ending the capture; once it
    // closes, the capturing field is focused again (the other field losing the focus does not
    // end the capture) and keeps consuming keys.
    [AvaloniaFact]
    public void ValidationMessageReturnsTheFocusToTheCapturingField()
    {
        var (app, fields) = Show();
        using var _ = app;
        app.Dialogs.OpenMessage = new TaskCompletionSource();
        Click(app.Window, fields[0]);

        app.Window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
        Dispatcher.UIThread.RunJobs();
        Assert.Single(app.Dialogs.Messages);
        fields[1].Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("capturing", fields[0].Classes);

        app.Dialogs.OpenMessage.SetResult();
        Assert.True(TestComposition.RunUntil(() => fields[0].IsFocused, TimeSpan.FromSeconds(5)));
        Assert.Contains("capturing", fields[0].Classes);
        Assert.DoesNotContain("capturing", fields[1].Classes);

        app.Dialogs.OpenMessage = null;
        app.Window.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0x0074, app.Settings.ShowHideHotkey);
    }
}
