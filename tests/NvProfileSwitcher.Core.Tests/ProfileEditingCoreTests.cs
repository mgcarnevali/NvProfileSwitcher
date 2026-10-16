using Xunit;

namespace NvProfileSwitcher.Core.Tests;

public sealed class ProfileEditingCoreTests
{
    private static ProfileListItem CompleteProfile()
    {
        var profile = new ProfileListItem("WarDog")
        {
            Enabled = true,
            Hotkey = 0x0257,
            DisplayProfiles =
            {
                new ProfileDisplaySettings
                {
                    Name = "DISPLAY1", DisplayName = "Primary", MonitorId = "MONITOR-1",
                    Brightness = 101, Contrast = 102, Gamma = 1.25, DigitalVibrance = 70, Hue = 3
                },
                new ProfileDisplaySettings
                {
                    Name = "DISPLAY2", DisplayName = "Secondary", MonitorId = "MONITOR-2",
                    Brightness = 91, Contrast = 98, Gamma = 2.42, DigitalVibrance = 80, Hue = -2
                }
            }
        };
        profile.SetExecutables([@"C:\Games\WarDog.exe"]);
        return profile;
    }

    [Fact]
    public void CloneProfileCreatesIndependentDeepCopy()
    {
        var source = CompleteProfile();
        var draft = ProfileEditingCore.CloneProfile(source);

        Assert.True(ProfileEditingCore.ProfilesEqual(source, draft));
        Assert.NotSame(source, draft);
        Assert.Equal(source.Id, draft.Id);
        Assert.NotSame(source.DisplayProfiles[0], draft.DisplayProfiles[0]);
        Assert.NotSame(source.DisplayProfiles[1], draft.DisplayProfiles[1]);

        draft.Name = "Edited";
        draft.DisplayProfiles[0].DigitalVibrance = 0;
        draft.DisplayProfiles[1].Gamma = 1.1;

        Assert.Equal("WarDog", source.Name);
        Assert.Equal(70, source.DisplayProfiles[0].DigitalVibrance);
        Assert.Equal(2.42, source.DisplayProfiles[1].Gamma);
        Assert.False(ProfileEditingCore.ProfilesEqual(source, draft));
    }

    [Fact]
    public void CopyProfilePreservesDestinationIdentity()
    {
        var source = CompleteProfile();
        var destination = CompleteProfile();
        var destinationId = destination.Id;

        ProfileEditingCore.CopyProfile(source, destination);

        Assert.Equal(destinationId, destination.Id);
        Assert.NotEqual(source.Id, destination.Id);
    }

    // Saving a draft replaces the values it has and keeps the others: a monitor synchronized
    // into the profile while the draft was edited keeps its values.
    [Fact]
    public void CopyProfileKeepsTheValuesTheDraftDoesNotHave()
    {
        var saved = CompleteProfile();
        var draft = ProfileEditingCore.CloneProfile(saved);
        draft.DisplayProfiles[0].DigitalVibrance = 15;
        saved.DisplayProfiles.Add(new ProfileDisplaySettings { DisplayName = "New", MonitorId = "MONITOR-3", DigitalVibrance = 64 });

        ProfileEditingCore.CopyProfile(draft, saved);

        Assert.Equal(["MONITOR-1", "MONITOR-2", "MONITOR-3"], saved.DisplayProfiles.Select(v => v.MonitorId));
        Assert.Equal((15, 64), (saved.DisplayProfiles[0].DigitalVibrance, saved.DisplayProfiles[2].DigitalVibrance));
    }

    [Fact]
    public void DisplayColorsEqualIgnoresNonColorProfileChanges()
    {
        var source = CompleteProfile();
        var draft = ProfileEditingCore.CloneProfile(source);

        draft.Name = "Other";
        draft.SetExecutables([@"C:\\Other.exe"]);
        draft.Enabled = false;
        draft.Hotkey++;

        Assert.True(ProfileEditingCore.DisplayColorsEqual(source, draft));
        Assert.False(ProfileEditingCore.ProfilesEqual(source, draft));
    }

