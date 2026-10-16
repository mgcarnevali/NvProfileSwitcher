using Xunit;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Nvidia;
using static NvProfileSwitcher.Core.Tests.FakeDisplayBackend;
using static NvProfileSwitcher.Core.Tests.SwitchingFixture;

namespace NvProfileSwitcher.Core.Tests;

// Selection, editing session, display selection and slider values, over the switching
// fixture's profiles and its two displays (Dell primary, LG second).
public sealed class ProfileEditorTests
{
    private static SwitchingFixture Editing(ProfileListItem? profile = null)
    {
        var app = new SwitchingFixture();
        app.Editor.RefreshDisplays(app.Nvidia.Displays);
        app.Editor.SetSelection(profile ?? app.Windows);
        app.Editor.Begin(profile ?? app.Windows);
        return app;
    }

    private static ProfileListItem WithEditing(SwitchingFixture app, Func<SwitchingFixture, ProfileListItem> profile)
    {
        var target = profile(app);
        app.Editor.SetSelection(target);
        app.Editor.Begin(target);
        return target;
    }

    [Fact]
    public void BeginKeepsThePreferredDisplayOrSelectsThePrimary()
    {
        using var app = Editing();
        Assert.Equal(Dell, app.Editor.SelectedDisplay!.MonitorId);

        app.Editor.Begin(app.Game, Lg);
        Assert.Equal(Lg, app.Editor.SelectedDisplay!.MonitorId);

        app.Editor.Begin(app.Game, "missing");
        Assert.Equal(Dell, app.Editor.SelectedDisplay!.MonitorId);
    }

    [Fact]
    public void WithoutAPrimaryDisplayTheFirstIsSelected()
    {
        using var app = new SwitchingFixture();
        NvidiaDisplay[] displays = [Display(Lg, "LG ULTRAGEAR", 2), Display(Dell, "DELL U2720Q", 3)];
        app.Editor.RefreshDisplays(displays);
        app.Editor.Begin(app.Game, null);

        Assert.Equal(Lg, app.Editor.SelectedDisplay!.MonitorId);
    }

    [Fact]
    public void BeginLoadsTheDraftValuesOfTheSelectedDisplay()
    {
        using var app = Editing();
        app.Editor.Begin(app.Game, Lg);

        Assert.Equal(new ColorValues(100, 100, 1, 60, 355), app.Editor.Values);
        Assert.False(app.Editor.IsDirty);
        Assert.True(app.Editor.HasLoadedValues);
    }

    // A display the profile has no values for shows the Windows values of that display, never
    // the values of another display (deliberate deviation: the original showed the first ones).
    [Fact]
    public void ADisplayWithoutValuesShowsItsWindowsValues()
    {
        using var app = Editing();
        app.Game.DisplayProfiles.RemoveAt(1);
        app.Editor.Begin(app.Game, Lg);

        Assert.Equal(new ColorValues(98, 102, 0.95, 55, 0), app.Editor.Values);
    }

    // The editor never reads the display, which may show a preview or another profile
    // (deliberate deviation: the original read vibrance and hue from it).
    [Fact]
    public void ChoosingADisplayOfAProfileWithoutValuesNeverReadsTheDisplay()
    {
        using var app = Editing();
        app.Backend.Vibrance[Lg] = 63;
        app.Backend.Hue[Lg] = 12;
        app.Game.DisplayProfiles.Clear();
        app.Editor.Begin(app.Game);

        app.Editor.SelectDisplay(app.Editor.Displays[1]);

        Assert.Equal(new ColorValues(98, 102, 0.95, 55, 0), app.Editor.Values);
    }

    // As in the original, the display is read only when the selection changes; beginning on
    // the same display keeps the sliders as they were.
    [Fact]
    public void BeginOnTheSameDisplayDoesNotReadTheDisplay()
    {
        using var app = Editing();
        app.Backend.Vibrance[Dell] = 63;
        app.Game.DisplayProfiles.Clear();

        app.Editor.Begin(app.Game);

        Assert.Equal(new ColorValues(100, 100, 1, 50, 0), app.Editor.Values);
    }

