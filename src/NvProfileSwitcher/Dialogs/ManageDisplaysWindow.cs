using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using System.Collections.ObjectModel;

namespace NvProfileSwitcher;

internal sealed class ManageDisplaysWindow : Window
{
    private readonly ObservableCollection<SavedDisplayInfo> items = [];
    private readonly Action<string> remove;
    private readonly TableView table = new()
    {
        CanUserResizeColumns = true
    };
    private readonly Button removeButton = new() { Content = "Remove", IsEnabled = false, MinWidth = 92 };
    private readonly TextBlock emptyMessage = new()
    {
        Text = "No saved displays found.",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    public ManageDisplaysWindow(IEnumerable<SavedDisplayInfo> source, Action<string> removeAction)
    {
        remove = removeAction;
        Title = "Manage Displays";
        Width = 560; MinWidth = 460; MaxHeight = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        foreach (var item in source) items.Add(item);

        var centeredHeaderTheme = Application.Current?.Resources["CenteredTableViewColumnHeaderTheme"] as ControlTheme;

        table.Columns.Add(new TableViewColumn
        {
            Header = "Display",
            Binding = CompiledBinding.Create<SavedDisplayInfo, string>(item => item.Name),
            Width = new GridLength(7, GridUnitType.Star),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HeaderTheme = centeredHeaderTheme
        });
        table.Columns.Add(new TableViewColumn
        {
            Header = "Status",
            Binding = CompiledBinding.Create<SavedDisplayInfo, string>(item => item.Status),
            Width = new GridLength(3, GridUnitType.Star),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HeaderTheme = centeredHeaderTheme
        });
        table.MaxHeight = 300;
        table.VerticalAlignment = VerticalAlignment.Top;
        table.ItemsSource = items;
        table.SelectionChanged += (_, _) => removeButton.IsEnabled = table.SelectedItem is SavedDisplayInfo { Connected: false };

        removeButton.Click += Remove_Click;
        var close = new Button { Content = "Close", MinWidth = 92 };
        close.Click += (_, _) => Close();
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        buttons.Children.Add(removeButton); Grid.SetColumn(close, 2); buttons.Children.Add(close);

        var body = new Grid(); body.Children.Add(table); body.Children.Add(emptyMessage);
        UpdateEmptyState();
        Content = new Grid
        {
            Margin = new Avalonia.Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 10,
            Children = { body, buttons }
        };
        Grid.SetRow(buttons, 1);
    }

    private async void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (table.SelectedItem is not SavedDisplayInfo { Connected: false } selected) return;
        var confirm = new Window { Title = "Remove display", Width = 430, Height = 190, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var yes = new Button { Content = "Remove", MinWidth = 92 };
        var no = new Button { Content = "Cancel", MinWidth = 92 };
        var result = false;
        yes.Click += (_, _) => { result = true; confirm.Close(); }; no.Click += (_, _) => confirm.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { no, yes } };
        var message = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Remove this display?", Classes = { "dialogMessage" } },
                new TextBlock { Text = selected.Name, Classes = { "dialogMessage" } },
                new TextBlock { Text = "Its saved settings will be permanently removed from all profiles.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "dialogMessage" } }
            }
        };
        confirm.Content = new Grid { Margin = new Avalonia.Thickness(18), RowDefinitions = new RowDefinitions("*,Auto"), Children = { message, buttons } };
        Grid.SetRow(buttons, 1);
        await confirm.ShowDialog(this);
        if (!result) return;
        remove(selected.MonitorId); items.Remove(selected); removeButton.IsEnabled = false;
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var isEmpty = items.Count == 0;
        table.IsVisible = !isEmpty;
        emptyMessage.IsVisible = isEmpty;
    }
}
