using NvProfileSwitcher.Services;
using NvProfileSwitcher.Services.Editing;
using NvProfileSwitcher.Shell;

namespace NvProfileSwitcher.Composition;

/// <summary>
/// Maps triggered global hotkeys to the service that performs each action.
/// It holds no state and contains no business logic.
/// </summary>
internal sealed class HotkeyActionDispatcher
{
    private readonly MainWindowHost host;
    private readonly AutomaticSwitchingService automaticSwitching;
    private readonly LivePreviewController preview;

    public HotkeyActionDispatcher(
        HotkeyService hotkeys,
        MainWindowHost host,
        AutomaticSwitchingService automaticSwitching,
        LivePreviewController preview)
    {
        this.host = host;
        this.automaticSwitching = automaticSwitching;
        this.preview = preview;
        hotkeys.Triggered += OnTriggered;
    }

    private void OnTriggered(HotkeyAction action, int profileIndex)
    {
        switch (action)
        {
            case HotkeyAction.ShowHide:
                preview.Discard();
                if (host.IsVisible)
                    host.HideToTray();
                else
                    host.ShowFromTray();
                break;
            case HotkeyAction.WindowsOverride:
                automaticSwitching.ToggleWindowsOverride();
                break;
            case HotkeyAction.ResumeAutomatic:
                automaticSwitching.ResumeAutomatic();
                break;
            case HotkeyAction.GlobalSwitching:
                automaticSwitching.ToggleGlobal();
                break;
            case HotkeyAction.Profile:
                automaticSwitching.ToggleProfileOverride(profileIndex);
                break;
        }
    }
}
