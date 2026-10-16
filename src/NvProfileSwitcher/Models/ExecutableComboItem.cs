namespace NvProfileSwitcher;

internal enum ExecutableComboAction { None, AddRunningApp, AddBrowse }

internal sealed class ExecutableComboItem
{
    public string Text { get; init; } = string.Empty;
    public int ExecutableIndex { get; init; } = -1;
    public bool CanRemove { get; init; }
    public ExecutableComboAction Action { get; init; }
    public bool IsExecutable => Action == ExecutableComboAction.None;

    public static ExecutableComboItem Executable(string text, int index, bool canRemove) => new() { Text = text, ExecutableIndex = index, CanRemove = canRemove };
    public static ExecutableComboItem CreateAction(string text, ExecutableComboAction action) => new() { Text = text, Action = action };
}