    [Fact]
    public void DisplayColorsEqualTracksColorChanges()
    {
        var source = CompleteProfile();
        var draft = ProfileEditingCore.CloneProfile(source);

        draft.DisplayProfiles[1].Gamma = 1.0;

        Assert.False(ProfileEditingCore.DisplayColorsEqual(source, draft));
    }

    [Fact]
    public void ProfilesEqualTracksEveryEditableProfileField()
    {
        var source = CompleteProfile();

        AssertFieldMakesProfileDirty(source, p => p.Name = "Other");
        AssertFieldMakesProfileDirty(source, p => p.SetExecutables([@"C:\Other.exe"]));
        AssertFieldMakesProfileDirty(source, p => p.Hotkey++);
    }

    [Fact]
    public void ProfilesEqualIgnoresImmediateEnabledState()
    {
        var source = CompleteProfile();
        var draft = ProfileEditingCore.CloneProfile(source);

        source.Enabled = false;

        Assert.True(ProfileEditingCore.ProfilesEqual(source, draft));
    }

    [Fact]
    public void CopyProfilePreservesImmediateEnabledState()
    {
        var saved = CompleteProfile();
        var draft = ProfileEditingCore.CloneProfile(saved);
        saved.Enabled = false;
        draft.Name = "WarDog Edited";

        ProfileEditingCore.CopyProfile(draft, saved);

        Assert.False(saved.Enabled);
        Assert.Equal("WarDog Edited", saved.Name);
    }

    [Fact]
    public void ProfilesEqualTracksEveryDisplayFieldOnEveryMonitor()
    {
        var source = CompleteProfile();

        AssertDisplayFieldMakesProfileDirty(source, 0, p => p.Name = "OTHER");
        AssertDisplayFieldMakesProfileDirty(source, 0, p => p.DisplayName = "Other display");
        AssertDisplayFieldMakesProfileDirty(source, 0, p => p.MonitorId = "MONITOR-X");
        AssertDisplayFieldMakesProfileDirty(source, 0, p => p.Brightness++);
        AssertDisplayFieldMakesProfileDirty(source, 0, p => p.Contrast++);
        AssertDisplayFieldMakesProfileDirty(source, 0, p => p.Gamma += 0.1);
        AssertDisplayFieldMakesProfileDirty(source, 0, p => p.DigitalVibrance++);
        AssertDisplayFieldMakesProfileDirty(source, 0, p => p.Hue++);
        AssertDisplayFieldMakesProfileDirty(source, 1, p => p.Gamma = 1.0);
    }

    [Fact]
    public void MonitorIdComparisonRemainsCaseInsensitive()
    {
        var source = CompleteProfile();
        var draft = ProfileEditingCore.CloneProfile(source);
        draft.DisplayProfiles[0].MonitorId = source.DisplayProfiles[0].MonitorId.ToLowerInvariant();

        Assert.True(ProfileEditingCore.ProfilesEqual(source, draft));
    }

    [Fact]
    public void CopyProfileCommitsEntireMultiMonitorDraftAsDeepCopy()
    {
        var saved = CompleteProfile();
        var draft = ProfileEditingCore.CloneProfile(saved);
        draft.Name = "WarDog Edited";
        draft.SetExecutables([@"D:\WarDog\WarDog.exe"]);
        draft.Enabled = false;
        draft.Hotkey = 0x024E;
        draft.DisplayProfiles[0].DigitalVibrance = 15;
        draft.DisplayProfiles[1].Gamma = 1.75;

        ProfileEditingCore.CopyProfile(draft, saved);

        Assert.True(ProfileEditingCore.ProfilesEqual(draft, saved));
        Assert.Equal(15, saved.DisplayProfiles[0].DigitalVibrance);
        Assert.Equal(1.75, saved.DisplayProfiles[1].Gamma);
        Assert.NotSame(draft.DisplayProfiles[0], saved.DisplayProfiles[0]);
        Assert.NotSame(draft.DisplayProfiles[1], saved.DisplayProfiles[1]);

        draft.DisplayProfiles[1].Gamma = 2.8;
        Assert.Equal(1.75, saved.DisplayProfiles[1].Gamma);
    }

