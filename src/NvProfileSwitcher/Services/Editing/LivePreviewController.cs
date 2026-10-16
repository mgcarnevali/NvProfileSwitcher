using NvProfileSwitcher.Models;

namespace NvProfileSwitcher.Services.Editing;

/// <summary>
/// The live preview: shows the edited color values of the selected display while they differ
/// from the saved ones, and gives the display back to the real colors when the preview is
/// discarded. It writes nothing itself: the preview is a layer of <see cref="DisplayColorController"/>
/// over the base profile, so ending it writes only the previewed display, with its final values.
/// As in the original, beginning only marks the preview as active, discarding reapplies the real
/// colors only when it was active, and applying does not require it to be active.
/// </summary>
internal sealed class LivePreviewController
{
    private readonly ProfileEditor editor;
    private readonly ActiveProfileService activeProfile;
    private readonly DisplayColorController colors;
    private readonly SwitchingState switching;
    private readonly Func<bool> isWindowShownNormally;

    public LivePreviewController(
        ProfileEditor editor,
        ActiveProfileService activeProfile,
        DisplayColorController colors,
        SwitchingState switching,
        Func<bool> isWindowShownNormally)
    {
        this.editor = editor;
        this.activeProfile = activeProfile;
        this.colors = colors;
        this.switching = switching;
        this.isWindowShownNormally = isWindowShownNormally;
        editor.EditingTargetChanging += Discard;
        editor.DisplayChanged += Resume;
        activeProfile.ProfileSwitching += Discard;
    }

    /// <summary>Whether the preview began and was not accepted or discarded since.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Previews the slider values: begins the preview when it can, and applies them.</summary>
    public void Preview()
    {
        Begin();
        Apply();
    }

    private void Begin()
    {
        if (IsActive || !editor.HasLoadedValues || editor.SelectedDisplay is null)
            return;

        IsActive = true;
    }

    // Shows the slider values on the selected display, unless global switching is paused.
    private void Apply()
    {
        if (!ProfileApplicationCore.CanApply(switching.GlobalPaused)) return;
        if (editor.SelectedDisplay is not { } display) return;

        try
        {
            colors.SetPreview(display.MonitorId, editor.Values);
        }
        catch (Exception)
        {
            // As in the original, a failed preview leaves the display as it is.
        }
    }

    /// <summary>The previewed values were saved: they are the real colors now.</summary>
    public void Accept()
    {
        editor.AcceptValues();
        IsActive = false;
        colors.ClearPreview();
    }

    /// <summary>
    /// Ends the preview: its display shows the real colors again, which are chosen again when
    /// the preview was active. Only the previewed display is written, unless the real colors
    /// are another profile now.
    /// </summary>
    public void Discard()
    {
        var wasActive = IsActive;
        IsActive = false;
        // Restoring the real colors is best effort, as in the original.
        using (colors.Batch())
        {
            colors.ClearPreview();
            if (wasActive)
                activeProfile.ReapplyRealColors(allDisplays: false);
        }
    }

    public void Suspend() => Discard();

    /// <summary>
    /// Applies an active preview again when its draft still has unsaved color changes; false
    /// when there is nothing to preview.
    /// </summary>
    public bool ReapplyIfPreviewing()
    {
        if (!IsActive || !editor.HasUnsavedColorChanges || editor.Session is null)
            return false;

        Apply();
        return true;
    }

    /// <summary>Previews the draft again while the window is shown and it has unsaved color changes.</summary>
    public void Resume()
    {
        if (!isWindowShownNormally() || !editor.HasUnsavedColorChanges || editor.Session is null)
            return;

        Preview();
    }
}
