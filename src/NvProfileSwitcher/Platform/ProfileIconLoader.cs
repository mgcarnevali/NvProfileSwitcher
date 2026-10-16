using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;

namespace NvProfileSwitcher;

// Windows-only executable icon loader that returns an Avalonia Bitmap.
internal static class ProfileIconLoader
{
    private const uint DiNormal = 0x0003;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;

    public static Bitmap? TryLoadExecutableIcon(string path, int size = 48)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        IntPtr largeIcon = IntPtr.Zero;
        IntPtr smallIcon = IntPtr.Zero;
        IntPtr icon = IntPtr.Zero;
        IntPtr dc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr oldObject = IntPtr.Zero;

        try
        {
            // Request a 48x48 resource first so Windows can choose the best icon
            // image embedded in the executable.
            // Falling straight back to ExtractIconExW can return a 32x32 icon,
            // which becomes visibly soft when Avalonia displays it at profile size.
            var iconId = 0u;
            var extracted = PrivateExtractIconsW(path, 0, size, size, out icon, out iconId, 1, 0);

            if (extracted == 0 || icon == IntPtr.Zero)
            {
                var count = ExtractIconExW(path, 0, out largeIcon, out smallIcon, 1);
                if (count > 0)
                    icon = largeIcon != IntPtr.Zero ? largeIcon : smallIcon;
            }

            if (icon == IntPtr.Zero)
            {
                var fileInfo = new ShellFileInfo();
                if (SHGetFileInfoW(path, 0, ref fileInfo, (uint)Marshal.SizeOf<ShellFileInfo>(),
                    ShgfiIcon | ShgfiLargeIcon) != IntPtr.Zero)
                    icon = fileInfo.Icon;
            }

            if (icon == IntPtr.Zero)
                return null;

            var bmi = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = size,
                    Height = -size, // top-down DIB
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                    SizeImage = (uint)(size * size * 4)
                }
            };

            dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
                return null;

            dib = CreateDIBSection(dc, ref bmi, DibRgbColors, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                return null;

            oldObject = SelectObject(dc, dib);
            if (oldObject == IntPtr.Zero)
                return null;

            if (!DrawIconEx(dc, 0, 0, icon, size, size, 0, IntPtr.Zero, DiNormal))
                return null;

            // Avalonia copies the source buffer when constructing the Bitmap,
            // so the Win32 DIB can be released immediately afterwards.
            return new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                bits,
                new PixelSize(size, size),
                new Vector(96, 96),
                size * 4);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (oldObject != IntPtr.Zero && dc != IntPtr.Zero)
                SelectObject(dc, oldObject);
            if (dib != IntPtr.Zero)
                DeleteObject(dib);
            if (dc != IntPtr.Zero)
                DeleteDC(dc);
            if (icon != IntPtr.Zero)
                DestroyIcon(icon);
            if (largeIcon != IntPtr.Zero && largeIcon != icon)
                DestroyIcon(largeIcon);
            if (smallIcon != IntPtr.Zero && smallIcon != icon && smallIcon != largeIcon)
                DestroyIcon(smallIcon);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIconsW(string szFileName, int nIconIndex,
        int cxIcon, int cyIcon, out IntPtr phicon, out uint piconid, uint nIcons, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string szFileName, int nIconIndex,
        out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes,
        ref ShellFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon,
        int cxWidth, int cyWidth, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo pbmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr ho);
}
