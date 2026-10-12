using System.IO;
using System.Windows;
using System.Windows.Controls;
using Size = System.Windows.Size;
namespace DesktopHelper;
internal static class SelfTests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "DesktopHelper-tests-" + Guid.NewGuid().ToString("N"));
        var results = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); results.Add("PASS " + name); }
        try
        {
            Directory.CreateDirectory(root);
            var file = Path.Combine(root, "快捷方式.url"); File.WriteAllText(file, "[InternetShortcut]\nURL=https://example.com");
            var group = new ShortcutGroup { Name = "测试分组" };
            Check(StateStore.AddFiles(group, [file, file.ToUpperInvariant(), root, Path.Combine(root, "missing")]) == 2, "files/folders, duplicate and missing-path handling");
            var store = new StateStore(root);
            var state = new DesktopState { Width = 777, Height = 444, X = -850, Y = 42, Locked = true, Groups = [group] };
            store.Save(state); var read = store.Load();
            Check(read.Width == 777 && read.X == -850 && read.Locked && read.Groups[0].Items[0].Path == file, "Unicode paths and geometry round-trip");
            state.Width = 555; store.Save(state);
            Check(File.Exists(store.FilePath + ".bak") && store.Load().Width == 555, "atomic replacement and previous-version backup");
            File.WriteAllText(store.FilePath, "{broken"); read = store.Load();
            Check(read.Width == 777 && Directory.GetFiles(root, "*.damaged-*").Length == 1, "corrupt config preserved and restored from backup");
            const string newer = "{\"Version\":99}";
            File.WriteAllText(store.FilePath, newer);
            var refused = false;
            try { store.Load(); } catch (NotSupportedException) { refused = true; }
            Check(refused && File.ReadAllText(store.FilePath) == newer, "future-version config is never overwritten");
            foreach (var available in new[] { 280d, 600d, 640d, 1000d })
            {
                var width = MainWindow.GroupWidth(available);
                var wrap = new WrapPanel { Width = available };
                for (var i = 0; i < 4; i++) wrap.Children.Add(new Border { Width = width, Height = 120, Margin = new(0, 0, 10, 10) });
                wrap.Measure(new Size(available, double.PositiveInfinity)); wrap.Arrange(new Rect(0, 0, available, wrap.DesiredSize.Height));
                var second = System.Windows.Media.VisualTreeHelper.GetOffset(wrap.Children[1]);
                Check(available < 620 ? second.Y > 0 : second.X > 0, "group responsive arrangement at " + available);
            }
            var items = new WrapPanel { Width = 250 };
            for (var i = 0; i < 5; i++) items.Children.Add(new Border { Width = 82, Height = 95 });
            items.Measure(new Size(250, double.PositiveInfinity)); items.Arrange(new Rect(0, 0, 250, items.DesiredSize.Height));
            Check(System.Windows.Media.VisualTreeHelper.GetOffset(items.Children[3]).Y == 95, "shortcut items wrap to next row");
            results.Add("ALL " + results.Count + " CHECKS PASSED");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), results);
            return 0;
        }
        catch (Exception ex) { results.Add("FAIL " + ex); File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), results); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
