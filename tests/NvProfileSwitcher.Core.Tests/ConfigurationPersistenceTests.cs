using System.Globalization;
using Xunit;
using static NvProfileSwitcher.Core.Tests.ConfigFixtures;

namespace NvProfileSwitcher.Core.Tests;

// The persistence guarantees: a configuration that could not be loaded is never lost, a save
// reports whether config.json was replaced, and failures are reported without repeating.
public sealed class ConfigurationPersistenceTests
{
    private static string[] Backups(ConfigSession session) =>
        Directory.GetFiles(session.ConfigDirectory, "config.invalid-*.json");

    // Saving fails while a directory takes the place of the temporary file.
    private static string BreakSaving(ConfigSession session) =>
        Directory.CreateDirectory(session.ConfigPath + ".tmp").FullName;

    // The backup names the load could use within the next seconds.
    private static IEnumerable<string> PossibleBackupNames(ConfigSession session, int suffixes)
    {
        var now = DateTime.Now;
        for (var second = 0; second < 3; second++)
        {
            var stamp = now.AddSeconds(second).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            for (var attempt = 0; attempt < suffixes; attempt++)
                yield return Path.Combine(session.ConfigDirectory,
                    attempt == 0 ? $"config.invalid-{stamp}.json" : $"config.invalid-{stamp}-{attempt}.json");
        }
    }

    [Theory]
    [MemberData(nameof(ConfigurationServiceTests.UnreadableConfigurations), MemberType = typeof(ConfigurationServiceTests))]
    public void InvalidContentIsKeptAsAnExactCopyAndReportedOnce(string json)
    {
        using var session = new ConfigSession(json);
        var original = File.ReadAllBytes(session.ConfigPath);

        session.Service.Load();

        var backup = Assert.Single(Backups(session));
        Assert.Equal(original, File.ReadAllBytes(backup));
        Assert.Equal(original, File.ReadAllBytes(session.ConfigPath));
        Assert.Empty(session.Dialogs.Messages);

        session.Service.ReportLoadProblem();
        session.Service.ReportLoadProblem();

        var message = Assert.Single(session.Dialogs.Messages);
        Assert.Contains("is not valid", message.Message);
        Assert.Contains(backup, message.Message);
        Assert.DoesNotContain("Import", message.Message);
    }

    // With the copy kept, the session saves normally and replaces the invalid file.
    [Fact]
    public void TheNextSaveReplacesTheInvalidFileAndKeepsTheCopy()
    {
        using var session = new ConfigSession("{ not json");
        session.Service.Load();

        Assert.True(session.Service.Save());

        Assert.Empty(Node(File.ReadAllText(session.ConfigPath))["Windows Profiles"]!.AsArray());
        Assert.Equal("{ not json", File.ReadAllText(Assert.Single(Backups(session))));
    }

    [Fact]
    public void AnExistingCopyIsNeverOverwritten()
    {
        using var session = new ConfigSession("{ not json");
        var existing = PossibleBackupNames(session, suffixes: 1).ToList();
        foreach (var path in existing)
            File.WriteAllText(path, "earlier copy");

        session.Service.Load();

        Assert.All(existing, path => Assert.Equal("earlier copy", File.ReadAllText(path)));
        var backup = Assert.Single(Backups(session).Except(existing));
        Assert.Equal("{ not json", File.ReadAllText(backup));
    }

    // Without a copy, the invalid file is the only one left: nothing is saved over it.
    [Fact]
    public void WithoutACopyTheInvalidFileIsNeverSavedOver()
    {
        using var session = new ConfigSession("{ not json");
        foreach (var path in PossibleBackupNames(session, suffixes: 101))
            File.WriteAllText(path, "taken");

        session.Service.Load();
        session.Settings.AlwaysOnTop = true;

        Assert.False(session.Service.Save());
        Assert.False(session.Service.SaveIfPending());
        Assert.Equal("{ not json", File.ReadAllText(session.ConfigPath));
        session.Service.ReportLoadProblem();
        Assert.Contains("could not be kept", Assert.Single(session.Dialogs.Messages).Message);
    }