    [Fact]
    public void SliderChangesGoToTheDraftOfTheSelectedDisplay()
    {
        using var app = Editing();
        WithEditing(app, a => a.Game);

        app.Editor.SetCurrentValues(new ColorValues(110.4, 100, 1.2, 80.5, 10.5));

        var values = DisplayCore.FindValues(app.Editor.Session!.Draft, Dell)!;
        Assert.Equal((110.4, 1.2, 80, 10), (values.Brightness, values.Gamma, values.DigitalVibrance, values.Hue));
        Assert.True(app.Editor.IsDirty);
        Assert.True(app.Editor.HasUnsavedColorChanges);
        Assert.Equal(105, DisplayCore.FindValues(app.Game, Dell)!.Brightness);
    }

    [Fact]
    public void SliderChangesAddValuesForADisplayTheDraftDoesNotHave()
    {
        using var app = Editing();
        app.Game.DisplayProfiles.RemoveAt(1);
        WithEditing(app, a => a.Game);
        app.Editor.SelectDisplay(app.Editor.Displays[1]);

        app.Editor.SetCurrentValues(app.Editor.Values with { Brightness = 90 });

        var added = DisplayCore.FindValues(app.Editor.Session!.Draft, Lg)!;
        Assert.Equal(("LG ULTRAGEAR", 90.0), (added.DisplayName, added.Brightness));
    }

    [Fact]
    public void LoadedValuesAreLimitedToTheSliders()
    {
        using var app = Editing();
        DisplayCore.FindValues(app.Game, Dell)!.Gamma = 3.5;

        WithEditing(app, a => a.Game);

        Assert.Equal(2.8, app.Editor.Values.Gamma);
    }

    [Fact]
    public void ResetShowsTheDefaultsInTheDraft()
    {
        using var app = Editing();
        WithEditing(app, a => a.Game);

        app.Editor.ResetValues();

        Assert.Equal(ColorValues.Defaults, app.Editor.Values);
        var draft = app.Editor.Session!.Draft;
        Assert.Equal((100.0, 50), (DisplayCore.FindValues(draft, Dell)!.Brightness, DisplayCore.FindValues(draft, Dell)!.DigitalVibrance));
        Assert.Equal(60, DisplayCore.FindValues(draft, Lg)!.DigitalVibrance);
        Assert.True(app.Editor.IsDirty);
    }

    // Without a display nothing is loaded; resetting the values does not count as a load, and
    // the preview cannot begin.
    [Fact]
    public void ResetDoesNotMarkTheValuesAsLoaded()
    {
        using var app = new SwitchingFixture(initialize: false);
        var empty = new ProfileListItem("Empty") { Enabled = true };
        app.Editor.RefreshDisplays([]);
        app.Editor.Begin(empty);
        Assert.False(app.Editor.HasLoadedValues);

        app.Editor.ResetValues();

        Assert.False(app.Editor.HasLoadedValues);
        Assert.Equal(ColorValues.Defaults, app.Editor.Values);
    }

    // Without any saved values for the display, it shows the NVIDIA defaults.
    [Fact]
    public void WithoutSavedValuesTheDisplayShowsTheDefaults()
    {
        using var app = Editing(null);
        app.Editor.SetCurrentValues(app.Editor.Values with { Vibrance = 77, Hue = 33 });
        app.Windows.DisplayProfiles.RemoveAll(v => v.MonitorId == Lg);
        var empty = new ProfileListItem("Empty") { Enabled = true };
        app.Editor.Begin(empty);

        app.Editor.SelectDisplay(app.Nvidia.Displays.Single(d => d.MonitorId == Lg));

        Assert.Equal(ColorValues.Defaults, app.Editor.Values);
    }

    [Fact]
    public void SelectingAnotherProfileWithoutChangesBeginsIt()
    {
        using var app = Editing();

        _ = app.Editing.RequestSelectionAsync(app.Game);

        Assert.Same(app.Game, app.Editor.SelectedProfile);
        Assert.Same(app.Game, app.Editor.Session!.Source);
        Assert.Empty(app.Dialogs.DiscardRequests);
    }

    [Fact]
    public async Task KeepEditingKeepsTheEditedProfileSelected()
    {
        using var app = Editing();
        WithEditing(app, a => a.Game);
        app.Editor.SetName("Changed");
        app.Dialogs.DiscardAnswer = false;

        await app.Editing.RequestSelectionAsync(app.Notepad);

        Assert.Equal([false], app.Dialogs.DiscardRequests);
        Assert.Same(app.Game, app.Editor.SelectedProfile);
        Assert.Equal("Changed", app.Editor.Session!.Draft.Name);
    }

