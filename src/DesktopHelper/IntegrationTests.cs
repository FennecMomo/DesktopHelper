using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace DesktopHelper;

internal static class IntegrationTests
{
    public static async Task RunAsync(App app)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "integration-results");
        Directory.CreateDirectory(root);
        var results = new List<string>(); MainWindow? window = null;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); results.Add("PASS " + name); }
        async Task Settle() { await Task.Delay(350); await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        try
        {
            var store = new StateStore(Path.Combine(root, "state-" + Guid.NewGuid().ToString("N")));
            var data = new DesktopState { X = 350, Y = 160, Width = 680, Height = 530, Groups = [new() { Name = "常用应用" }, new() { Name = "工作空间" }, new() { Name = "资料与项目" }] };
            var shortcut = Path.Combine(root, "计算器.lnk");
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic link = shell.CreateShortcut(shortcut);
            link.TargetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "calc.exe"); link.Save();
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            Check(File.Exists(shortcut), "real calculator shortcut fixture exists");
            StateStore.AddFiles(data.Groups[0], [shortcut, Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)]);
            StateStore.AddFiles(data.Groups[1], [Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)]);
            store.Save(data);
            window = new MainWindow(store); app.MainWindow = window; window.Show(); await Settle();
            Check(!DesktopHost.IsChild(window.NativeHandle), "standalone top-level HWND; never reparented into Explorer");
            Check(DesktopHost.GetParent(window.NativeHandle) != DesktopHost.FindDesktop(), "Explorer is not the window owner or parent");
            Check(!DesktopHost.IsTopmost(window.NativeHandle), "desktop panel is not topmost");
            Check(DesktopHost.IsBelowForeground(window.NativeHandle), "panel sits below the current foreground application");
            Check(DesktopHost.IsLayered(window.NativeHandle), "native WS_EX_LAYERED enabled (transparent rendering)");
            Check(DesktopHost.IsWindowVisible(window.NativeHandle), "native window visible");
            DesktopHost.GetWindowRect(window.NativeHandle, out var beforeMove);
            window.TestMove(24, 16);
            DesktopHost.GetWindowRect(window.NativeHandle, out var afterMove);
            Check(afterMove.Left > beforeMove.Left && afterMove.Top > beforeMove.Top, "title drag handler moves actual desktop HWND");
            window.CaptureRender(Path.Combine(root, "wide.png"));
            using (var imageFile = File.OpenRead(Path.Combine(root, "wide.png")))
            {
                var bitmap = System.Windows.Media.Imaging.BitmapDecoder.Create(imageFile, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                Check(pixels[(450 * bitmap.PixelWidth + 500) * 4 + 3] == 0, "empty desktop area renders zero alpha instead of black");
                Check(pixels[(30 * bitmap.PixelWidth + 40) * 4 + 3] > 0, "title drag surface remains native-hit-testable with transparent background");
            }
            var second = VisualTreeHelper.GetOffset(window.GroupPanel.Children[1]);
            Check(second.X > 0 && second.Y == 0, "wide groups arranged side by side");
            window.TestResize(360, 380); await Settle();
            window.CaptureRender(Path.Combine(root, "narrow.png"));
            second = VisualTreeHelper.GetOffset(window.GroupPanel.Children[1]);
            Check(second.X == 0 && second.Y > 0, "narrow groups wrap vertically");
            var firstGroup = (Border)window.GroupPanel.Children[0]; var body = (StackPanel)firstGroup.Child; var items = (WrapPanel)body.Children[1];
            Check(VisualTreeHelper.GetOffset(items.Children[3]).Y > 0, "real item controls wrap within group");
            Check(window.GroupPanel.ActualHeight > window.ActualHeight - 120, "content exceeds fixed frame and scrolls");
            window.TestClickThrough(true);
            Check(DesktopHost.IsClickThrough(window.NativeHandle), "DesktopTools click-through and no-activate flags enabled");
            window.TestClickThrough(false);
            Check(!DesktopHost.IsClickThrough(window.NativeHandle), "click-through can be disabled again");
            window.TestLaunchFirst(); await Task.Delay(2000);
            Check(Process.GetProcesses().Any(p => { try { return p.ProcessName.Equals("CalculatorApp", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("Calculator", StringComparison.OrdinalIgnoreCase); } catch { return false; } }), "real .lnk launch produced calculator process");
            var saved = store.Load(); window.PrepareClose(); window.Close();
            window = new MainWindow(store); app.MainWindow = window; window.Show(); await Settle();
            DesktopHost.GetWindowRect(window.NativeHandle, out var rect);
            Check(window.Width == 360 && window.Height == 380 && Math.Abs(rect.Left - saved.X) <= 1 && Math.Abs(rect.Top - saved.Y) <= 1, "close/reopen preserves size and physical position at current DPI");
            Check(store.Load().Groups[0].Items[0].Path == shortcut, "close/reopen preserves original .lnk target");
            window.TestResize(680, 530); await Settle(); window.CaptureRender(Path.Combine(root, "restored.png"));
            results.Add("ALL " + results.Count + " INTEGRATION CHECKS PASSED");
            File.WriteAllLines(Path.Combine(root, "results.txt"), results);
            window.PrepareClose(); window.Close(); app.Shutdown(0);
        }
        catch (Exception ex)
        {
            results.Add("FAIL " + ex); File.WriteAllLines(Path.Combine(root, "results.txt"), results);
            window?.PrepareClose(); window?.Close(); app.Shutdown(1);
        }
    }
}
