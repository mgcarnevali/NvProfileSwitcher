using NvProfileSwitcher.Core;

namespace NvProfileSwitcher.Services;

/// <summary>The displays saved in the profiles, listed and removed from the Manage displays dialog.</summary>
internal sealed class SavedDisplaysService(ProfileStore profiles, NvidiaService nvidia, ConfigurationService configuration)
{
    public List<SavedDisplayInfo> GetSavedDisplays() => SavedDisplaysCore.Build(profiles.Profiles, nvidia.Displays);

    /// <summary>
    /// Removes the display from every profile and saves. A failed save is reported by the
    /// configuration service (deliberate deviation: the original closed the application).
    /// </summary>
    public void Remove(string monitorId)
    {
        SavedDisplaysCore.Remove(profiles.Profiles, monitorId);
        configuration.Save();
    }
}
