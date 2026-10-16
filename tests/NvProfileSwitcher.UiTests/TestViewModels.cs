using NvProfileSwitcher.Core.Tests;
using NvProfileSwitcher.ViewModels;

namespace NvProfileSwitcher.UiTests;

// The main window ViewModels of the composed application, for tests that only show the views:
// the composition's own window is never shown, so the application does not start and the
// NVIDIA API is never created.
internal static class TestViewModels
{
    // The status bar in its widest state: "Unavailable" and an "Unknown" driver.
    public static MainWindowViewModel MainWindow(FakeDialogService? dialogs = null)
    {
        var app = Compose(dialogs);
        app.Nvidia.ReportUnavailable();
        return app.ViewModel;
    }

    // The composed application without configuration: only the Windows profile, selected and
    // being edited, with no display, and the status bar in its initial state.
    public static TestComposition Compose(FakeDialogService? dialogs = null) => new(dialogs: dialogs);
}
