using NvProfileSwitcher.Core;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services.Editing;

namespace NvProfileSwitcher.Services;

/// <summary>
/// Keeps the applied colors in line with the profile that should be active: it asks the
/// switching rules which profile that is, makes it the base profile of
/// <see cref="DisplayColorController"/> and publishes it. It is the only writer of the active
/// profile name and of the active profile shown in the status bar. While the live preview owns
/// the display, checks do nothing; a switch first ends the preview (<see cref="ProfileSwitching"/>).
/// </summary>
internal sealed class ActiveProfileService(
    DisplayColorController colors,
    NvidiaService nvidia,
    ProfileStore profiles,
    SwitchingState switching,
    Func<string> foregroundExecutable,
    ProfileEditor editor,
    Func<bool> isWindowActive)
{
    /// <summary>
    /// Raised right before a switch applies another profile, where the original discarded the
    /// live preview first.
    /// </summary>
    public event Action? ProfileSwitching;

    private bool CanSwitch => nvidia.IsInitialized && nvidia.Displays.Count > 0;

    /// <summary>
    /// Applies the profile automatic switching selects for the foreground application, unless
    /// it is already the active one. An invalid profile override returns to Automatic.
    /// Failures are ignored, as in the original.
    /// </summary>
    public void Check()
    {
        if (!CanSwitch) return;

        // While the window is active with unsaved color changes, the live preview owns the
        // display; the real profile is applied once the preview is suspended.
        if (isWindowActive() && editor.HasUnsavedColorChanges) return;

        var applicationProfiles = profiles.ApplicationProfiles;
        var decision = SwitchingCore.SelectTarget(
            ActiveProfileCore.Candidates(applicationProfiles),
            foregroundExecutable(),
            switching.OverrideMode,
            switching.OverrideProfileIndex);
        switching.OverrideMode = decision.Mode;
        if (decision.Mode != OverrideMode.Profile) switching.OverrideProfileIndex = -1;
        if (string.Equals(switching.ActiveProfileName, decision.ActiveName, StringComparison.Ordinal)) return;

        try
        {
            // The preview ends and the profile is applied in one operation: every display
            // receives the profile's values once, the previewed one included.
            using (colors.Batch(throwOnFailure: true))
            {
                ProfileSwitching?.Invoke();
                colors.SetBase(decision.ProfileIndex >= 0 ? applicationProfiles[decision.ProfileIndex] : null, allDisplays: true);
            }

            Publish(decision.ActiveName);
        }
        catch (Exception)
        {
            // As in the original, a failed switch leaves the current state.
        }
    }

    /// <summary>
    /// Applies the real colors again: the profile override, the automatic match or Windows
    /// (<see cref="ActiveProfileCore.RealColorsTarget"/>). With <paramref name="allDisplays"/>
    /// every display is written again; otherwise only when that profile changed, as after a
    /// preview, which gives its own display back. Best effort, as in the original.
    /// </summary>
    public void ReapplyRealColors(bool allDisplays = true)
    {
        if (!CanSwitch) return;

        try
        {
            var applicationProfiles = profiles.ApplicationProfiles;
            var index = ActiveProfileCore.RealColorsTarget(
                ActiveProfileCore.Candidates(applicationProfiles),
                foregroundExecutable,
                switching.OverrideMode,
                switching.OverrideProfileIndex);
            var target = index >= 0 ? applicationProfiles[index] : null;
            colors.SetBase(target, allDisplays);
            Publish(target?.Name ?? "Windows");
        }
        catch (Exception)
        {
            // Restoring the real colors is best effort, as in the original.
        }
    }

    /// <summary>Forgets the active profile so the next check applies its target.</summary>
    public void Invalidate() => switching.ActiveProfileName = string.Empty;

    /// <summary>Forgets the active profile and checks it again, applying its target.</summary>
    public void Reevaluate()
    {
        Invalidate();
        Check();
    }

    /// <summary>Publishes the active profile, override and global state to the status bar.</summary>
    public void PublishActiveProfile() =>
        switching.DisplayedActiveProfile = new ActiveProfileDisplay(
            switching.ActiveProfileName, switching.OverrideMode, switching.GlobalPaused);

    private void Publish(string name)
    {
        switching.ActiveProfileName = name;
        PublishActiveProfile();
    }
}
