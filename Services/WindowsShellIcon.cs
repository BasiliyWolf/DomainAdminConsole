using System.Runtime.InteropServices;

namespace DomainAdminConsole.Services;

/// <summary>
/// Windows Explorer stock icons obtained directly from shell32.dll.
/// Unlike resizing SystemIcons bitmaps, this preserves the alpha channel used by
/// ToolStrip/ContextMenuStrip and avoids the coloured/broken squares seen on some DPI settings.
/// </summary>
internal enum ShellStockIcon : uint
{
    Application = 2,
    Folder = 3,
    FolderOpen = 4,
    World = 13,
    Server = 15,
    Printer = 16,
    Find = 22,
    Link = 29,
    Shield = 77,
    Warning = 78,
    Info = 79,
    Error = 80,
    Key = 81,
    Software = 82,
    Rename = 83,
    Delete = 84,
    DesktopPc = 94,
    Users = 96,
    NetworkConnect = 103,
    Settings = 106
}

internal static class WindowsShellIcon
{
    private const uint ShgsiIcon = 0x000000100;
    private const uint ShgsiSmallIcon = 0x000000001;
    private const int S_OK = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(
        ShellStockIcon siid,
        uint uFlags,
        ref SHSTOCKICONINFO psii);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Image? Get(ShellStockIcon stockIcon, int size = 16)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            var info = new SHSTOCKICONINFO
            {
                cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>(),
                szPath = string.Empty
            };

            var hr = SHGetStockIconInfo(stockIcon, ShgsiIcon | ShgsiSmallIcon, ref info);
            if (hr != S_OK || info.hIcon == IntPtr.Zero)
                return Fallback(stockIcon, size);

            handle = info.hIcon;
            using var clonedIcon = (Icon)Icon.FromHandle(handle).Clone();
            using var bitmap = clonedIcon.ToBitmap();
            return new Bitmap(bitmap, new Size(size, size));
        }
        catch
        {
            return Fallback(stockIcon, size);
        }
        finally
        {
            if (handle != IntPtr.Zero)
                DestroyIcon(handle);
        }
    }

    private static Image? Fallback(ShellStockIcon icon, int size)
    {
        try
        {
            var fallback = icon switch
            {
                ShellStockIcon.Error or ShellStockIcon.Delete => SystemIcons.Error,
                ShellStockIcon.Warning => SystemIcons.Warning,
                ShellStockIcon.Info or ShellStockIcon.Find => SystemIcons.Information,
                ShellStockIcon.Shield => SystemIcons.Shield,
                _ => SystemIcons.Application
            };

            using var source = fallback.ToBitmap();
            return new Bitmap(source, new Size(size, size));
        }
        catch
        {
            return null;
        }
    }
}
