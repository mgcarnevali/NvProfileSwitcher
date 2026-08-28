namespace GameProfileSwitcher;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase))));
    }
}
