namespace NvProfileSwitcher;

internal static class ProfileApplicationCore
{
    internal static bool CanApply(bool globalPaused, bool forceApply = false) =>
        !globalPaused || forceApply;
}
