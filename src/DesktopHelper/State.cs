using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
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
    private string? expectedHash;
    private bool loaded;
    private bool recoveryBlocked;
    public string? Revision => expectedHash;
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    private void ObserveFile() { expectedHash = File.Exists(FilePath) ? Hash(File.ReadAllText(FilePath)) : null; loaded = true; }
    public DesktopState Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        using var gate = new FileStream(Path.Combine(DirectoryPath, "layout.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        ObserveFile();
        if (expectedHash is null && !File.Exists(FilePath + ".bak") && !Directory.Exists(Path.Combine(DirectoryPath, "history"))) return new();
        try { return Read(FilePath); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".damaged-" + Guid.NewGuid().ToString("N"), false);
            Warning = "配置读取失败，原文件已保留备份。";
            if (File.Exists(FilePath + ".bak"))
                try { return Read(FilePath + ".bak"); } catch (Exception backupError) when (backupError is IOException or JsonException or InvalidDataException) { }
            var history = Path.Combine(DirectoryPath, "history");
            if (Directory.Exists(history))
                foreach (var snapshot in Directory.GetFiles(history, "*.json").OrderByDescending(p => p))
                    try { return Read(snapshot); } catch (Exception backupError) when (backupError is IOException or JsonException or InvalidDataException) { }
            recoveryBlocked = true;
            throw new InvalidDataException("布局无法读取，已停止加载以避免用空布局覆盖原数据。请从配置目录的 history 或 recovery 中恢复。");
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
        if (recoveryBlocked) throw new IOException("配置恢复尚未完成，禁止覆盖原布局。");
        using var gate = new FileStream(Path.Combine(DirectoryPath, "layout.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previousText = File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
        var actualHash = previousText is null ? null : Hash(previousText);
        if ((!loaded && actualHash is not null) || (loaded && actualHash != expectedHash))
            throw new IOException("配置已被另一个实例或外部程序修改，本次保存已阻止。请重新打开以读取最新布局。");
        var nextText = JsonSerializer.Serialize(state, options);
        if (nextText == previousText)
        {
            if (!Directory.Exists(Path.Combine(DirectoryPath, "history"))) Archive(nextText);
            return;
        }
        DesktopState? previousState = null;
        if (previousText is not null)
            try { previousState = JsonSerializer.Deserialize<DesktopState>(previousText, options); } catch (JsonException) { }
        // Keep content revisions indefinitely; geometry-only saves cannot erase them.
        if (!Directory.Exists(Path.Combine(DirectoryPath, "history")) || previousState is null || JsonSerializer.Serialize(previousState.Groups, options) != JsonSerializer.Serialize(state.Groups, options))
        {
            if (previousState?.Groups is not null) Archive(previousText!);
            Archive(nextText);
        }
        var temp = FilePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(nextText); stream.Write(bytes); stream.Flush(true);
        }
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
        else File.Move(temp, FilePath);
        expectedHash = Hash(nextText); loaded = true;
        if (Hash(File.ReadAllText(FilePath)) != expectedHash) throw new IOException("保存后的配置校验失败。");
    }
    private void Archive(string text)
    {
        var history = Path.Combine(DirectoryPath, "history"); Directory.CreateDirectory(history);
        var hash = Hash(text);
        if (Directory.EnumerateFiles(history, "*-" + hash + ".json").Any()) return;
        File.WriteAllText(Path.Combine(history, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + hash + ".json"), text);
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
