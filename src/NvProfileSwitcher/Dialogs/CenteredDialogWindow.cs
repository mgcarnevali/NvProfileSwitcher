using Avalonia.Controls;

namespace NvProfileSwitcher;

/// <summary>
/// Base window for application dialogs. Dialogs opened with ShowDialog(owner)
/// are centered by Avalonia relative to their owner.
/// </summary>
internal class CenteredDialogWindow : Window
{
    public CenteredDialogWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }
}
