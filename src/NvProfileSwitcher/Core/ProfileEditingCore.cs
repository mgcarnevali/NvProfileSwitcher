namespace NvProfileSwitcher;

public static class ProfileEditingCore
{
    public static ProfileListItem CloneProfile(ProfileListItem source)
    {
        var clone = new ProfileListItem(source.Name, source.IsWindowsProfile, source.Id)
        {
            Enabled = source.Enabled,
            Hotkey = source.Hotkey
        };
        clone.SetExecutables(source.ExecutablePaths);

        foreach (var values in source.DisplayProfiles)
            clone.DisplayProfiles.Add(CloneDisplaySettings(values));

        return clone;
    }

    public static ProfileDisplaySettings CloneDisplaySettings(ProfileDisplaySettings source) => new()
    {
        Name = source.Name,
        DisplayName = source.DisplayName,
        MonitorId = source.MonitorId,
        Brightness = source.Brightness,
        Contrast = source.Contrast,
        Gamma = source.Gamma,
        DigitalVibrance = source.DigitalVibrance,
        Hue = source.Hue
    };

    /// <summary>
    /// Copies a draft into its profile. The display values of the draft replace the profile's
    /// values for the same monitor, and values the draft does not have stay: a monitor
    /// synchronized while the draft was edited keeps its values (deliberate deviation: the
    /// original replaced them all, so saving removed that monitor and the next refresh created
    /// it again from the colors it showed).
    /// </summary>
    public static void CopyProfile(ProfileListItem source, ProfileListItem destination)
    {
        if (!destination.IsWindowsProfile)
        {
            destination.Name = source.Name;
            destination.SetExecutables(source.ExecutablePaths);
            destination.Hotkey = source.Hotkey;
        }

        foreach (var values in source.DisplayProfiles)
        {
            var index = destination.DisplayProfiles.FindIndex(existing => DisplayCore.SameMonitorId(existing.MonitorId, values.MonitorId));
            if (index >= 0)
                destination.DisplayProfiles[index] = CloneDisplaySettings(values);
            else
                destination.DisplayProfiles.Add(CloneDisplaySettings(values));
        }
    }

    public static int CopyDisplaySettings(ProfileListItem source, ProfileListItem destination)
    {
        var copied = 0;
        foreach (var destinationValues in destination.DisplayProfiles)
        {
            var sourceValues = source.DisplayProfiles.FirstOrDefault(values =>
                DisplayCore.SameMonitorId(values.MonitorId, destinationValues.MonitorId));
            if (sourceValues is null)
                continue;

            destinationValues.Brightness = sourceValues.Brightness;
            destinationValues.Contrast = sourceValues.Contrast;
            destinationValues.Gamma = sourceValues.Gamma;
            destinationValues.DigitalVibrance = sourceValues.DigitalVibrance;
            destinationValues.Hue = sourceValues.Hue;
            copied++;
        }

        return copied;
    }

    public static bool DisplayColorsEqual(ProfileListItem a, ProfileListItem b)
    {
        foreach (var x in a.DisplayProfiles)
        {
            var y = b.DisplayProfiles.FirstOrDefault(values =>
                DisplayCore.SameMonitorId(values.MonitorId, x.MonitorId));
            if (y is null)
                continue;

            if (x.Brightness != y.Brightness ||
                x.Contrast != y.Contrast ||
                x.Gamma != y.Gamma ||
                x.DigitalVibrance != y.DigitalVibrance ||
                x.Hue != y.Hue)
                return false;
        }

        return true;
    }

    public static bool ProfilesEqual(ProfileListItem a, ProfileListItem b)
    {
        if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal) ||
            !a.ExecutablePaths.SequenceEqual(b.ExecutablePaths, StringComparer.Ordinal) ||
            a.Hotkey != b.Hotkey ||
            a.DisplayProfiles.Count != b.DisplayProfiles.Count)
            return false;

        for (var i = 0; i < a.DisplayProfiles.Count; i++)
        {
            var x = a.DisplayProfiles[i];
            var y = b.DisplayProfiles[i];
            if (!string.Equals(x.Name, y.Name, StringComparison.Ordinal) ||
                !string.Equals(x.DisplayName, y.DisplayName, StringComparison.Ordinal) ||
                !DisplayCore.SameMonitorId(x.MonitorId, y.MonitorId) ||
                x.Brightness != y.Brightness ||
                x.Contrast != y.Contrast ||
                x.Gamma != y.Gamma ||
                x.DigitalVibrance != y.DigitalVibrance ||
                x.Hue != y.Hue)
                return false;
        }

        return true;
    }
}
