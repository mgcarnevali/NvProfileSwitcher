namespace NvProfileSwitcher.Services.Editing;

/// <summary>
/// The edit of one profile: the profile in the list and a draft cloned from it. The draft
/// collects every change until it is saved into the source or discarded.
/// </summary>
internal sealed class ProfileEditorSession(ProfileListItem source)
{
    public ProfileListItem Source { get; } = source;

    public ProfileListItem Draft { get; } = ProfileEditingCore.CloneProfile(source);

    /// <summary>Whether the draft differs from its source in any saved field.</summary>
    public bool HasChanges => !ProfileEditingCore.ProfilesEqual(Source, Draft);

    /// <summary>Whether the draft has color values that differ from its source.</summary>
    public bool HasUnsavedColorChanges => !ProfileEditingCore.DisplayColorsEqual(Source, Draft);
}
