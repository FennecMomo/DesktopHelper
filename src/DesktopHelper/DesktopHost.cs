using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace DesktopHelper;

internal static class DesktopHost
{
    internal delegate bool EnumProc(nint window, nint data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [DllImport("user32.dll", SetLastError = true)] static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint window);
    [DllImport("user32.dll")] static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out RectNative rect);
    [DllImport("user32.dll")] static extern bool ScreenToClient(nint window, ref PointNative point);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint window, StringBuilder name, int length);
    [DllImport("user32.dll")] static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern nint SHGetFileInfo(string path, uint attrs, out ShellFileInfo info, uint size, uint flags);
    [StructLayout(LayoutKind.Sequential)] internal struct RectNative { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ShellFileInfo
    {
        public nint Icon; public int IconIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    public static string ClassName(nint handle) { var text = new StringBuilder(256); GetClassName(handle, text, text.Capacity); return text.ToString(); }
    internal static bool IsLayered(nint window) => (GetWindowLong(window, -20) & 0x80000) != 0;
    internal static bool IsClickThrough(nint window) => (GetWindowLong(window, -20) & 0x08000020) == 0x08000020;
    public static nint FindDesktop()
    {
        nint host = 0;
        EnumWindows((window, _) =>
        {
            if (FindWindowEx(window, 0, "SHELLDLL_DefView", null) == 0) return true;
            host = window; return false;
        }, 0);
        return host;
    }
    public static bool Attach(nint window)
    {
        var parent = FindDesktop();
        if (parent == 0) return false;
        var previous = GetWindowLong(window, -16);
        SetWindowLong(window, -16, (previous & ~unchecked((int)0x80000000)) | 0x40000000); // WS_POPUP -> WS_CHILD
        SetParent(window, parent);
        if (GetParent(window) != parent) { SetWindowLong(window, -16, previous); return false; }
        SetWindowPos(window, 0, 0, 0, 0, 0, 0x0033); // frame changed, no move/size/activation
        return true;
    }
    public static void Detach(nint window)
    {
        SetParent(window, 0);
        SetWindowLong(window, -16, (GetWindowLong(window, -16) & ~0x40000000) | unchecked((int)0x80000000));
        SetWindowPos(window, 0, 0, 0, 0, 0, 0x0033);
    }
    public static void SetClickThrough(nint window, bool enabled)
    {
        // Same flags as DesktopTools' layered-window lock; WPF supplies WS_EX_LAYERED.
        const int flags = 0x20 | 0x08000000;
        var style = GetWindowLong(window, -20);
        SetWindowLong(window, -20, enabled ? style | flags : style & ~flags);
        SetWindowPos(window, 0, 0, 0, 0, 0, 0x0033);
    }
    public static void Place(nint window, int screenX, int screenY, int width, int height)
    {
        var point = new PointNative { X = screenX, Y = screenY };
        var parent = GetParent(window);
        if (parent != 0) ScreenToClient(parent, ref point);
        SetWindowPos(window, 0, point.X, point.Y, width, height, 0x0010);
    }
    public static ImageSource? LoadIcon(string path)
    {
        var result = SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100);
        if (result == 0 || info.Icon == 0) return null;
        try { var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); image.Freeze(); return image; }
        finally { DestroyIcon(info.Icon); }
    }
}
