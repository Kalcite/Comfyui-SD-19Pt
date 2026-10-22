using System.Text.Encodings.Web;
using System.Text.Json;
using ComfyLauncher.Models;

namespace ComfyLauncher.Services;

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AppSettings Load(string root)
    {
        var path = ProjectPaths.ConfigFile(root);
        AppSettings? settings = null;
        try
        {
            if (File.Exists(path))
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path, System.Text.Encoding.UTF8), JsonOptions);
            }
        }
        catch
        {
            settings = null;
        }

        settings ??= new AppSettings();
        settings.ProjectRoot = root;

        var defaults = new (string Current, string Default, Action<string> Set)[]
        {
            (settings.ComfyPath, ProjectPaths.DefaultComfy(root), v => settings.ComfyPath = v),
            (settings.PythonPath, ProjectPaths.DefaultPython(root), v => settings.PythonPath = v),
            (settings.VenvPath, ProjectPaths.DefaultVenv(root), v => settings.VenvPath = v),
            (settings.UserDirectory, ProjectPaths.DefaultUser(root), v => settings.UserDirectory = v),
            (settings.OutputDirectory, ProjectPaths.DefaultOutput(root), v => settings.OutputDirectory = v),
            (settings.InputDirectory, ProjectPaths.DefaultInput(root), v => settings.InputDirectory = v),
        };

        foreach (var (current, fallback, set) in defaults)
        {
            if (string.IsNullOrWhiteSpace(current) || (!Directory.Exists(current) && Directory.Exists(fallback)))
            {
                set(fallback);
            }
        }

        EnsureDirectories(root);
        return settings;
    }

    public static void EnsureDirectories(string root)
    {
        foreach (var dir in new[]
                 {
                     Path.Combine(root, "config"), ProjectPaths.LogsDir(root), ProjectPaths.TempDir(root),
                     ProjectPaths.DefaultUser(root), ProjectPaths.DefaultOutput(root), ProjectPaths.DefaultInput(root)
                 })
        {
            try { Directory.CreateDirectory(dir); } catch { /* 启动器随后会提示 */ }
        }
    }

    public static string Save(AppSettings settings)
    {
        var path = ProjectPaths.ConfigFile(settings.ProjectRoot);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions), new System.Text.UTF8Encoding(false));
            File.Move(tmp, path, true);
            return path;
        }
        catch (Exception ex)
        {
            return "保存失败: " + ex.Message;
        }
    }
}
