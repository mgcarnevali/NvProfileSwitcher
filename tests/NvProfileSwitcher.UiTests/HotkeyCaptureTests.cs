using Avalonia.Headless.XUnit;
using Avalonia.Input;
using NvProfileSwitcher.Models;

namespace NvProfileSwitcher.UiTests;

// Capturing a global hotkey: the original validations and messages, plus the approved
// deviations (single end of capture, aggregate message only after a real capture, focus
// after every validation message, silent save failures).
public sealed class HotkeyCaptureTests
{
    private const string Title = "NvProfileSwitcher";
    private const string Unavailable = "One or more saved hotkeys could not be enabled.\n\nChoose a supported, unused combination in Hotkeys.";
    private const string NeedsModifier = "Hotkeys must use Ctrl or Alt with another key. Function keys can be used alone.\n\nUse Ctrl + Shift, Alt + Shift, or a function key instead.";
    private const string CtrlAlt = "Ctrl + Alt hotkeys are not supported because Windows may treat Right Alt (AltGr) as Ctrl + Alt.\n\nUse Ctrl + Shift, Alt + Shift, or a function key instead.";
    private const string UsedByAnotherApplication = "That hotkey is already in use by another application.";

    // A shown, active window with a window handle attached to the hotkey service.
    private static TestComposition Create(string? configJson = null)
    {
        var app = new TestComposition(configJson);
        app.Window.Show();
        app.Window.Activate();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(app.Host.IsActive);
        app.Hotkeys.Attach(42);
        return app;
    }

    private static void Capture(TestComposition app, HotkeyField field = HotkeyField.ShowHide)
    {
        app.Capture.Begin(field);
        Assert.Equal(field, app.Capture.CaptureTarget);
    }

    private static string Saved(TestComposition app) => File.Exists(app.ConfigPath) ? File.ReadAllText(app.ConfigPath) : string.Empty;

    [AvaloniaFact]
    public void CaptureStartsOnlyWhileTheWindowIsActive()
    {
        using var app = new TestComposition();

        app.Capture.Begin(HotkeyField.ShowHide);

        Assert.Null(app.Capture.CaptureTarget);
    }

    [AvaloniaFact]
    public void CaptureUnregistersEveryHotkeyAndHidesTheTextBehindTheCaret()
    {
        using var app = Create("""{"ShowHideHotkey":624}""");
        var field = app.HotkeysPanel.ShowHide;
        Assert.Equal("CTRL + F1", field.Text);
        app.Registrar.Log.Clear();

        Capture(app);

        Assert.Equal("unregister 3001", app.Registrar.Log[0]);
        Assert.DoesNotContain(app.Registrar.Log, entry => entry.StartsWith("register", StringComparison.Ordinal));
        Assert.True(field.IsCapturing);
        Assert.True(field.IsCaretVisible);
        Assert.False(app.HotkeysPanel.WindowsOverride.IsCapturing);
    }

    [AvaloniaFact]
    public void OnlyTheCapturingFieldConsumesKeysAndModifiersAloneAreIgnored()
    {
        using var app = Create();
        Capture(app);

        Assert.False(app.Capture.KeyPressed(HotkeyField.WindowsOverride, Key.F2, KeyModifiers.None));
        Assert.True(app.Capture.KeyPressed(HotkeyField.ShowHide, Key.LeftCtrl, KeyModifiers.Control));
        Assert.True(app.Capture.KeyPressed(HotkeyField.ShowHide, Key.CapsLock, KeyModifiers.None));

        Assert.Equal(HotkeyField.ShowHide, app.Capture.CaptureTarget);
        Assert.Empty(app.Dialogs.Messages);
    }

    [AvaloniaFact]
    public void AcceptedHotkeyIsSavedAndRegisteredAgain()
    {
        using var app = Create();
        Capture(app);

        Assert.True(app.Capture.KeyPressed(HotkeyField.ShowHide, Key.F1, KeyModifiers.Control));

        Assert.Null(app.Capture.CaptureTarget);
        Assert.Equal(0x0270, app.Settings.ShowHideHotkey);
        Assert.Contains("\"ShowHideHotkey\": 624", Saved(app));
        Assert.Equal(["register 3001 4002 70"], app.Registrar.Registered());
        Assert.Equal("CTRL + F1", app.HotkeysPanel.ShowHide.Text);
        Assert.False(app.HotkeysPanel.ShowHide.IsCaretVisible);
    }

