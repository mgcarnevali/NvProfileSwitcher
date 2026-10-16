using Avalonia;

namespace NvProfileSwitcher;

internal static class Program
{
    internal static SingleInstanceService? SingleInstance { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--apply-update", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = UpdateService.ApplyPreparedUpdate(args);
            return;
        }

        if (args.Length > 0 && string.Equals(args[0], UpdateService.RelaunchAfterInstallArgument, StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = UpdateService.RelaunchAfterInstall(args);
            return;
        }

        if (!SingleInstanceService.TryCreatePrimary(out var singleInstance))
        {
            SingleInstanceService.RequestShowFromPrimary();
            return;
        }

        SingleInstance = singleInstance;
        singleInstance.StartListening();
        UpdateService.DeleteLeftoverUpdates();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            singleInstance.Dispose();
            SingleInstance = null;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
