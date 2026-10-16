using Xunit;
using NvProfileSwitcher.Models;
using static NvProfileSwitcher.Core.Tests.SwitchingFixture;

namespace NvProfileSwitcher.Core.Tests;

// The live preview and its relation to switching, with the sequence of color applications
// (the profile each one came from, or "preview").
public sealed class LivePreviewControllerTests
{
    private static readonly ColorValues Previewed = new(90, 95, 1.3, 40, 30);

    // Editing the Game profile on the Dell display, with the window active and shown.
    private static SwitchingFixture EditingGame()
    {
        var app = new SwitchingFixture { WindowActive = true };
        app.Editor.RefreshDisplays(app.Nvidia.Displays);
        app.Editor.SetSelection(app.Game);
        app.Editor.Begin(app.Game);
        app.Backend.Applied.Clear();
        return app;
    }

    // What a slider change does: the draft takes the values, then the preview begins and applies.
    private static void MoveSlider(SwitchingFixture app, ColorValues values)
    {
        app.Editor.SetCurrentValues(values);
        app.Preview.Preview();
    }

    private static List<string> Applications(SwitchingFixture app) =>
        app.Backend.Applied.Select(applied =>
            applied.MonitorId == Dell && applied.Brightness == Previewed.Brightness && applied.Vibrance == (int)Previewed.Vibrance
                ? "preview"
                : app.AppliedProfiles()[app.Backend.Applied.IndexOf(applied)]).ToList();

    [Fact]
    public void MovingASliderPreviewsTheSelectedDisplay()
    {
        using var app = EditingGame();

        MoveSlider(app, Previewed);

        Assert.True(app.Preview.IsActive);
        var applied = Assert.Single(app.Backend.Applied);
        Assert.Equal((Dell, 40, 90.0, 95.0, 1.3, 30), (applied.MonitorId, applied.Vibrance, applied.Brightness, applied.Contrast, applied.Gamma, applied.Hue));
    }

    [Fact]
    public void DiscardingReappliesTheRealColorsOnce()
    {
        using var app = EditingGame();
        MoveSlider(app, Previewed);

        app.Preview.Discard();
        app.Preview.Discard();

        Assert.False(app.Preview.IsActive);
        Assert.Equal(["preview", "Windows", "Windows"], Applications(app));
    }

    [Fact]
    public void AcceptingEndsThePreviewWithoutReapplying()
    {
        using var app = EditingGame();
        MoveSlider(app, Previewed);

        // Accepting gives the display to the base profile, which Save then makes the active
        // one in the same operation; a later discard has nothing to give back.
        app.Preview.Accept();
        app.Preview.Discard();

        Assert.False(app.Preview.IsActive);
        Assert.Equal(["preview", "Windows"], Applications(app));
    }

    [Fact]
    public void NothingIsPreviewedWhileGlobalSwitchingIsPaused()
    {
        using var app = EditingGame();
        app.Session.Switching.GlobalPaused = true;

        MoveSlider(app, Previewed);

        Assert.True(app.Preview.IsActive);
        Assert.Empty(app.Backend.Applied);
    }

    [Fact]
    public void ResumingNeedsAShownWindowAndUnsavedColorChanges()
    {
        using var app = EditingGame();
        app.Preview.Resume();
        Assert.Empty(app.Backend.Applied);

        app.Editor.SetCurrentValues(Previewed);
        app.WindowShown = false;
        app.Preview.Resume();
        Assert.Empty(app.Backend.Applied);

        app.WindowShown = true;
        app.Preview.Resume();
        Assert.True(app.Preview.IsActive);
        Assert.Equal(["preview"], Applications(app));
    }

    // Changing the edited profile suspends the preview, as the original did before beginning.
    [Fact]
    public void BeginningAnotherProfileDiscardsThePreview()
    {
        using var app = EditingGame();
        MoveSlider(app, Previewed);

        app.Editor.Begin(app.Notepad);

        Assert.False(app.Preview.IsActive);
        Assert.Equal(["preview", "Windows", "Windows"], Applications(app));
    }

    // Choosing another display discards the preview and resumes it on the new display.
    [Fact]
    public void ChoosingAnotherDisplayResumesThePreviewThere()
    {
        using var app = EditingGame();
        MoveSlider(app, Previewed);

        app.Editor.SelectDisplay(app.Editor.Displays[1]);

        Assert.True(app.Preview.IsActive);
        Assert.Equal(["preview", "Windows", "Windows"], Applications(app).Take(3));
        var resumed = Assert.Single(app.Backend.Applied.Skip(3));
        Assert.Equal((Lg, 60), (resumed.MonitorId, resumed.Vibrance));
    }

    // While the window is active with unsaved color changes, the preview owns the display.
    [Fact]
    public void SwitchingWaitsWhileThePreviewOwnsTheDisplay()
    {
        using var app = EditingGame();
        MoveSlider(app, Previewed);
        app.Foreground = "game.exe";

        app.Active.Check();

        Assert.True(app.Preview.IsActive);
        Assert.Equal(["preview"], Applications(app));
    }

    // Once the window is not active, a switch first discards the preview, as in the original:
    // the real colors are already the foreground application's, which is then applied again.
    [Fact]
    public void ASwitchDiscardsThePreviewFirst()
    {
        using var app = EditingGame();
        MoveSlider(app, Previewed);
        app.WindowActive = false;
        app.Foreground = "game.exe";

        app.Active.Check();

        Assert.False(app.Preview.IsActive);
        // One write per display, the previewed one included (deliberate deviation: the
        // original reapplied the real colors to every display before applying the switch).
        Assert.Equal(["preview", "Game", "Game"], Applications(app));
    }

    [Fact]
    public void EnablingGlobalSwitchingAppliesTheActivePreviewAgain()
    {
        using var app = EditingGame();
        MoveSlider(app, Previewed);
        app.Automatic.SetGlobalEnabled(false);
        app.Backend.Applied.Clear();

        app.Automatic.SetGlobalEnabled(true);

        Assert.Equal(["preview"], Applications(app));
    }

    [Fact]
    public void EnablingGlobalSwitchingWithoutAPreviewAppliesTheRealColors()
    {
        using var app = EditingGame();
        app.Automatic.SetGlobalEnabled(false);
        app.Backend.Applied.Clear();

        app.Automatic.SetGlobalEnabled(true);

        Assert.Equal(["Windows", "Windows"], Applications(app));
    }

    [Fact]
    public void OverridesDiscardThePreview()
    {
        using var app = EditingGame();

        MoveSlider(app, Previewed);
        app.Automatic.ToggleWindowsOverride();
        Assert.False(app.Preview.IsActive);

        MoveSlider(app, Previewed);
        app.Automatic.ResumeAutomatic();
        Assert.False(app.Preview.IsActive);

        MoveSlider(app, Previewed);
        app.Automatic.ToggleProfileOverride(0);
        Assert.False(app.Preview.IsActive);
    }

    // As in the original, an override that cannot apply (a disabled profile) leaves the preview.
    [Fact]
    public void AnOverrideThatDoesNotApplyKeepsThePreview()
    {
        using var app = EditingGame();
        MoveSlider(app, Previewed);

        app.Automatic.ToggleProfileOverride(1);

        Assert.True(app.Preview.IsActive);
    }
}
