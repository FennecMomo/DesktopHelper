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
    [DllImport("user32.dll")] internal static extern nint GetParent(nint window);
    [DllImport("user32.dll")] static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint window, out RectNative rect);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] static extern nint GetShellWindow();
    [DllImport("user32.dll")] static extern bool RedrawWindow(nint window, nint rect, nint region, uint flags);
    [DllImport("user32.dll")] static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint window, StringBuilder name, int length);
    [DllImport("user32.dll")] static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern nint SHGetFileInfo(string path, uint attrs, out ShellFileInfo info, uint size, uint flags);
    [StructLayout(LayoutKind.Sequential)] internal struct RectNative { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ShellFileInfo
    {
        public nint Icon; public int IconIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    public static string ClassName(nint handle) { var text = new StringBuilder(256); GetClassName(handle, text, text.Capacity); return text.ToString(); }
    internal static bool IsLayered(nint window) => (GetWindowLong(window, -20) & 0x80000) != 0;
    internal static bool IsClickThrough(nint window) => (GetWindowLong(window, -20) & 0x08000020) == 0x08000020;
    internal static bool IsChild(nint window) => (GetWindowLong(window, -16) & 0x40000000) != 0;
    internal static bool IsTopmost(nint window) => (GetWindowLong(window, -20) & 0x8) != 0;
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
    public static void ConfigureStandalone(nint window)
    {
        // Never reparent WPF into Explorer. Keep our own layered top-level HWND,
        // like DesktopTools, but use the normal (not topmost) window band.
        SetWindowLong(window, -20, (GetWindowLong(window, -20) | 0x80) & ~0x40000);
        SetWindowPos(window, new nint(-2), 0, 0, 0, 0, 0x0033); // HWND_NOTOPMOST
    }
    public static bool DesktopIsForeground()
    {
        var foreground = GetForegroundWindow();
        return foreground == GetShellWindow() || ClassName(foreground) is "Progman" or "WorkerW";
    }
    public static void MaintainDesktopLayer(nint window)
    {
        if (DesktopIsForeground())
        {
            if (IsIconic(window) || !IsWindowVisible(window)) ShowWindow(window, 4); // SW_SHOWNOACTIVATE
            SetWindowPos(window, 0, 0, 0, 0, 0, 0x0013); // normal HWND_TOP, not topmost
        }
        else
        {
            SetWindowPos(window, new nint(1), 0, 0, 0, 0, 0x0013); // HWND_BOTTOM
        }
    }
    public static bool IsBelowForeground(nint window)
    {
        var foreground = GetForegroundWindow();
        if (DesktopIsForeground() || foreground == window) return true;
        for (var current = GetWindow(window, 3); current != 0; current = GetWindow(current, 3))
            if (current == foreground) return true;
        return false;
    }
    public static void RefreshDesktop() { var desktop = GetShellWindow(); if (desktop != 0) RedrawWindow(desktop, 0, 0, 0x185); }
    public static void SetClickThrough(nint window, bool enabled, bool desktopMode = true)
    {
        // Same flags as DesktopTools' layered-window lock; WPF supplies WS_EX_LAYERED.
        const int flags = 0x20 | 0x08000000;
        var style = GetWindowLong(window, -20) & ~flags;
        if (enabled) style |= 0x20;
        if (enabled || desktopMode) style |= 0x08000000;
        SetWindowLong(window, -20, style);
        SetWindowPos(window, 0, 0, 0, 0, 0, 0x0037);
    }
    public static void Place(nint window, int screenX, int screenY, int width, int height)
    {
        SetWindowPos(window, 0, screenX, screenY, width, height, 0x0014); // keep current z-order
    }
    public static ImageSource? LoadIcon(string path)
    {
        var result = SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100);
        if (result == 0 || info.Icon == 0) return null;
        try { var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); image.Freeze(); return image; }
        finally { DestroyIcon(info.Icon); }
    }
}