    // A file that exists but cannot be read is left untouched: the session starts with the
    // defaults and saves nothing, without reporting each save.
    [Fact]
    public void AnUnreadableFileIsNeverModified()
    {
        using var session = new ConfigSession(Canonical);
        var original = File.ReadAllBytes(session.ConfigPath);
        using (new FileStream(session.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
            session.Service.Load();

        Assert.Equal("Windows", Assert.Single(session.Profiles.Profiles).Name);
        Assert.Empty(Backups(session));
        Assert.False(session.Service.Save());
        Assert.False(session.Service.SaveIfPending());
        Assert.Equal(original, File.ReadAllBytes(session.ConfigPath));

        session.Service.ReportLoadProblem();
        var message = Assert.Single(session.Dialogs.Messages);
        Assert.Contains("could not be read", message.Message);
        Assert.Contains("was not modified", message.Message);
    }

    // Importing is the explicit replacement of the configuration: it ends the protection.
    [Fact]
    public async Task ImportingEndsTheProtection()
    {
        using var session = new ConfigSession(Canonical);
        using (new FileStream(session.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
            session.Service.Load();
        Assert.True(ConfigurationCore.TryValidateImportedConfiguration(Canonical, out var config));

        await session.Service.ImportAsync(Canonical, config);
        session.Settings.AlwaysOnTop = true;

        Assert.True(session.Service.Save());
        Assert.Contains("\"AlwaysOnTop\": true", File.ReadAllText(session.ConfigPath));
    }

    // Only the first failure of a streak is reported; a successful save ends the streak.
    [Fact]
    public void FailedSavesAreReportedOncePerStreak()
    {
        using var session = new ConfigSession();
        session.Service.Load();
        var broken = BreakSaving(session);

        Assert.False(session.Service.Save());
        Assert.False(session.Service.Save());
        Assert.Single(session.Dialogs.Messages);
        Assert.Contains("remain active", session.Dialogs.Messages[0].Message);

        Directory.Delete(broken);
        Assert.True(session.Service.Save());
        BreakSaving(session);
        Assert.False(session.Service.Save());

        Assert.Equal(2, session.Dialogs.Messages.Count);
    }

    // A caller that reports the failure itself starts the streak without the notice.
    [Fact]
    public void AFailureReportedByTheCallerStartsTheStreak()
    {
        using var session = new ConfigSession();
        session.Service.Load();
        BreakSaving(session);

        Assert.False(session.Service.Save(reportFailure: false));
        Assert.False(session.Service.Save());

        Assert.Empty(session.Dialogs.Messages);
    }

    [Fact]
    public void SaveIfPendingWritesOnlyAfterAFailedSave()
    {
        using var session = new ConfigSession();
        session.Service.Load();

        Assert.True(session.Service.SaveIfPending());
        Assert.False(File.Exists(session.ConfigPath));

        var broken = BreakSaving(session);
        session.Settings.AlwaysOnTop = true;
        Assert.False(session.Service.Save());
        Directory.Delete(broken);

        Assert.True(session.Service.SaveIfPending());
        Assert.Contains("\"AlwaysOnTop\": true", File.ReadAllText(session.ConfigPath));
        Assert.Single(session.Dialogs.Messages);
    }

    // Saving an edited profile writes exactly what applying the draft and saving wrote,
    // without changing the state. Enabled stays the profile's own.
    [Fact]
    public void SavingAProfileWritesWhatApplyingTheDraftWouldWrite()
    {
        using var applied = new ConfigSession(Canonical);
        using var replaced = new ConfigSession(Canonical);
        applied.Service.Load();
        replaced.Service.Load();

        (ProfileListItem Source, ProfileListItem Draft) Edit(ConfigSession session)
        {
            var source = session.Profiles.Profiles[1];
            var draft = ProfileEditingCore.CloneProfile(source);
            source.Enabled = false;
            draft.Name = "Renamed";
            draft.Hotkey = 0x0341;
            draft.DisplayProfiles[0].Brightness = 120;
            return (source, draft);
        }

        var (appliedSource, appliedDraft) = Edit(applied);
        ProfileEditingCore.CopyProfile(appliedDraft, appliedSource);
        Assert.True(applied.Service.Save());

        var (source, draft) = Edit(replaced);
        var saved = ProfileEditingCore.CloneProfile(source);
        ProfileEditingCore.CopyProfile(draft, saved);
        Assert.True(replaced.Service.Save(source, saved, reportFailure: true));

        Assert.Equal(File.ReadAllBytes(applied.ConfigPath), File.ReadAllBytes(replaced.ConfigPath));
        Assert.Contains("\"Enabled\": false", File.ReadAllText(replaced.ConfigPath));
        Assert.Equal("Café & Games", source.Name);
        Assert.Equal(105, source.DisplayProfiles[0].Brightness);
    }

    // The unsaved new profile is written when it is the profile being saved.
    [Fact]
    public void SavingTheUnsavedNewProfileWritesIt()
    {
        using var session = new ConfigSession(Canonical);
        session.Service.Load();
        var added = new ProfileListItem("New Profile") { Enabled = true };
        added.SetExecutables(["new.exe"]);
        session.Profiles.AddUnsaved(added);
        var saved = ProfileEditingCore.CloneProfile(added);
        saved.Name = "Added";

        Assert.True(session.Service.Save(added, saved, reportFailure: true));

        Assert.Contains("\"Name\": \"Added\"", File.ReadAllText(session.ConfigPath));
        Assert.Equal("New Profile", added.Name);
        Assert.Same(added, session.Profiles.UnsavedNewProfile);
    }
}
