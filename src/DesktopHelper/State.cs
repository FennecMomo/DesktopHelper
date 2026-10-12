using System.IO;
using System.Text.Json;
namespace DesktopHelper;
public sealed class ShortcutItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}
public sealed class ShortcutGroup
{
    public string Name { get; set; } = "新分组";
    public List<ShortcutItem> Items { get; set; } = [];
}
public sealed class DesktopState
{
    public int Version { get; set; } = 1;
    public double X { get; set; } = 120;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 680;
    public double Height { get; set; } = 530;
    public bool Locked { get; set; }
    public bool NoBackground { get; set; } = true;
    public bool ClickThrough { get; set; }
    public List<ShortcutGroup> Groups { get; set; } = [new() { Name = "常用应用" }, new() { Name = "工作空间" }];
}
public sealed class StateStore(string directory)
{
    public string DirectoryPath { get; } = directory;
    public string FilePath => Path.Combine(DirectoryPath, "layout.json");
    public string? Warning { get; private set; }
    private readonly JsonSerializerOptions options = new() { WriteIndented = true };
    public DesktopState Load()
    {
        if (!File.Exists(FilePath)) return new();
        try { return Read(FilePath); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            File.Copy(FilePath, FilePath + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"), false);
            Warning = "配置读取失败，原文件已保留备份。";
            if (File.Exists(FilePath + ".bak"))
                try { return Read(FilePath + ".bak"); } catch (Exception backupError) when (backupError is IOException or JsonException or InvalidDataException) { }
            return new();
        }
    }
    private DesktopState Read(string path)
    {
        var value = JsonSerializer.Deserialize<DesktopState>(File.ReadAllText(path), options) ?? throw new InvalidDataException();
        if (value.Version != 1) throw new NotSupportedException("此布局由其他版本创建，请使用对应的新版本打开；原文件未修改。");
        if (value.Groups is null || value.Groups.Any(g => g is null || g.Name is null || g.Items is null || g.Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Path) || i.Name is null)))
            throw new InvalidDataException("配置格式无效");
        if (!double.IsFinite(value.Width) || !double.IsFinite(value.Height) || !double.IsFinite(value.X) || !double.IsFinite(value.Y)) throw new InvalidDataException();
        value.Width = Math.Clamp(value.Width, 320, 4000); value.Height = Math.Clamp(value.Height, 240, 3000);
        return value;
    }
    public void Save(DesktopState state)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, options));
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
        else File.Move(temp, FilePath);
    }
    public static int AddFiles(ShortcutGroup group, IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) continue;
            var full = Path.GetFullPath(path);
            if (group.Items.Any(i => string.Equals(i.Path, full, StringComparison.OrdinalIgnoreCase))) continue;
            group.Items.Add(new() { Name = Directory.Exists(full) ? new DirectoryInfo(full).Name : Path.GetFileNameWithoutExtension(full), Path = full });
            added++;
        }
        return added;
    }
}
