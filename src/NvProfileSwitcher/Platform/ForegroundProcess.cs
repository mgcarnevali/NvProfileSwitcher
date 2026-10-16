using System.Runtime.InteropServices;
using System.Text;

namespace NvProfileSwitcher;

internal static class ForegroundProcess
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public static string GetExecutableName()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return string.Empty;

        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return string.Empty;

        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero) return string.Empty;

        try
        {
            var path = new StringBuilder(32768);
            var length = path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref length)
                ? Path.GetFileName(path.ToString())
                : string.Empty;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder executableName, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
