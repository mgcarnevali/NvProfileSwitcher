using System.Text.Json;

namespace NvProfileSwitcher.Services;

/// <summary>A monitor whose resolution this application changed: its desktop resolution and the one applied.</summary>
internal sealed record ResolutionRecoveryEntry(string MonitorId, DisplayResolution Original, DisplayResolution Applied);

/// <summary>The content of the recovery file.</summary>
internal sealed class ResolutionRecovery
{
    public List<ResolutionRecoveryEntry> Monitors { get; set; } = [];
}

/// <summary>
/// The file that lets the next start restore the monitors whose resolution a session changed and
/// could not restore, as after a crash. It is written before each change and removed once every
/// monitor is back. Each write replaces the file atomically (a flushed temporary file moved over
/// it), so an interrupted write leaves the previous one.
/// </summary>
internal sealed class ResolutionRecoveryStore(string path)
{
    public string FilePath { get; } = path;

    /// <summary>The saved entries; none when the file is missing or cannot be read.</summary>
    public IReadOnlyList<ResolutionRecoveryEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize(stream, NvProfileSwitcherJsonContext.Default.ResolutionRecovery)?.Monitors
                .Where(entry => !string.IsNullOrWhiteSpace(entry.MonitorId) && entry.Original is { IsValid: true } && entry.Applied is { IsValid: true })
                .ToList() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Saves <paramref name="entries"/>, or removes the file when there are none; false when it could not.</summary>
    public bool Save(IReadOnlyCollection<ResolutionRecoveryEntry> entries)
    {
        try
        {
            if (entries.Count == 0)
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
                return true;
            }

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var temporary = FilePath + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new ResolutionRecovery { Monitors = [.. entries] },
                    NvProfileSwitcherJsonContext.Default.ResolutionRecovery);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, FilePath, true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
