using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;
using Xunit;

namespace NvProfileSwitcher.Core.Tests;

// Registration, release and dispatch of the saved hotkeys, as in the original.
public sealed class HotkeyServiceTests
{
    private const string Unavailable = "One or more saved hotkeys could not be enabled.\n\nChoose a supported, unused combination in Hotkeys.";

    private readonly FakeHotkeyRegistrar registrar = new();
    private readonly AppSettings settings = new();
    private readonly ProfileStore profiles = new(new SwitchingState());
    private readonly FakeDialogService dialogs = new();

    public HotkeyServiceTests()
    {
        profiles.Load([new ProfileListItem("Windows", isWindowsProfile: true)]);
    }

    private HotkeyService Create() => new(registrar, settings, profiles, dialogs);

    private void AddProfile(string name, int hotkey, bool enabled = true) =>
        profiles.Load([.. profiles.Profiles, new ProfileListItem(name) { Hotkey = hotkey, Enabled = enabled }]);

    [Fact]
    public void AttachRegistersOnlyTheFirstTimeAWindowHandleIsAvailable()
    {
        settings.ShowHideHotkey = 0x0270;
        var service = Create();

        service.Attach(0);
        Assert.Empty(registrar.Log);

        service.Attach(42);
        service.Attach(43);
        Assert.Equal(["register 3001 4002 70"], registrar.Log);
    }

    [Fact]
    public void RegistersGlobalAndEnabledProfileHotkeysWithTheirModifiers()
    {
        settings.ShowHideHotkey = 0x0270;
        settings.WindowsOverrideHotkey = 0x0441;
        settings.ResumeAutomaticHotkey = 0x0342;
        settings.GlobalSwitchingHotkey = 0x0071;
        AddProfile("Game", 0x0543);
        AddProfile("Disabled", 0x0244, enabled: false);
        AddProfile("No hotkey", 0);
        AddProfile("Editor", 0x0A2E);

        Create().Attach(42);

        Assert.Equal(
        [
            "register 3001 4002 70",
            "register 3002 4001 41",
            "register 3003 4006 42",
            "register 3004 4000 71",
            "register 4000 4005 43",
            "register 4003 4002 2E",
        ], registrar.Log);
        Assert.Empty(dialogs.Messages);
    }

    [Fact]
    public void RegisteringTwiceDoesNothingUntilUnregistered()
    {
        settings.ShowHideHotkey = 0x0270;
        var service = Create();
        service.Attach(42);

        service.RegisterAll(showAggregateMessage: true);
        Assert.Single(registrar.Log);

        service.UnregisterAll();
        service.RegisterAll(showAggregateMessage: true);
        Assert.Equal(2, registrar.Log.Count(entry => entry == "register 3001 4002 70"));
    }

    // Releasing frees exactly what was registered, and the test registration in case it was left.
    [Fact]
    public void UnregistersWhatWasRegisteredAndTheTestId()
    {
        settings.ShowHideHotkey = 0x0270;
        AddProfile("Game", 0x0347);
        AddProfile("Disabled", 0x0348, enabled: false);
        AddProfile("NoHotkey", 0);
        var service = Create();
        service.UnregisterAll();
        Assert.Empty(registrar.Log);

        service.Attach(42);
        registrar.Log.Clear();
        service.UnregisterAll();

        Assert.Equal(["unregister 3001", "unregister 4000", "unregister 3005"], registrar.Log);
        Assert.Empty(registrar.Active);
    }

    // A failed registration holds nothing, so nothing is released for it.
    [Fact]
    public void AFailedRegistrationIsNotReleased()
    {
        AddProfile("Game", 0x0347);
        registrar.FailingIds.Add(4000);
        var service = Create();
        service.Attach(42);
        registrar.Log.Clear();

        service.UnregisterAll();

        Assert.Equal(["unregister 3005"], registrar.Log);
    }

    // Refresh registers the current state again: a profile disabled since is released.
    [Fact]
    public void RefreshRegistersTheCurrentState()
    {
        AddProfile("Game", 0x0347);
        AddProfile("Notepad", 0x034E);
        var service = Create();
        service.Attach(42);

        profiles.Profiles[1].Enabled = false;
        service.Refresh(showAggregateMessage: false);

        Assert.Equal([0x034E], registrar.ActiveHotkeys());
    }

    [Fact]
    public void FailedRegistrationShowsTheAggregateMessageOnce()
    {
        settings.ShowHideHotkey = 0x0270;
        settings.WindowsOverrideHotkey = 0x0441;
        registrar.FailingIds.Add(3001);

        Create().Attach(42);

        Assert.Contains("register 3002 4001 41", registrar.Log);
        Assert.Equal([("NvProfileSwitcher", Unavailable)], dialogs.Messages);
    }

