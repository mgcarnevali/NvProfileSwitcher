using System.ComponentModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;

namespace NvProfileSwitcher.ViewModels;

/// <summary>
/// One hotkey field: the stored hotkey as text, hidden while capturing, and the
/// capture caret. The input events reach it through <c>HotkeyCaptureBehavior</c>.
/// </summary>
internal sealed class HotkeyFieldViewModel : ObservableObject
{
    private readonly HotkeyField target;
    private readonly HotkeyCaptureController capture;
    private readonly HotkeyService hotkeys;

    public HotkeyFieldViewModel(HotkeyField field, HotkeyCaptureController capture, HotkeyService hotkeys)
    {
        this.target = field;
        this.capture = capture;
        this.hotkeys = hotkeys;
        capture.PropertyChanged += OnCapturePropertyChanged;
        capture.FocusRequested += requested =>
        {
            if (requested == field)
                FocusRequested?.Invoke();
        };
        capture.StoredHotkeyChanged += () => OnPropertyChanged(nameof(Text));
    }

    /// <summary>Asks the view to focus this field.</summary>
    public event Action? FocusRequested;

    /// <summary>The stored hotkey, or "None"; it is shown again unchanged when a capture ends without a new hotkey.</summary>
    public string Text => hotkeys.Format(capture.StoredHotkey(target));

    public bool IsCapturing => capture.CaptureTarget == target;

    public bool IsCaretVisible => IsCapturing && capture.IsCaretVisible;

    public void BeginCapture() => capture.Begin(target);

    public bool KeyPressed(Key key, KeyModifiers modifiers) => capture.KeyPressed(target, key, modifiers);

    public void FocusLost() => capture.FocusLost(target);

    private void OnCapturePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HotkeyCaptureController.CaptureTarget))
            OnPropertyChanged(nameof(IsCapturing));
        OnPropertyChanged(nameof(IsCaretVisible));
    }
}
