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

    /// <summary>
    /// The resolution the profile gives this monitor while it is active, or null to leave it
    /// unchanged (the default). Independent of the colors; never set for the Windows profile.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DisplayResolution? Resolution { get; set; }

    /// <summary>
    /// The refresh rate the profile gives this monitor while it is active, independent of
    /// <see cref="Resolution"/>: null keeps the desktop rate (the default), or else the highest rate
    /// or an explicit one. Never set for the Windows profile.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RefreshRateSetting? RefreshRate { get; set; }

    /// <summary>
    /// True when the colors were never read or set: values an application profile got for a
    /// monitor whose colors could not be adjusted and that had no Windows values, only to hold its
    /// resolution. They take the monitor's Windows values as soon as it has them, so made-up colors
    /// never overwrite the ones the user set. Written only when true.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ColorsPending { get; set; }
}

/// <summary>A display resolution in pixels; the refresh rate is chosen separately (<see cref="RefreshRateSetting"/>).</summary>
public sealed record DisplayResolution(int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}

/// <summary>
/// A refresh rate a profile asks for: the highest one the resolution offers, or an explicit rate
/// in hertz. Saved as "Highest" or as the number of hertz.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(RefreshRateSettingConverter))]
public sealed record RefreshRateSetting
{
    private RefreshRateSetting(int hertz) => Hertz = hertz;

    public static RefreshRateSetting Highest { get; } = new(0);

    public static RefreshRateSetting Explicit(int hertz) => new(hertz);

    /// <summary>The explicit rate in hertz; 0 for <see cref="Highest"/>.</summary>
    public int Hertz { get; }

    public bool IsHighest => Hertz == 0;

    public bool IsValid => Hertz >= 0;
}

/// <summary>Reads and writes a <see cref="RefreshRateSetting"/> as "Highest" or a number of hertz.</summary>
public sealed class RefreshRateSettingConverter : System.Text.Json.Serialization.JsonConverter<RefreshRateSetting>
{
    public const string HighestValue = "Highest";

    public override RefreshRateSetting? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            System.Text.Json.JsonTokenType.String when reader.GetString() == HighestValue => RefreshRateSetting.Highest,
            System.Text.Json.JsonTokenType.Number when reader.TryGetInt32(out var hertz) && hertz > 0 => RefreshRateSetting.Explicit(hertz),
            _ => throw new System.Text.Json.JsonException("A refresh rate is \"Highest\" or a positive number of hertz."),
        };

    public override void Write(System.Text.Json.Utf8JsonWriter writer, RefreshRateSetting value, System.Text.Json.JsonSerializerOptions options)
    {
        if (value.IsHighest)
            writer.WriteStringValue(HighestValue);
        else
            writer.WriteNumberValue(value.Hertz);
    }
}