    [AvaloniaFact]
    public void EscapeClearsTheHotkeyAndShowsNone()
    {
        using var app = Create("""{"ShowHideHotkey":624}""");
        Capture(app);

        app.Capture.KeyPressed(HotkeyField.ShowHide, Key.Escape, KeyModifiers.None);

        Assert.Null(app.Capture.CaptureTarget);
        Assert.Equal(0, app.Settings.ShowHideHotkey);
        Assert.Contains("\"ShowHideHotkey\": 0", Saved(app));
        Assert.Equal("None", app.HotkeysPanel.ShowHide.Text);
    }

    [AvaloniaFact]
    public void SameCombinationEndsTheCaptureWithoutSaving()
    {
        var json = """{"ShowHideHotkey":624}""";
        using var app = Create(json);
        Capture(app);

        app.Capture.KeyPressed(HotkeyField.ShowHide, Key.F1, KeyModifiers.Control);

        Assert.Null(app.Capture.CaptureTarget);
        Assert.Equal(json, Saved(app));
        Assert.Equal(["register 3001 4002 70"], app.Registrar.Registered());
    }

    // The original checks in order, each with its message. The capture continues with every
    // hotkey still unregistered and the field gets the focus back.
    [AvaloniaTheory]
    [InlineData("A", "None", NeedsModifier)]
    [InlineData("A", "Control, Alt", CtrlAlt)]
    [InlineData("A", "Control, Shift", "That hotkey is already assigned to Windows Override.\n\nChoose a different combination.")]
    [InlineData("B", "Control", "That hotkey is already assigned to Game.\n\nChoose a different combination.")]
    [InlineData("C", "Control", UsedByAnotherApplication)]
    public void FailedValidationShowsItsMessageAndKeepsCapturing(string key, string modifiers, string message)
    {
        using var app = Create("""{"WindowsOverrideHotkey":833,"Application Profiles":[{"Name":"Game","Hotkey":578,"Enabled":true}]}""");
        app.Registrar.FailingIds.Add(3005);
        var focusRequests = 0;
        app.HotkeysPanel.ShowHide.FocusRequested += () => focusRequests++;
        Capture(app);
        app.Registrar.Log.Clear();

        app.Capture.KeyPressed(HotkeyField.ShowHide, Enum.Parse<Key>(key), Enum.Parse<KeyModifiers>(modifiers));

        Assert.Equal([(Title, message)], app.Dialogs.Messages);
        Assert.Equal(HotkeyField.ShowHide, app.Capture.CaptureTarget);
        Assert.Equal(0, app.Settings.ShowHideHotkey);
        Assert.DoesNotContain(app.Registrar.Log, entry => entry.StartsWith("register 300", StringComparison.Ordinal) && !entry.StartsWith("register 3005", StringComparison.Ordinal));
        Assert.Equal(1, focusRequests);
        Assert.Equal("None", app.HotkeysPanel.ShowHide.Text);
    }

    [AvaloniaFact]
    public void FieldsDoNotConflictWithThemselves()
    {
        using var app = Create("""{"ShowHideHotkey":624}""");
        Capture(app, HotkeyField.WindowsOverride);

        app.Capture.KeyPressed(HotkeyField.WindowsOverride, Key.F1, KeyModifiers.Control);

        Assert.Equal([(Title, "That hotkey is already assigned to Show / hide window.\n\nChoose a different combination.")], app.Dialogs.Messages);
    }

    [AvaloniaFact]
    public void EndingWithoutANewHotkeyShowsThePreviousValueAgain()
    {
        using var app = Create("""{"ShowHideHotkey":624}""");
        Capture(app);
        app.Capture.KeyPressed(HotkeyField.ShowHide, Key.A, KeyModifiers.None);
        Assert.False(app.HotkeysPanel.ShowHide.Text == "None");

        app.Capture.End();

        Assert.False(app.HotkeysPanel.ShowHide.IsCapturing);
        Assert.Equal("CTRL + F1", app.HotkeysPanel.ShowHide.Text);
        Assert.Equal(624, app.Settings.ShowHideHotkey);

        Capture(app, HotkeyField.WindowsOverride);
        app.Capture.End();
        Assert.Equal("None", app.HotkeysPanel.WindowsOverride.Text);
    }

