using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;

namespace NvProfileSwitcher;

internal sealed class RunningAppInfo
{
    public required string Application { get; init; }
    public required string Executable { get; init; }
    public required string Path { get; init; }
}

internal sealed class RunningAppsWindow : Window
{
    private readonly ObservableCollection<RunningAppInfo> items = [];
    private readonly TableView table = new()
    {
        CanUserResizeColumns = true,
        MaxHeight = 420,
        VerticalAlignment = VerticalAlignment.Top
    };
    private readonly Button selectButton = new()
    {
        Content = "Select",
        IsEnabled = false,
        MinWidth = 92
    };

    public RunningAppsWindow()
    {
        Title = "Running Applications";
        Width = 720;
        MinWidth = 560;
        MaxHeight = 620;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var centeredHeaderTheme = Application.Current?.Resources["CenteredTableViewColumnHeaderTheme"] as ControlTheme;

        table.Columns.Add(new TableViewColumn
        {
            Header = "Application",
            Binding = CompiledBinding.Create<RunningAppInfo, string>(item => item.Application),
            Width = new GridLength(4, GridUnitType.Star),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HeaderTheme = centeredHeaderTheme
        });
        table.Columns.Add(new TableViewColumn
        {
            Header = "Executable",
            Binding = CompiledBinding.Create<RunningAppInfo, string>(item => item.Executable),
            Width = new GridLength(3, GridUnitType.Star),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HeaderTheme = centeredHeaderTheme
        });

        table.ItemsSource = items;
        table.SelectionChanged += (_, _) => selectButton.IsEnabled = table.SelectedItem is RunningAppInfo;
        table.DoubleTapped += Table_DoubleTapped;

        var refreshButton = new Button
        {
            Content = "Refresh",
            MinWidth = 92
        };
        refreshButton.Click += (_, _) => RefreshProcesses();

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 92
        };
        cancelButton.Click += (_, _) => Close(null);
        selectButton.Click += (_, _) => SelectCurrent();

        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 8
        };
        buttons.Children.Add(refreshButton);
        Grid.SetColumn(cancelButton, 2);
        buttons.Children.Add(cancelButton);
        Grid.SetColumn(selectButton, 3);
        buttons.Children.Add(selectButton);

        var empty = new TextBlock
        {
            Text = "No running applications with accessible executable paths were found.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsVisible = false
        };

        var body = new Grid();
        body.Children.Add(table);
        body.Children.Add(empty);

        Content = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 10,
            Children = { body, buttons }
        };
        Grid.SetRow(buttons, 1);

        RefreshProcesses();
        empty.IsVisible = items.Count == 0;
    }

    private void RefreshProcesses()
    {
        var selectedPath = (table.SelectedItem as RunningAppInfo)?.Path;
        var running = EnumerateRunningApplications();

        items.Clear();
        foreach (var item in running)
            items.Add(item);

        table.SelectedItem = !string.IsNullOrWhiteSpace(selectedPath)
            ? items.FirstOrDefault(x => string.Equals(x.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            : null;
        selectButton.IsEnabled = table.SelectedItem is RunningAppInfo;
    }

    private static List<RunningAppInfo> EnumerateRunningApplications()
    {
        var running = new List<RunningAppInfo>();
        var currentProcessId = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((window, _) =>
        {
            // Only visible, unowned top-level windows with a non-empty title
            // are considered applications.
            if (!NativeMethods.IsWindowVisible(window) || NativeMethods.GetWindow(window, NativeMethods.GW_OWNER) != IntPtr.Zero)
                return true;

            var titleLength = NativeMethods.GetWindowTextLength(window);
            if (titleLength <= 0)
                return true;

            var titleBuffer = new StringBuilder(titleLength + 1);
            if (NativeMethods.GetWindowText(window, titleBuffer, titleBuffer.Capacity) <= 0)
                return true;

            var title = titleBuffer.ToString();
            if (string.IsNullOrWhiteSpace(title))
                return true;

            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == currentProcessId)
                return true;

            var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (process == IntPtr.Zero)
                return true;

            try
            {
                var pathBuffer = new StringBuilder(32768);
                var pathLength = pathBuffer.Capacity;
                if (!NativeMethods.QueryFullProcessImageName(process, 0, pathBuffer, ref pathLength) || pathLength <= 0)
                    return true;

                var path = pathBuffer.ToString(0, pathLength);
                if (running.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)))
                    return true;

                running.Add(new RunningAppInfo
                {
                    Application = title,
                    Executable = System.IO.Path.GetFileName(path),
                    Path = path
                });
            }
            finally
            {
                NativeMethods.CloseHandle(process);
            }

            return true;
        }, IntPtr.Zero);

        return running
            .OrderBy(x => x.Application, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void Table_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (table.SelectedItem is RunningAppInfo)
            SelectCurrent();
    }

    private void SelectCurrent()
    {
        if (table.SelectedItem is RunningAppInfo selected)
            Close(selected.Path);
    }

    private static class NativeMethods
    {
        internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        internal const uint GW_OWNER = 4;

        internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLength(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder executableName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }

}
