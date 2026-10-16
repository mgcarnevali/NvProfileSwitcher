using Avalonia;
using Avalonia.Controls;

namespace NvProfileSwitcher.Shell;

/// <summary>
/// Adapts the main window for the rest of the application. It translates window events,
/// shows and hides the window for the tray, forwards raw window messages and provides
/// the dialog owner. It has no domain knowledge: it never references services,
/// ViewModels or configuration, and it never decides policies such as whether
/// minimizing hides the window to the tray.
/// </summary>
internal sealed class MainWindowHost
{
    // Replaced, never mutated: a dispatch iterates the array it started with, so handlers
    // added or removed while a message is being dispatched cannot affect that iteration.
    private Func<uint, nint, bool>[] messageHandlers = [];
    private Window? window;
    private Func<bool>? closeConfirmationRequired;
    private Func<Task<bool>>? confirmCloseAsync;
    private bool hidingForTray;
    private bool startHiddenToTray;
    private bool allowCloseAfterConfirm;
    private bool wndProcHookInstalled;

    public event Action? Opened;
    public event Action? Activated;
    public event Action? Deactivated;
    public event Action? Minimized;
    public event Action? Restored;
    public event Action? Hidden;
    public event Action? HidingToTray;
    public event Action<bool>? TrayIconVisibilityRequested;
    public event Action? Closing;
    public event Action? Closed;

    /// <summary>Owner for dialogs, storage pickers and the launcher.</summary>
    public Window? Owner => window;

    /// <summary>Native window handle, available after the window was opened on Windows.</summary>
    public nint WindowHandle { get; private set; }

    public bool IsActive => window?.IsActive == true;
    public bool IsVisible => window?.IsVisible == true;
    public bool IsMinimized => window?.WindowState == WindowState.Minimized;
    public bool IsShownNormally => IsVisible && !IsMinimized;

    public void Attach(Window mainWindow)
    {
        if (window is not null)
            throw new InvalidOperationException("The main window is already attached.");

        window = mainWindow;
        window.Opened += (_, _) =>
        {
            InstallMessageHook();
            Opened?.Invoke();

            // As in the original startup, a window started hidden is hidden only after
            // every Opened step has run, and only the first time it opens.
            if (startHiddenToTray)
            {
                startHiddenToTray = false;
                HideToTray();
            }
        };
        window.Activated += (_, _) => Activated?.Invoke();
        window.Deactivated += (_, _) => Deactivated?.Invoke();
        window.Closing += OnClosing;
        window.Closed += (_, _) => Closed?.Invoke();
        window.PropertyChanged += OnWindowPropertyChanged;
    }

    /// <summary>
    /// Registers a handler for raw window messages. The handler returns true when it
    /// handled the message.
    /// </summary>
    public void AddMessageHandler(Func<uint, nint, bool> handler) =>
        messageHandlers = [.. messageHandlers, handler];

    public void RemoveMessageHandler(Func<uint, nint, bool> handler)
    {
        var index = Array.IndexOf(messageHandlers, handler);
        if (index >= 0)
            messageHandlers = [.. messageHandlers[..index], .. messageHandlers[(index + 1)..]];
    }

    /// <summary>
    /// Dispatches a raw window message to the registered handlers and returns true when
    /// any of them handled it.
    /// </summary>
    public bool DispatchMessage(uint msg, nint wParam)
    {
        var handled = false;
        foreach (var handler in messageHandlers)
        {
            if (handler(msg, wParam))
                handled = true;
        }

        return handled;
    }

    /// <summary>
    /// Configures the close guard once and completely: <paramref name="confirmationRequired"/>
    /// tells whether closing must be confirmed and <paramref name="confirmAsync"/> asks for
    /// that confirmation. While confirmation is required the window only closes after it
    /// is given.
    /// </summary>
    public void SetCloseGuard(Func<bool> confirmationRequired, Func<Task<bool>> confirmAsync)
    {
        ArgumentNullException.ThrowIfNull(confirmationRequired);
        ArgumentNullException.ThrowIfNull(confirmAsync);
        if (closeConfirmationRequired is not null)
            throw new InvalidOperationException("The close guard is already configured.");

        closeConfirmationRequired = confirmationRequired;
        confirmCloseAsync = confirmAsync;
    }

    public void SetTopmost(bool topmost)
    {
        if (window is not null)
            window.Topmost = topmost;
    }

    /// <summary>
    /// Hides the window to the tray once it has opened, after every <see cref="Opened"/>
    /// handler has run. Must be called after <see cref="Attach"/> and before the window is shown.
    /// </summary>
    public void StartHiddenToTray()
    {
        if (window is null)
            throw new InvalidOperationException("The main window must be attached before it can start hidden.");

        startHiddenToTray = true;
        window.ShowActivated = false;
    }

    public void ShowFromTray()
    {
        if (window is null)
            return;

        TrayIconVisibilityRequested?.Invoke(false);
        hidingForTray = false;
        window.ShowInTaskbar = true;
        if (!window.IsVisible)
            window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.ShowActivated = true;
        window.Activate();
    }

    public void HideToTray()
    {
        if (window is null || hidingForTray)
            return;

        HidingToTray?.Invoke();
        hidingForTray = true;
        window.ShowInTaskbar = false;
        TrayIconVisibilityRequested?.Invoke(true);
        window.Hide();
    }

    public void RemoveMessageHook()
    {
        if (window is null || !wndProcHookInstalled)
            return;

        Win32Properties.RemoveWndProcHookCallback(window, WndProcHook);
        wndProcHookInstalled = false;
    }

    private void InstallMessageHook()
    {
        // The main window is hidden and shown repeatedly, but the hook belongs to the
        // window lifetime: install it only once so each message is dispatched once.
        if (window is null || wndProcHookInstalled)
            return;

        var handle = window.TryGetPlatformHandle();
        if (handle is null || !string.Equals(handle.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
            return;

        WindowHandle = handle.Handle;
        Win32Properties.AddWndProcHookCallback(window, WndProcHook);
        wndProcHookInstalled = true;
    }

    private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (DispatchMessage(msg, wParam))
            handled = true;

        return IntPtr.Zero;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (allowCloseAfterConfirm)
        {
            Closing?.Invoke();
            return;
        }

        if (closeConfirmationRequired is not null && confirmCloseAsync is not null && closeConfirmationRequired())
        {
            e.Cancel = true;
            if (!await confirmCloseAsync())
                return;

            allowCloseAfterConfirm = true;
            window?.Close();
            // A close that did not happen leaves the guard in place for the next one.
            if (window?.IsVisible == true)
                allowCloseAfterConfirm = false;
            return;
        }

        Closing?.Invoke();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (window is null)
            return;

        if (e.Property == Window.WindowStateProperty && window.WindowState == WindowState.Minimized)
            Minimized?.Invoke();
        else if (e.Property == Window.WindowStateProperty && window.WindowState != WindowState.Minimized)
            Restored?.Invoke();
        else if (e.Property == Visual.IsVisibleProperty && !window.IsVisible)
            Hidden?.Invoke();
    }
}
