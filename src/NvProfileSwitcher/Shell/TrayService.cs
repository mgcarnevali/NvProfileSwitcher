using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using NvProfileSwitcher.Models;
using NvProfileSwitcher.Services;

namespace NvProfileSwitcher.Shell;

/// <summary>
/// The notification area icon and its menu. The icon starts hidden and follows the
/// visibility the window host requests; clicking it shows the main window.
/// </summary>
internal sealed class TrayService : IDisposable
{
    private readonly MainWindowHost host;
    private readonly SwitchingState switching;
    private readonly AutomaticSwitchingService automaticSwitching;
    private readonly UpdateCheckService updateCheck;
    private readonly IDialogService dialogs;
    private readonly Action shutdown;
    private readonly TrayIcon trayIcon;
    private readonly NativeMenuItem globalSwitchingMenuItem;

    public TrayService(
        MainWindowHost host,
        SwitchingState switching,
        AutomaticSwitchingService automaticSwitching,
        UpdateCheckService updateCheck,
        IDialogService dialogs,
        Action shutdown)
    {
        this.host = host;
        this.switching = switching;
        this.automaticSwitching = automaticSwitching;
        this.updateCheck = updateCheck;
        this.dialogs = dialogs;
        this.shutdown = shutdown;

        globalSwitchingMenuItem = new NativeMenuItem("Global Switching")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = !switching.GlobalPaused
        };
        trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://NvProfileSwitcher/Assets/branding/NvProfileSwitcher.ico"))),
            ToolTipText = "NvProfileSwitcher",
            IsVisible = false,
            Menu = CreateMenu()
        };
        trayIcon.Clicked += (_, _) => ShowMainWindow();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { trayIcon });

        host.TrayIconVisibilityRequested += SetIconVisible;
        switching.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SwitchingState.GlobalPaused))
                globalSwitchingMenuItem.IsChecked = !switching.GlobalPaused;
        };
    }

    public bool IsIconVisible => trayIcon.IsVisible;

    public NativeMenu Menu => trayIcon.Menu!;

    public void SetIconVisible(bool visible) => trayIcon.IsVisible = visible;

    /// <summary>Shows the main window; the window host hides the icon first.</summary>
    public void ShowMainWindow() => host.ShowFromTray();

    /// <summary>Show request from a second instance, which arrives on another thread.</summary>
    public void PostShowMainWindow() => Dispatcher.UIThread.Post(ShowMainWindow);

    public void Dispose() => trayIcon.Dispose();

    private NativeMenu CreateMenu()
    {
        var open = new NativeMenuItem("Open NvProfileSwitcher");
        open.Click += (_, _) => ShowMainWindow();

        globalSwitchingMenuItem.Click += (_, _) => automaticSwitching.ToggleGlobal();

        var checkUpdates = new NativeMenuItem("Check for updates");
        checkUpdates.Click += (_, _) => Dispatcher.UIThread.Post(() => _ = updateCheck.CheckAsync(manual: true));

        var about = new NativeMenuItem("About NvProfileSwitcher");
        about.Click += (_, _) => Dispatcher.UIThread.Post(() => _ = dialogs.ShowAboutAsync());

        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) =>
        {
            SetIconVisible(false);
            shutdown();
        };

        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(globalSwitchingMenuItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(checkUpdates);
        menu.Items.Add(about);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);
        return menu;
    }
}