    [Fact]
    public void CtrlAltHotkeysAreNeverRegistered()
    {
        settings.ShowHideHotkey = 0x0641;

        Create().Attach(42);

        Assert.Empty(registrar.Log);
        Assert.Equal([("NvProfileSwitcher", Unavailable)], dialogs.Messages);
    }

    [Fact]
    public void AggregateMessageIsSkippedWhenRequestedOrWhileAMessageIsOpen()
    {
        settings.ShowHideHotkey = 0x0270;
        registrar.FailingIds.Add(3001);
        var service = Create();
        dialogs.OpenMessage = new TaskCompletionSource();
        service.Attach(42);
        Assert.True(service.IsMessageDialogOpen);

        service.UnregisterAll();
        service.RegisterAll(showAggregateMessage: true);
        service.UnregisterAll();
        service.RegisterAll(showAggregateMessage: false);

        Assert.Single(dialogs.Messages);
        dialogs.OpenMessage.SetResult();
        Assert.False(service.IsMessageDialogOpen);
    }

    [Fact]
    public void ProfileIdsStopAtTheLastHotkeyId()
    {
        profiles.Load([.. profiles.Profiles, .. Enumerable.Range(0, 0xBFFF - 4000 + 2).Select(i => new ProfileListItem($"P{i}") { Enabled = true })]);

        Create().Attach(42);

        Assert.Equal([("NvProfileSwitcher", Unavailable)], dialogs.Messages);
    }

    [Fact]
    public void TestRegistrationUsesTheTestIdAndReleasesIt()
    {
        var service = Create();
        service.Attach(42);

        Assert.True(service.TryTest(0x0241));
        registrar.FailingIds.Add(3005);
        Assert.False(service.TryTest(0x0242));

        Assert.Equal(["register 3005 4002 41", "unregister 3005", "register 3005 4002 42"], registrar.Log);
    }

    [Fact]
    public void TestRegistrationFailsWithoutAWindowHandle() =>
        Assert.False(Create().TryTest(0x0241));

    [Theory]
    [InlineData(3001, "ShowHide", -1)]
    [InlineData(3002, "WindowsOverride", -1)]
    [InlineData(3003, "ResumeAutomatic", -1)]
    [InlineData(3004, "GlobalSwitching", -1)]
    [InlineData(4000, "Profile", 0)]
    [InlineData(4001, "Profile", 1)]
    public void HotkeyMessagesTriggerTheirAction(int id, string action, int index)
    {
        AddProfile("Game", 0x0347);
        AddProfile("Editor", 0x0345);
        var service = Create();
        service.Attach(42);
        var triggered = new List<(HotkeyAction, int)>();
        service.Triggered += (a, i) => triggered.Add((a, i));

        Assert.True(service.HandleMessage(0x0312, id));

        Assert.Equal([(Enum.Parse<HotkeyAction>(action), index)], triggered);
    }

    // A profile hotkey triggers its profile wherever the profile is in the list now; a removed
    // profile's hotkey is still ours until registered again, but triggers nothing.
    [Fact]
    public void AProfileHotkeyTriggersItsProfileWhereverItIsNow()
    {
        AddProfile("Game", 0x0347);
        AddProfile("Editor", 0x0345);
        var service = Create();
        service.Attach(42);
        var triggered = new List<int>();
        service.Triggered += (_, i) => triggered.Add(i);
        var editorId = registrar.IdOf(0x0345)!.Value;
        var gameId = registrar.IdOf(0x0347)!.Value;

        profiles.Move(2, 1);
        Assert.True(service.HandleMessage(0x0312, editorId));
        profiles.Remove(profiles.Profiles[2]);
        Assert.True(service.HandleMessage(0x0312, gameId));

        Assert.Equal([0], triggered);
    }

    [Fact]
    public void OtherMessagesAndIdsAreNotHandledButUnknownProfileIdsAre()
    {
        AddProfile("Game", 0);
        var service = Create();
        var triggered = 0;
        service.Triggered += (_, _) => triggered++;

        Assert.False(service.HandleMessage(0x007E, 3001));
        Assert.False(service.HandleMessage(0x0312, 3005));
        Assert.False(service.HandleMessage(0x0312, 3999));
        Assert.True(service.HandleMessage(0x0312, 4001));
        Assert.Equal(0, triggered);
    }

    [Fact]
    public void FormatsWithTheRegistrarKeyNames()
    {
        var service = Create();

        Assert.Equal("None", service.Format(0));
        Assert.Equal("CTRL + SHIFT + A", service.Format(0x0341));
    }
}
