using Xunit;
using NvProfileSwitcher.Services.Editing;

namespace NvProfileSwitcher.Core.Tests;

public sealed class ProfileEditorSessionTests
{
    private static ProfileListItem Game()
    {
        var game = new ProfileListItem("Game") { Enabled = true, Hotkey = 0x0270 };
        game.SetExecutables(["game.exe"]);
        game.DisplayProfiles.Add(new ProfileDisplaySettings { MonitorId = "DEL4321", Brightness = 105, DigitalVibrance = 70 });
        return game;
    }

    [Fact]
    public void ANewSessionHasNoChanges()
    {
        var session = new ProfileEditorSession(Game());

        Assert.NotSame(session.Source, session.Draft);
        Assert.False(session.HasChanges);
        Assert.False(session.HasUnsavedColorChanges);
    }

    [Fact]
    public void NameExecutableAndHotkeyChangesAreNotColorChanges()
    {
        var session = new ProfileEditorSession(Game());
        session.Draft.Name = "Game 2";
        session.Draft.AddExecutable("game2.exe");
        session.Draft.Hotkey = 0x0271;

        Assert.True(session.HasChanges);
        Assert.False(session.HasUnsavedColorChanges);
    }

    [Fact]
    public void ColorChangesAreChanges()
    {
        var session = new ProfileEditorSession(Game());
        session.Draft.DisplayProfiles[0].DigitalVibrance = 71;

        Assert.True(session.HasChanges);
        Assert.True(session.HasUnsavedColorChanges);
    }

    // Enabling or disabling is a list action, not an edit, as in the original comparison.
    [Fact]
    public void TheEnabledStateIsNotAChange()
    {
        var session = new ProfileEditorSession(Game());
        session.Source.Enabled = false;

        Assert.False(session.HasChanges);
    }

    // Values for a display only the draft has are a change but not a color change, as in the original.
    [Fact]
    public void ValuesForANewDisplayAreNotAColorChange()
    {
        var session = new ProfileEditorSession(Game());
        session.Draft.DisplayProfiles.Add(new ProfileDisplaySettings { MonitorId = "GSM5B7F", Brightness = 90 });

        Assert.True(session.HasChanges);
        Assert.False(session.HasUnsavedColorChanges);
    }
}
