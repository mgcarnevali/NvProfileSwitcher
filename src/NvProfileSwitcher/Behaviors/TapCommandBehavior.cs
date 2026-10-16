using System.Windows.Input;
using Avalonia;
using Avalonia.Input;

namespace NvProfileSwitcher.Behaviors;

/// <summary>
/// Runs a command when the element is tapped, for elements without a Command property
/// such as the footer link text blocks.
/// </summary>
internal sealed class TapCommandBehavior : AvaloniaObject
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<TapCommandBehavior, InputElement, ICommand?>("Command");

    static TapCommandBehavior()
    {
        CommandProperty.Changed.AddClassHandler<InputElement>(OnCommandChanged);
    }

    private TapCommandBehavior()
    {
    }

    public static ICommand? GetCommand(InputElement element) => element.GetValue(CommandProperty);

    public static void SetCommand(InputElement element, ICommand? value) => element.SetValue(CommandProperty, value);

    private static void OnCommandChanged(InputElement element, AvaloniaPropertyChangedEventArgs e)
    {
        element.Tapped -= OnTapped;
        if (e.NewValue is not null)
            element.Tapped += OnTapped;
    }

    private static void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is InputElement element && GetCommand(element) is { } command && command.CanExecute(null))
            command.Execute(null);
    }
}
