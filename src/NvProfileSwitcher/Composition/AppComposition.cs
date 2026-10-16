using Avalonia.Controls;
using NvProfileSwitcher.Shell;
using NvProfileSwitcher.ViewModels;

namespace NvProfileSwitcher.Composition;

/// <summary>
/// The application built by <see cref="AppHost.Compose"/>: the services, the window host, the
/// tray, the startup coordinator, the ViewModels and the window. The application keeps it alive
/// through the window and its subscriptions; tests use it to reach the parts they check.
/// </summary>
internal sealed record AppComposition(
    AppServices Services,
    TrayService Tray,
    MainWindowHost Host,
    StartupCoordinator Startup,
    MainWindowViewModel ViewModel,
    Window Window);