    [Fact]
    public void RecreatingDraftFromSavedProfileDiscardsAllUnsavedChanges()
    {
        var saved = CompleteProfile();
        var draft = ProfileEditingCore.CloneProfile(saved);
        draft.Name = "Unsaved";
        draft.DisplayProfiles[0].DigitalVibrance = 0;
        draft.DisplayProfiles[1].Gamma = 2.8;

        var restoredDraft = ProfileEditingCore.CloneProfile(saved);

        Assert.True(ProfileEditingCore.ProfilesEqual(saved, restoredDraft));
        Assert.Equal("WarDog", restoredDraft.Name);
        Assert.Equal(70, restoredDraft.DisplayProfiles[0].DigitalVibrance);
        Assert.Equal(2.42, restoredDraft.DisplayProfiles[1].Gamma);
    }

    [Fact]
    public void ResetLikeDraftChangeDoesNotModifySavedProfileUntilCopy()
    {
        var saved = CompleteProfile();
        var draft = ProfileEditingCore.CloneProfile(saved);
        var selectedDisplay = draft.DisplayProfiles[1];
        selectedDisplay.Brightness = 100;
        selectedDisplay.Contrast = 100;
        selectedDisplay.Gamma = 1;
        selectedDisplay.DigitalVibrance = 50;
        selectedDisplay.Hue = 0;

        Assert.False(ProfileEditingCore.ProfilesEqual(saved, draft));
        Assert.Equal(2.42, saved.DisplayProfiles[1].Gamma);
        Assert.Equal(80, saved.DisplayProfiles[1].DigitalVibrance);
        Assert.Equal(70, saved.DisplayProfiles[0].DigitalVibrance);

        ProfileEditingCore.CopyProfile(draft, saved);
        Assert.True(ProfileEditingCore.ProfilesEqual(saved, draft));
    }

    [Fact]
    public void CopyDisplaySettingsCopiesColorsByMonitorIdWithoutChangingDestinationIdentity()
    {
        var source = CompleteProfile();
        var destination = new ProfileListItem("Destination")
        {
            Enabled = false,
            Hotkey = 99
        };
        destination.SetExecutables([@"C:\Games\Destination.exe"]);
        destination.DisplayProfiles.Add(new ProfileDisplaySettings
        {
            Name = "DEST1", DisplayName = "Destination primary", MonitorId = "monitor-1",
            Brightness = 80, Contrast = 81, Gamma = 0.8, DigitalVibrance = 20, Hue = 20
        });
        destination.DisplayProfiles.Add(new ProfileDisplaySettings
        {
            Name = "DEST2", DisplayName = "Destination secondary", MonitorId = "MONITOR-2",
            Brightness = 82, Contrast = 83, Gamma = 0.9, DigitalVibrance = 30, Hue = 30
        });

        var copied = ProfileEditingCore.CopyDisplaySettings(source, destination);

        Assert.Equal(2, copied);
        Assert.Equal("Destination", destination.Name);
        Assert.Equal(@"C:\Games\Destination.exe", destination.ExecutablePaths[0]);
        Assert.False(destination.Enabled);
        Assert.Equal(99, destination.Hotkey);
        Assert.Equal("DEST1", destination.DisplayProfiles[0].Name);
        Assert.Equal("Destination primary", destination.DisplayProfiles[0].DisplayName);
        Assert.Equal("monitor-1", destination.DisplayProfiles[0].MonitorId);
        Assert.Equal(101, destination.DisplayProfiles[0].Brightness);
        Assert.Equal(70, destination.DisplayProfiles[0].DigitalVibrance);
        Assert.Equal(2.42, destination.DisplayProfiles[1].Gamma);
        Assert.Equal(80, destination.DisplayProfiles[1].DigitalVibrance);
    }

