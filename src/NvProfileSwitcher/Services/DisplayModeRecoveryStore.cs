using System.Text.Json;
using NvProfileSwitcher.Core;

namespace NvProfileSwitcher.Services;

/// <summary>
/// The file that lets the next start restore the monitors whose mode this session changed, when
/// it ended without restoring them. It is written before each change and removed once every
/// monitor is back to its desktop mode. Each write replaces the file atomically: a temporary file
/// is written and flushed, then moved over it, so an interrupted write leaves the previous one.
/// </summary>
internal sealed class DisplayModeRecoveryStore(string path)
{
    public string FilePath { get; } = path;

    public bool Exists => File.Exists(FilePath);

    /// <summary>The saved entries, or null when the file is missing or cannot be read.</summary>
    public IReadOnlyList<DisplayModeRecoveryEntry>? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize(stream, NvProfileSwitcherJsonContext.Default.DisplayModeRecovery)?.Monitors;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Saves <paramref name="entries"/>, or removes the file when there are none; false when it could not.</summary>
    public bool Save(IReadOnlyCollection<DisplayModeRecoveryEntry> entries)
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

            var tempPath = FilePath + ".tmp";
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new DisplayModeRecovery { Monitors = [.. entries] },
                    NvProfileSwitcherJsonContext.Default.DisplayModeRecovery);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, FilePath, true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