    [Fact]
    public async Task DiscardBeginsTheRequestedProfile()
    {
        using var app = Editing();
        WithEditing(app, a => a.Game);
        app.Editor.SetName("Changed");

        await app.Editing.RequestSelectionAsync(app.Notepad);

        Assert.Same(app.Notepad, app.Editor.SelectedProfile);
        Assert.Same(app.Notepad, app.Editor.Session!.Source);
        Assert.Equal("Game", app.Game.Name);
    }

    // While confirming, the list is back on the edited profile.
    [Fact]
    public void TheSelectionReturnsToTheEditedProfileWhileConfirming()
    {
        using var app = Editing();
        WithEditing(app, a => a.Game);
        app.Editor.SetName("Changed");
        app.Dialogs.PendingDiscard = new TaskCompletionSource<bool>();

        var selection = app.Editing.RequestSelectionAsync(app.Notepad);

        Assert.Same(app.Game, app.Editor.SelectedProfile);
        app.Dialogs.PendingDiscard.SetResult(true);
        Assert.True(selection.IsCompleted);
        Assert.Same(app.Notepad, app.Editor.SelectedProfile);
    }

    [Fact]
    public async Task DiscardingTheUnsavedProfileRemovesIt()
    {
        using var app = Editing();
        var added = new ProfileListItem("New Profile") { Enabled = true };
        app.Session.Profiles.AddUnsaved(added);
        WithEditing(app, _ => added);
        Assert.True(app.Editor.IsDirty);

        await app.Editing.RequestSelectionAsync(app.Game);

        Assert.Equal([true], app.Dialogs.DiscardRequests);
        Assert.DoesNotContain(added, app.Session.Profiles.Profiles);
        Assert.Null(app.Session.Profiles.UnsavedNewProfile);
        Assert.Same(app.Game, app.Editor.Session!.Source);
    }

    [Fact]
    public void DiscardWithoutAnUnsavedProfileKeepsTheList()
    {
        using var app = Editing();
        WithEditing(app, a => a.Game);

        app.Editor.Discard(removeUnsavedProfile: true);

        Assert.Null(app.Editor.Session);
        Assert.Equal(4, app.Session.Profiles.Profiles.Count);
        Assert.Same(app.Game, app.Editor.SelectedProfile);
    }

    [Fact]
    public void RefreshingDisplaysKeepsTheSelectedMonitor()
    {
        using var app = Editing();
        app.Editor.Begin(app.Game, Lg);
        app.Editor.SetCurrentValues(app.Editor.Values with { Brightness = 90 });

        app.Editor.RefreshDisplays([Display(Dell, "DELL U2720Q"), Display(Lg, "LG ULTRAGEAR", 2)]);

        Assert.Equal(Lg, app.Editor.SelectedDisplay!.MonitorId);
        Assert.Equal(90, app.Editor.Values.Brightness);
    }

    [Fact]
    public void RefreshingWithoutTheSelectedMonitorSelectsTheFirst()
    {
        using var app = Editing();
        app.Editor.Begin(app.Game, Lg);

        app.Editor.RefreshDisplays([Display(Dell, "DELL U2720Q")]);

        Assert.Equal(Dell, app.Editor.SelectedDisplay!.MonitorId);
        Assert.Equal(80, app.Editor.Values.Vibrance);
    }

    [Fact]
    public void TheProfileHotkeyIsTheDraftsForApplicationProfiles()
    {
        using var app = Editing();
        app.Game.Hotkey = 0x0270;
        Assert.Equal(0, app.Editor.ProfileHotkey);

        app.Editor.SetDraftHotkey(0x0271);
        Assert.Equal(0, app.Editor.ProfileHotkey);

        WithEditing(app, a => a.Game);
        Assert.Equal(0x0270, app.Editor.ProfileHotkey);

        app.Editor.SetDraftHotkey(0x0271);
        Assert.Equal(0x0271, app.Editor.Session!.Draft.Hotkey);
        Assert.Equal(0x0270, app.Game.Hotkey);
        Assert.True(app.Editor.IsDirty);
    }
}
