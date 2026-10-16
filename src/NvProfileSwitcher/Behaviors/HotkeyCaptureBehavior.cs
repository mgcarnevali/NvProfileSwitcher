using Avalonia;
using Avalonia.Input;
using NvProfileSwitcher.ViewModels;

namespace NvProfileSwitcher.Behaviors;

/// <summary>
/// Connects a hotkey field element to its <see cref="HotkeyFieldViewModel"/>: a click or
/// focus by Tab begins the capture, losing the focus ends it, key presses go to the capture,
/// and the element is focused again when the ViewModel asks for it.
/// </summary>
internal sealed class HotkeyCaptureBehavior : AvaloniaObject
{
    public static readonly AttachedProperty<HotkeyFieldViewModel?> FieldProperty =
        AvaloniaProperty.RegisterAttached<HotkeyCaptureBehavior, InputElement, HotkeyFieldViewModel?>("Field");

    // The focus handler subscribed for each element, kept to unsubscribe it.
    private static readonly AttachedProperty<Action?> FocusHandlerProperty =
        AvaloniaProperty.RegisterAttached<HotkeyCaptureBehavior, InputElement, Action?>("FocusHandler");

    static HotkeyCaptureBehavior()
    {
        FieldProperty.Changed.AddClassHandler<InputElement>(OnFieldChanged);
    }

    private HotkeyCaptureBehavior()
    {
    }

    public static HotkeyFieldViewModel? GetField(InputElement element) => element.GetValue(FieldProperty);

    public static void SetField(InputElement element, HotkeyFieldViewModel? value) => element.SetValue(FieldProperty, value);

    private static void OnFieldChanged(InputElement element, AvaloniaPropertyChangedEventArgs e)
    {
        element.PointerPressed -= OnPointerPressed;
        element.GotFocus -= OnGotFocus;
        element.LostFocus -= OnLostFocus;
        element.KeyDown -= OnKeyDown;
        if (e.OldValue is HotkeyFieldViewModel oldField && element.GetValue(FocusHandlerProperty) is { } oldHandler)
            oldField.FocusRequested -= oldHandler;
        element.SetValue(FocusHandlerProperty, null);

        if (e.NewValue is not HotkeyFieldViewModel field)
            return;

        element.PointerPressed += OnPointerPressed;
        element.GotFocus += OnGotFocus;
        element.LostFocus += OnLostFocus;
        element.KeyDown += OnKeyDown;
        Action handler = () => element.Focus();
        field.FocusRequested += handler;
        element.SetValue(FocusHandlerProperty, handler);
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e) => Field(sender)?.BeginCapture();

    private static void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NavigationMethod == NavigationMethod.Tab)
            Field(sender)?.BeginCapture();
    }

    private static void OnLostFocus(object? sender, FocusChangedEventArgs e) => Field(sender)?.FocusLost();

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Field(sender) is { } field && field.KeyPressed(e.Key, e.KeyModifiers))
            e.Handled = true;
    }

    private static HotkeyFieldViewModel? Field(object? sender) => sender is InputElement element ? GetField(element) : null;
}
