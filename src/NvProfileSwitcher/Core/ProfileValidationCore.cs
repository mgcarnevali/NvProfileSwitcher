namespace NvProfileSwitcher.Core;

/// <summary>The checks Save profile runs before saving, with the original messages.</summary>
internal static class ProfileValidationCore
{
    /// <summary>
    /// The message that prevents saving <paramref name="draft"/>, or null when it can be saved:
    /// an application profile needs a name and an executable. The name is checked trimmed.
    /// </summary>
    internal static string? Message(ProfileListItem draft)
    {
        if (draft.IsWindowsProfile)
            return null;

        var missingName = string.IsNullOrWhiteSpace(draft.Name);
        var missingExecutable = draft.ExecutablePaths.Count == 0;
        return (missingName, missingExecutable) switch
        {
            (true, true) => "Profile name is required.\nExecutable is required.",
            (true, false) => "Profile name is required.",
            (false, true) => "Executable is required.",
            _ => null
        };
    }
}