    // Deliberate deviation: a failed save no longer closes the application.
    [AvaloniaFact]
    public void FailedSaveKeepsTheHotkeyAndEndsTheCapture()
    {
        using var app = Create();
        Directory.CreateDirectory(app.ConfigPath + ".tmp");
        Capture(app);

        app.Capture.KeyPressed(HotkeyField.ShowHide, Key.F1, KeyModifiers.Control);
        Assert.Equal(0x0270, app.Settings.ShowHideHotkey);
        Assert.Null(app.Capture.CaptureTarget);
        Assert.Equal(["register 3001 4002 70"], app.Registrar.Registered());

        Capture(app);
        app.Capture.KeyPressed(HotkeyField.ShowHide, Key.Escape, KeyModifiers.None);
        Assert.Equal(0, app.Settings.ShowHideHotkey);
        Assert.Null(app.Capture.CaptureTarget);
    }

    // Deliberate deviation: ending without a capture registers the hotkeys again silently;
    // only the startup and the end of a real capture report unavailable hotkeys.
    [AvaloniaFact]
    public void AggregateMessageIsShownAtStartupAndAfterARealCaptureOnly()
    {
        var app = new TestComposition("""{"ShowHideHotkey":624}""");
        using var _ = app;
        app.Window.Show();
        app.Window.Activate();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        app.Registrar.FailingIds.Add(3001);
        app.Hotkeys.Attach(42);
        Assert.Equal([(Title, Unavailable)], app.Dialogs.Messages);

        app.Capture.EndUnlessMessageOpen();
        app.Capture.End();
        app.Capture.EndUnlessMessageOpen();
        Assert.Single(app.Dialogs.Messages);
        Assert.Equal(4, app.Registrar.Log.Count(entry => entry == "register 3001 4002 70"));

        app.Registrar.FailingIds.Clear();
        app.Capture.End();
        Assert.Single(app.Dialogs.Messages);

        app.Registrar.FailingIds.Add(3001);
        Capture(app, HotkeyField.WindowsOverride);
        app.Capture.End();
        Assert.Equal([(Title, Unavailable), (Title, Unavailable)], app.Dialogs.Messages);
    }

    [AvaloniaFact]
    public void OpenMessageKeepsTheCaptureExceptForAnExplicitEnd()
    {
        using var app = Create();
        app.Dialogs.OpenMessage = new TaskCompletionSource();
        Capture(app);
        app.Capture.KeyPressed(HotkeyField.ShowHide, Key.A, KeyModifiers.None);
        Assert.True(app.Hotkeys.IsMessageDialogOpen);

        app.Capture.EndUnlessMessageOpen();
        Assert.Equal(HotkeyField.ShowHide, app.Capture.CaptureTarget);

        app.Capture.End();
        Assert.Null(app.Capture.CaptureTarget);

        var focusRequests = 0;
        app.HotkeysPanel.ShowHide.FocusRequested += () => focusRequests++;
        app.Dialogs.OpenMessage.SetResult();
        Assert.True(TestComposition.RunUntil(() => !app.Hotkeys.IsMessageDialogOpen, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, focusRequests);
    }

    [AvaloniaFact]
    public void EndingTwiceIsHarmless()
    {
        using var app = Create();
        Capture(app);

        app.Capture.End();
        app.Capture.End();

        Assert.Null(app.Capture.CaptureTarget);
        Assert.False(app.Capture.IsCaretVisible);
        Assert.Empty(app.Dialogs.Messages);
    }

    [AvaloniaFact]
    public void CaretBlinksEvery530Milliseconds()
    {
        using var app = Create();
        Capture(app);
        var changes = new List<(bool Visible, DateTime At)>();
        app.HotkeysPanel.ShowHide.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "IsCaretVisible")
                changes.Add((app.HotkeysPanel.ShowHide.IsCaretVisible, DateTime.UtcNow));
        };
        var start = DateTime.UtcNow;

        Assert.True(TestComposition.RunUntil(() => changes.Count >= 2, TimeSpan.FromSeconds(5)));

        Assert.Equal([false, true], changes.Take(2).Select(change => change.Visible));
        Assert.InRange((changes[0].At - start).TotalMilliseconds, 400, 1000);
    }
}