    [Fact]
    public void CopyDisplaySettingsLeavesUnmatchedDestinationDisplaysUnchanged()
    {
        var source = CompleteProfile();
        var destination = new ProfileListItem("Destination");
        destination.DisplayProfiles.Add(new ProfileDisplaySettings
        {
            DisplayName = "Other", MonitorId = "MONITOR-X",
            Brightness = 88, Contrast = 89, Gamma = 1.33, DigitalVibrance = 44, Hue = 12
        });

        var copied = ProfileEditingCore.CopyDisplaySettings(source, destination);

        Assert.Equal(0, copied);
        Assert.Equal(88, destination.DisplayProfiles[0].Brightness);
        Assert.Equal(1.33, destination.DisplayProfiles[0].Gamma);
        Assert.Equal(44, destination.DisplayProfiles[0].DigitalVibrance);
    }

    [Fact]
    public void CopyProfilePreservesWindowsProfileIdentityFields()
    {
        var windows = new ProfileListItem("Windows", isWindowsProfile: true)
        {
            Enabled = true,
            Hotkey = 123
        };
        windows.SetExecutables(["saved-path"]);
        windows.DisplayProfiles.Add(new ProfileDisplaySettings { DisplayName = "Primary", MonitorId = "MONITOR-1", DigitalVibrance = 60 });

        var draft = ProfileEditingCore.CloneProfile(windows);
        draft.Name = "Changed name";
        draft.SetExecutables(["changed-path"]);
        draft.Enabled = false;
        draft.Hotkey = 456;
        draft.DisplayProfiles[0].DigitalVibrance = 75;

        ProfileEditingCore.CopyProfile(draft, windows);

        Assert.Equal("Windows", windows.Name);
        Assert.Equal("saved-path", windows.ExecutablePaths[0]);
        Assert.True(windows.Enabled);
        Assert.Equal(123, windows.Hotkey);
        Assert.Equal(75, windows.DisplayProfiles[0].DigitalVibrance);
    }

    [Fact]
    public void CloneAndCopyPreserveMultipleExecutablesAsIndependentLists()
    {
        var source = CompleteProfile();
        source.AddExecutable(@"C:\Games\WarDogLauncher.exe");
        var draft = ProfileEditingCore.CloneProfile(source);

        Assert.Equal(2, draft.ExecutablePaths.Count);
        Assert.Equal(@"C:\Games\WarDogLauncher.exe", draft.ExecutablePaths[1]);

        draft.ReplaceExecutableAt(1, @"D:\Tools\Launcher.exe");
        Assert.Equal(@"C:\Games\WarDogLauncher.exe", source.ExecutablePaths[1]);
        Assert.False(ProfileEditingCore.ProfilesEqual(source, draft));

        ProfileEditingCore.CopyProfile(draft, source);
        Assert.Equal(draft.ExecutablePaths.ToArray(), source.ExecutablePaths.ToArray());
    }

    [Fact]
    public void ProfileNeverRemovesItsLastExecutable()
    {
        var profile = CompleteProfile();
        Assert.False(profile.RemoveExecutableAt(0));
        Assert.Single(profile.ExecutablePaths);

        profile.AddExecutable(@"C:\Games\Second.exe");
        Assert.True(profile.RemoveExecutableAt(0));
        Assert.Single(profile.ExecutablePaths);
        Assert.Equal(@"C:\Games\Second.exe", profile.ExecutablePaths[0]);
    }

    private static void AssertFieldMakesProfileDirty(ProfileListItem source, Action<ProfileListItem> change)
    {
        var draft = ProfileEditingCore.CloneProfile(source);
        change(draft);
        Assert.False(ProfileEditingCore.ProfilesEqual(source, draft));
    }

    private static void AssertDisplayFieldMakesProfileDirty(ProfileListItem source, int index, Action<ProfileDisplaySettings> change)
    {
        var draft = ProfileEditingCore.CloneProfile(source);
        change(draft.DisplayProfiles[index]);
        Assert.False(ProfileEditingCore.ProfilesEqual(source, draft));
    }
}
