using CommunityToolkit.Mvvm.ComponentModel;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;

namespace NvProfileSwitcher.ViewModels;

/// <summary>
/// Hotkeys panel: global hotkey fields.
/// Depends only on services and shared state, never on another panel ViewModel.
/// </summary>
internal sealed class HotkeysViewModel : ObservableObject
{
    public HotkeysViewModel(HotkeyCaptureController capture, HotkeyService hotkeys)
    {
        ShowHide = new HotkeyFieldViewModel(HotkeyField.ShowHide, capture, hotkeys);
        WindowsOverride = new HotkeyFieldViewModel(HotkeyField.WindowsOverride, capture, hotkeys);
        ResumeAutomatic = new HotkeyFieldViewModel(HotkeyField.ResumeAutomatic, capture, hotkeys);
        GlobalSwitching = new HotkeyFieldViewModel(HotkeyField.GlobalSwitching, capture, hotkeys);
    }

    public HotkeyFieldViewModel ShowHide { get; }
    public HotkeyFieldViewModel WindowsOverride { get; }
    public HotkeyFieldViewModel ResumeAutomatic { get; }
    public HotkeyFieldViewModel GlobalSwitching { get; }
}
