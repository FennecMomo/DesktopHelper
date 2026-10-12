using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using MenuItem = System.Windows.Controls.MenuItem;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace DesktopHelper;
public sealed class MainWindow : Window
{
    private readonly StateStore store;
    private readonly DesktopState state;
    private readonly WrapPanel groups = new();
    private readonly TextBlock status = new() { FontSize = 11, Foreground = Brush("#9BAABD"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 0, 12, 0) };
    private readonly TextBlock mode = new() { Text = "桌面空间", FontSize = 12, Foreground = Brush("#71DCC4") };
    private readonly ScrollViewer scroll;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer hostTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, ImageSource?> icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.NotifyIcon tray;
    private nint handle;
    private bool attached;
    private bool quitting;
    private bool ready;
    private bool arranging;
    private readonly Border frame;
    private readonly Thumb moveThumb;
    private readonly List<Thumb> resizeThumbs = [];
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    public MainWindow(StateStore store, bool arrange = false)
    {
        this.store = store;
        arranging = arrange;
        state = store.Load();
        Title = "DesktopHelper · 桌面空间";
        Width = state.Width; Height = state.Height; MinWidth = 320; MinHeight = 240;
        Left = state.X; Top = state.Y;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        ShowInTaskbar = false; Background = System.Windows.Media.Brushes.Transparent; FontFamily = new("Microsoft YaHei UI"); FontSize = 12;
        var root = new Grid(); Content = root;
        frame = new Border { BorderBrush = Brush("#354458"), BorderThickness = new(1), CornerRadius = new(14), Padding = new(18, 14, 18, 10) };
        root.Children.Add(frame);
        var layout = new Grid(); frame.Child = layout;
        layout.RowDefinitions.Add(new() { Height = new(66) });
        layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = new(32) });
        var header = new Grid(); layout.Children.Add(header);
        // A layered window's fully transparent pixels do not receive native mouse input.
        // Keep a visible drag surface even when the main background is transparent.
        moveThumb = new Thumb { Cursor = System.Windows.Input.Cursors.SizeAll, Template = ThumbTemplate("#A01D2938"), Height = 54, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Stretch };
        header.Children.Add(moveThumb);
        var heading = new StackPanel { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
        heading.Children.Add(mode);
        heading.Children.Add(new TextBlock { Text = "桌面空间", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new(0, 5, 0, 0) });
        header.Children.Add(heading);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var add = MakeButton("＋ 分组", NewGroup); add.ToolTip = "创建一个新的快捷方式分组"; controls.Children.Add(add);
        var menu = MakeButton("⋯", ShowMenu); menu.Margin = new(6, 0, 0, 0); menu.ToolTip = "设置与退出"; controls.Children.Add(menu); header.Children.Add(controls);
        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = groups, Padding = new(0, 0, 3, 0) };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        Grid.SetRow(status, 2); layout.Children.Add(status);
        AddResize(root, HorizontalAlignment.Right, VerticalAlignment.Stretch, 7, double.NaN, System.Windows.Input.Cursors.SizeWE, true, false);
        AddResize(root, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, 7, System.Windows.Input.Cursors.SizeNS, false, true);
        AddResize(root, HorizontalAlignment.Right, VerticalAlignment.Bottom, 18, 18, System.Windows.Input.Cursors.SizeNWSE, true, true);
        var grip = new TextBlock { Text = "◢", Foreground = Brush("#688199"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 3, 1), IsHitTestVisible = false }; root.Children.Add(grip);
        moveThumb.DragDelta += (_, e) =>
        {
            if (state.Locked) return;
            DesktopHost.GetWindowRect(handle, out var rect);
            var dpi = VisualTreeHelper.GetDpi(this);
            DesktopHost.Place(handle, rect.Left + (int)Math.Round(e.HorizontalChange * dpi.DpiScaleX), rect.Top + (int)Math.Round(e.VerticalChange * dpi.DpiScaleY), rect.Right - rect.Left, rect.Bottom - rect.Top);
        };
        moveThumb.DragCompleted += (_, _) => { EnsureVisible(); QueueSave(); };
        scroll.SizeChanged += (_, _) => Reflow();
        SizeChanged += (_, _) => QueueSave();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); };
        hostTimer.Tick += (_, _) => { if (!arranging && (!attached || !DesktopHost.IsWindow(DesktopHost.GetParent(handle)))) Attach(); };
        tray = new() { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Text = "DesktopHelper · 桌面空间", Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Reveal);
        var trayMenu = new System.Windows.Forms.ContextMenuStrip();
        trayMenu.Items.Add("显示桌面区域", null, (_, _) => Dispatcher.Invoke(Reveal));
        trayMenu.Items.Add("整理快捷方式", null, (_, _) => Dispatcher.Invoke(Arrange));
        trayMenu.Items.Add("关闭鼠标穿透", null, (_, _) => Dispatcher.Invoke(() => { state.ClickThrough = false; UpdateAppearance(); Save(); }));
        trayMenu.Items.Add("隐藏桌面区域", null, (_, _) => Dispatcher.Invoke(Hide));
        trayMenu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Quit)); tray.ContextMenuStrip = trayMenu;
        SourceInitialized += (_, _) => { handle = new WindowInteropHelper(this).Handle; if (!arranging) Attach(); else { ShowInTaskbar = true; mode.Text = "整理模式"; } };
        Loaded += (_, _) =>
        {
            ready = true;
            var dpi = VisualTreeHelper.GetDpi(this);
            DesktopHost.Place(handle, (int)state.X, (int)state.Y, (int)Math.Round(Width * dpi.DpiScaleX), (int)Math.Round(Height * dpi.DpiScaleY));
            EnsureVisible(); UpdateLock(); UpdateAppearance(); RenderGroups(); hostTimer.Start(); if (store.Warning is not null) status.Text = store.Warning; Save();
        };
        Closing += (_, e) => { if (!quitting) { e.Cancel = true; Hide(); } };
    }
    private static ControlTemplate ThumbTemplate(string color = "#28688098")
    {
        var factory = new FrameworkElementFactory(typeof(Border)); factory.SetValue(Border.BackgroundProperty, Brush(color)); factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        return new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
    }
    private void AddResize(Grid root, HorizontalAlignment h, VerticalAlignment v, double w, double height, System.Windows.Input.Cursor cursor, bool x, bool y)
    {
        var thumb = new Thumb { HorizontalAlignment = h, VerticalAlignment = v, Width = w, Height = height, Cursor = cursor, Template = ThumbTemplate() };
        root.Children.Add(thumb); resizeThumbs.Add(thumb);
        thumb.DragDelta += (_, e) =>
        {
            if (state.Locked) return;
            var work = CurrentWorkArea(); var dpi = VisualTreeHelper.GetDpi(this);
            if (x) Width = Math.Clamp(ActualWidth + e.HorizontalChange, MinWidth, Math.Max(MinWidth, work.Width / dpi.DpiScaleX));
            if (y) Height = Math.Clamp(ActualHeight + e.VerticalChange, MinHeight, Math.Max(MinHeight, work.Height / dpi.DpiScaleY));
        };
        thumb.DragCompleted += (_, _) => { EnsureVisible(); QueueSave(); };
    }
    private void Attach()
    {
        attached = DesktopHost.Attach(handle);
        mode.Text = attached ? "DESKTOP HELPER" : "桌面连接中";
        ShowInTaskbar = !attached;
    }
    private System.Drawing.Rectangle CurrentWorkArea()
    {
        DesktopHost.GetWindowRect(handle, out var rect);
        return System.Windows.Forms.Screen.FromRectangle(new(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top))).WorkingArea;
    }
    private void EnsureVisible()
    {
        if (handle == 0) return;
        DesktopHost.GetWindowRect(handle, out var rect);
        var work = CurrentWorkArea(); var dpi = VisualTreeHelper.GetDpi(this);
        Width = Math.Min(Width, Math.Max(MinWidth, work.Width / dpi.DpiScaleX));
        Height = Math.Min(Height, Math.Max(MinHeight, work.Height / dpi.DpiScaleY));
        var w = (int)Math.Round(Width * dpi.DpiScaleX); var h = (int)Math.Round(Height * dpi.DpiScaleY);
        DesktopHost.Place(handle, Math.Clamp(rect.Left, work.Left, Math.Max(work.Left, work.Right - w)), Math.Clamp(rect.Top, work.Top, Math.Max(work.Top, work.Bottom - h)), w, h);
    }
    public void Reveal() { arranging = false; Show(); Attach(); EnsureVisible(); UpdateAppearance(); Save(); }
    private void Arrange()
    {
        DesktopHost.GetWindowRect(handle, out var rect);
        arranging = true; DesktopHost.Detach(handle); attached = false; ShowInTaskbar = true;
        Show(); DesktopHost.Place(handle, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        mode.Text = "整理模式"; UpdateAppearance(); Activate(); Save();
    }
    private void QueueSave() { if (!ready) return; saveTimer.Stop(); saveTimer.Start(); }
    private bool Save()
    {
        if (!ready) return true;
        DesktopHost.GetWindowRect(handle, out var rect);
        state.X = rect.Left; state.Y = rect.Top; state.Width = Width; state.Height = Height;
        try
        {
            store.Save(state);
            File.WriteAllText(Path.Combine(store.DirectoryPath, "status.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Version = "0.1.0", ProcessId = Environment.ProcessId, DesktopAttached = attached,
                ParentClass = DesktopHost.ClassName(DesktopHost.GetParent(handle)), ParentHandle = DesktopHost.GetParent(handle).ToInt64(),
                Visible = DesktopHost.IsWindowVisible(handle), X = rect.Left, Y = rect.Top, PixelWidth = rect.Right - rect.Left, PixelHeight = rect.Bottom - rect.Top,
                Width, Height, state.NoBackground, state.ClickThrough, Layered = DesktopHost.IsLayered(handle), GroupCount = state.Groups.Count, ItemCount = state.Groups.Sum(g => g.Items.Count)
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "保存失败：" + ex.Message; return false; }
    }
    internal void Quit()
    {
        if (PrepareClose()) System.Windows.Application.Current.Shutdown();
        else MessageBox.Show("布局保存失败，程序已保留运行。请检查配置目录权限或磁盘空间后重试退出。", "DesktopHelper", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    internal bool PrepareClose() { if (!Save()) return false; quitting = true; hostTimer.Stop(); saveTimer.Stop(); tray.Dispose(); return true; }
    private static Button MakeButton(string text, Action action)
    {
        var button = new Button { Content = text }; button.Click += (_, _) => action(); return button;
    }
    private static void Menu(System.Windows.Controls.ContextMenu menu, string label, Action action)
    {
        var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item);
    }
    private void ShowMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        Menu(menu, arranging ? "完成整理，返回桌面" : "整理快捷方式（临时窗口）", arranging ? Reveal : Arrange);
        Menu(menu, state.NoBackground ? "显示区域底色" : "透明区域底色（空白处穿透）", () => { state.NoBackground = !state.NoBackground; UpdateAppearance(); Save(); });
        Menu(menu, "开启整窗鼠标穿透（从托盘关闭）", () => { state.ClickThrough = true; UpdateAppearance(); Save(); });
        Menu(menu, state.Locked ? "解锁位置和大小" : "锁定位置和大小", () => { state.Locked = !state.Locked; UpdateLock(); Save(); });
        var auto = new MenuItem { Header = "开机启动", IsCheckable = true, IsChecked = IsAutoStart() };
        auto.Click += (_, _) =>
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (auto.IsChecked) key.SetValue("DesktopHelper", "\"" + Environment.ProcessPath + "\""); else key.DeleteValue("DesktopHelper", false);
            }
            catch (Exception ex) { Error("无法更改开机启动", ex); }
        };
        menu.Items.Add(auto);
        Menu(menu, "打开配置文件夹", () => LaunchPath(store.DirectoryPath));
        Menu(menu, "隐藏（托盘双击恢复）", Hide);
        Menu(menu, "退出", Quit);
        menu.IsOpen = true;
    }
    private static bool IsAutoStart() { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("DesktopHelper") is string; }
    private void UpdateLock()
    {
        moveThumb.IsEnabled = !state.Locked; foreach (var thumb in resizeThumbs) thumb.IsEnabled = !state.Locked;
        UpdateStatus();
    }
    private void UpdateAppearance()
    {
        frame.Background = state.NoBackground && !arranging ? System.Windows.Media.Brushes.Transparent : Brush("#EB151E2B");
        frame.BorderBrush = state.NoBackground && !arranging ? System.Windows.Media.Brushes.Transparent : Brush("#6B536B83");
        DesktopHost.SetClickThrough(handle, state.ClickThrough && !arranging);
    }
    private void UpdateStatus()
    {
        status.Text = state.Locked ? "位置已锁定 · 从右上角菜单解锁" : "拖入快捷方式 · 拖动标题移动 · 右下角缩放";
        status.ToolTip = status.Text;
    }
    private void NewGroup()
    {
        var name = Ask("新建分组", "给这一组起个名字", "新分组");
        if (name is null) return; state.Groups.Add(new() { Name = name }); RenderGroups(); Save();
    }
    private void RenderGroups()
    {
        groups.Children.Clear();
        foreach (var group in state.Groups)
        {
            var box = new Border { Background = Brush("#CE1D2938"), BorderBrush = Brush("#654C647B"), BorderThickness = new(1), CornerRadius = new(10), Padding = new(12), Margin = new(0, 0, 10, 10), AllowDrop = true };
            var body = new StackPanel(); box.Child = body;
            var bar = new DockPanel { Margin = new(0, 0, 0, 12) };
            var settings = MakeButton("⋯", () => GroupMenu(group)); settings.Padding = new(8, 1, 8, 1); settings.ToolTip = "分组设置：" + group.Name; DockPanel.SetDock(settings, Dock.Right); bar.Children.Add(settings);
            var add = MakeButton("＋", () => AddItems(group)); add.Padding = new(8, 1, 8, 1); add.ToolTip = "添加到「" + group.Name + "」"; DockPanel.SetDock(add, Dock.Right); bar.Children.Add(add);
            bar.Children.Add(new TextBlock { Text = group.Name + "  " + group.Items.Count, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = group.Name }); body.Children.Add(bar);
            var items = new WrapPanel(); body.Children.Add(items);
            if (group.Items.Count == 0)
                body.Children.Add(new TextBlock { Text = "拖入应用、快捷方式或文件夹\n也可以点击 ＋ 添加", Foreground = Brush("#8FA0B6"), FontSize = 12, LineHeight = 23, Margin = new(4, 15, 0, 22), TextWrapping = TextWrapping.Wrap });
            foreach (var item in group.Items) items.Children.Add(ItemButton(group, item));
            box.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) || e.Data.GetDataPresent(typeof(ShortcutDrag)) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None; e.Handled = true; };
            box.Drop += (_, e) =>
            {
                if (e.Data.GetData(typeof(ShortcutDrag)) is ShortcutDrag drag)
                {
                    if (drag.Group != group && !group.Items.Any(i => string.Equals(i.Path, drag.Item.Path, StringComparison.OrdinalIgnoreCase))) { drag.Group.Items.Remove(drag.Item); group.Items.Add(drag.Item); }
                }
                else if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files) StateStore.AddFiles(group, files);
                e.Handled = true; RenderGroups(); Save();
            };
            groups.Children.Add(box);
        }
        Reflow(); UpdateStatus();
    }
    private sealed record ShortcutDrag(ShortcutGroup Group, ShortcutItem Item);
    private Button ItemButton(ShortcutGroup group, ShortcutItem item)
    {
        var button = new Button { Width = 80, Height = 91, Padding = new(5), Margin = new(0, 0, 2, 4), Background = System.Windows.Media.Brushes.Transparent, ToolTip = item.Name + "\n" + item.Path };
        var content = new StackPanel();
        var image = new System.Windows.Controls.Image { Width = 32, Height = 32, Margin = new(0, 3, 0, 7) };
        content.Children.Add(image);
        content.Children.Add(new TextBlock { Text = item.Name, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 34, FontSize = 11 });
        button.Content = content;
        _ = SetIcon(image, item.Path);
        button.Click += (_, _) => LaunchPath(item.Path);
        var menu = new System.Windows.Controls.ContextMenu();
        Menu(menu, "打开", () => LaunchPath(item.Path));
        Menu(menu, "重命名", () => { var name = Ask("重命名快捷方式", "显示名称", item.Name); if (name is not null) { item.Name = name; RenderGroups(); Save(); } });
        var move = new MenuItem { Header = "移动到分组" };
        foreach (var target in state.Groups.Where(g => g != group))
        {
            var entry = new MenuItem { Header = target.Name };
            entry.Click += (_, _) => { if (!target.Items.Any(i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase))) { group.Items.Remove(item); target.Items.Add(item); RenderGroups(); Save(); } };
            move.Items.Add(entry);
        }
        move.IsEnabled = move.Items.Count > 0; menu.Items.Add(move);
        Menu(menu, "向前移动", () => { var index = group.Items.IndexOf(item); if (index > 0) { group.Items.RemoveAt(index); group.Items.Insert(index - 1, item); RenderGroups(); Save(); } });
        Menu(menu, "向后移动", () => { var index = group.Items.IndexOf(item); if (index < group.Items.Count - 1) { group.Items.RemoveAt(index); group.Items.Insert(index + 1, item); RenderGroups(); Save(); } });
        Menu(menu, "从分组移除（保留原文件）", () => { group.Items.Remove(item); RenderGroups(); Save(); });
        button.ContextMenu = menu;
        Point start = default; var dragging = false;
        button.PreviewMouseLeftButtonDown += (_, e) => { start = e.GetPosition(button); dragging = false; };
        button.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragging) return;
            var p = e.GetPosition(button); if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragging = true; System.Windows.DragDrop.DoDragDrop(button, new ShortcutDrag(group, item), System.Windows.DragDropEffects.Copy); e.Handled = true;
        };
        return button;
    }
    private async Task SetIcon(System.Windows.Controls.Image image, string path)
    {
        if (!icons.TryGetValue(path, out var source)) { source = await Task.Run(() => DesktopHost.LoadIcon(path)); icons[path] = source; }
        image.Source = source;
    }
    internal static double GroupWidth(double available)
    {
        var columns = Math.Max(1, (int)(available / 310)); return Math.Max(250, available / columns - 10);
    }
    private void Reflow()
    {
        var width = GroupWidth(Math.Max(260, scroll.ViewportWidth));
        foreach (FrameworkElement group in groups.Children) group.Width = width;
    }
    private void GroupMenu(ShortcutGroup group)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        Menu(menu, "重命名分组", () => { var name = Ask("重命名分组", "分组名称", group.Name); if (name is not null) { group.Name = name; RenderGroups(); Save(); } });
        Menu(menu, "添加文件夹", () => { var dialog = new Microsoft.Win32.OpenFolderDialog(); if (dialog.ShowDialog() == true) { StateStore.AddFiles(group, [dialog.FolderName]); RenderGroups(); Save(); } });
        Menu(menu, "向前移动", () => { var index = state.Groups.IndexOf(group); if (index > 0) { state.Groups.RemoveAt(index); state.Groups.Insert(index - 1, group); RenderGroups(); Save(); } });
        Menu(menu, "向后移动", () => { var index = state.Groups.IndexOf(group); if (index < state.Groups.Count - 1) { state.Groups.RemoveAt(index); state.Groups.Insert(index + 1, group); RenderGroups(); Save(); } });
        Menu(menu, "删除分组", () =>
        {
            if (group.Items.Count > 0 && MessageBox.Show("删除「" + group.Name + "」及其中的快捷入口？\n原应用和文件不会被删除。", "删除分组", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            state.Groups.Remove(group); RenderGroups(); Save();
        });
        menu.IsOpen = true;
    }
    private void AddItems(ShortcutGroup group)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "添加应用、快捷方式或文件", Multiselect = true, DereferenceLinks = false, Filter = "所有文件|*.*" };
        if (dialog.ShowDialog() == true) { StateStore.AddFiles(group, dialog.FileNames); RenderGroups(); Save(); }
    }
    private static string? Ask(string title, string label, string value)
    {
        var dialog = new Window { Title = title, Width = 380, Height = 205, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brush("#1D2938"), ShowInTaskbar = false, FontFamily = new("Microsoft YaHei UI"), Topmost = true };
        var panel = new StackPanel { Margin = new(22) }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = label, Margin = new(0, 0, 0, 10) });
        var input = new TextBox { Text = value, MaxLength = 80, Padding = new(7), FontSize = 14 }; panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) };
        var cancel = MakeButton("取消", () => dialog.DialogResult = false); cancel.IsCancel = true; buttons.Children.Add(cancel);
        var ok = MakeButton("保存", () => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; }); ok.IsDefault = true; ok.Margin = new(8, 0, 0, 0); buttons.Children.Add(ok); panel.Children.Add(buttons);
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
    private static void LaunchPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Error("无法打开，请检查原文件是否已移动或删除", ex); }
    }
    private static void Error(string title, Exception ex) => MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    internal void CaptureRender(string path)
    {
        UpdateLayout();
        var visual = (System.Windows.Media.Visual)Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
    internal void TestResize(double width, double height) { Width = width; Height = height; UpdateLayout(); Reflow(); UpdateLayout(); Save(); }
    internal void TestMove(double dx, double dy)
    {
        moveThumb.RaiseEvent(new DragDeltaEventArgs(dx, dy) { RoutedEvent = Thumb.DragDeltaEvent });
        moveThumb.RaiseEvent(new DragCompletedEventArgs(dx, dy, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Save();
    }
    internal void TestClickThrough(bool enabled) { state.ClickThrough = enabled; UpdateAppearance(); Save(); }
    internal nint NativeHandle => handle;
    internal WrapPanel GroupPanel => groups;
    internal void TestLaunchFirst() => LaunchPath(state.Groups[0].Items[0].Path);
}
