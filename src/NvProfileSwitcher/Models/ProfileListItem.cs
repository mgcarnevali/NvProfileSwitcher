using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace NvProfileSwitcher;

public sealed class ProfileListItem : INotifyPropertyChanged
{
    private bool canMoveUp;
    private bool canMoveDown;

    private string name;
    private readonly List<string> executablePaths = [];
    private Bitmap? icon;
    private bool iconLoaded;
    private bool enabled;

    public ProfileListItem(string name, bool isWindowsProfile = false, Guid? id = null)
    {
        this.name = name;
        IsWindowsProfile = isWindowsProfile;
        Id = isWindowsProfile ? Guid.Empty : id ?? Guid.NewGuid();
    }

    public Guid Id { get; }

    public string Name
    {
        get => name;
        set => SetField(ref name, value);
    }

    public IReadOnlyList<string> ExecutablePaths => executablePaths;

    public bool AddExecutable(string path)
    {
        var normalized = path?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized) || executablePaths.Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase))) return false;
        executablePaths.Add(normalized);
        NotifyExecutablePathsChanged();
        return true;
    }

    public bool ReplaceExecutableAt(int index, string path)
    {
        var normalized = path?.Trim() ?? string.Empty;
        if (index < 0 || index >= executablePaths.Count || string.IsNullOrWhiteSpace(normalized)) return false;
        if (executablePaths.Where((_, i) => i != index).Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase))) return false;
        if (string.Equals(executablePaths[index], normalized, StringComparison.Ordinal)) return true;
        executablePaths[index] = normalized;
        NotifyExecutablePathsChanged();
        return true;
    }

    public bool RemoveExecutableAt(int index)
    {
        if (executablePaths.Count <= 1 || index < 0 || index >= executablePaths.Count) return false;
        executablePaths.RemoveAt(index);
        NotifyExecutablePathsChanged();
        return true;
    }

    public void SetExecutables(IEnumerable<string> paths)
    {
        executablePaths.Clear();
        foreach (var path in paths)
        {
            var normalized = path?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalized) && !executablePaths.Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase)))
                executablePaths.Add(normalized);
        }
        NotifyExecutablePathsChanged();
    }

    private void NotifyExecutablePathsChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExecutablePaths)));
        InvalidateIcon();
    }

    /// <summary>
    /// The icon of the first executable, loaded the first time it is read: only the list rows
    /// read it, so drafts and copies of a profile never load one.
    /// </summary>
    public Bitmap? Icon
    {
        get
        {
            if (!iconLoaded)
            {
                iconLoaded = true;
                icon = IsWindowsProfile || executablePaths.Count == 0 ? null : ProfileIconLoader.TryLoadExecutableIcon(executablePaths[0]);
            }

            return icon;
        }
    }

    public bool HasApplicationIcon => !IsWindowsProfile && Icon is not null;
    public bool ShowFallbackIcon => !IsWindowsProfile && Icon is null;

    // A loaded icon is released when the executables change, and the rows that show it read the new one.
    private void InvalidateIcon()
    {
        if (!iconLoaded)
            return;

        icon?.Dispose();
        icon = null;
        iconLoaded = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasApplicationIcon)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowFallbackIcon)));
    }

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;
            enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDisabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfileStateToolTip)));
        }
    }

    public bool IsDisabled => !Enabled;
    public string ProfileStateToolTip => Enabled ? "Profile ON" : "Profile OFF";
    public int Hotkey { get; set; }
    public List<ProfileDisplaySettings> DisplayProfiles { get; } = [];

    public bool IsWindowsProfile { get; }
    public bool CanReorder => !IsWindowsProfile;

    public bool CanMoveUp
    {
        get => canMoveUp;
        internal set => SetField(ref canMoveUp, value);
    }

    public bool CanMoveDown
    {
        get => canMoveDown;
        internal set => SetField(ref canMoveDown, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


public sealed class ProfileDisplaySettings
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string MonitorId { get; set; } = string.Empty;
    public double Brightness { get; set; } = 100;
    public double Contrast { get; set; } = 100;
    public double Gamma { get; set; } = 1;
    public int DigitalVibrance { get; set; } = 50;
    public int Hue { get; set; }
}
