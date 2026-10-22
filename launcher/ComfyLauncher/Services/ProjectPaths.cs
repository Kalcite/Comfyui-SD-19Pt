using System.Diagnostics;
using System.Text;
using ComfyLauncher.Models;

namespace ComfyLauncher.Services;

public static class ProjectPaths
{
    public static string FindRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ComfyUI", "main.py")) ||
                    File.Exists(Path.Combine(dir.FullName, "runtime", "python312", "python.exe")))
                {
                    return dir.FullName;
                }
            }
        }

        return Directory.GetCurrentDirectory();
    }

    public static string DefaultComfy(string root) => Path.Combine(root, "ComfyUI");
    public static string DefaultPython(string root) => Path.Combine(root, "runtime", "python312", "python.exe");
    public static string DefaultVenv(string root) => Path.Combine(root, ".venv");
    public static string VenvPython(string venv) => Path.Combine(venv, "Scripts", "python.exe");
    public static string DefaultUser(string root) => Path.Combine(root, "user");
    public static string DefaultOutput(string root) => Path.Combine(root, "output");
    public static string DefaultInput(string root) => Path.Combine(root, "input");
    public static string ConfigFile(string root) => Path.Combine(root, "config", "launcher.json");
    public static string LogsDir(string root) => Path.Combine(root, "logs");
    public static string TempDir(string root) => Path.Combine(root, "temp");

    public static string ResolveComfy(string root, string configured)
        => !string.IsNullOrWhiteSpace(configured) ? configured : DefaultComfy(root);

    public static string ResolvePython(string root, string configured)
        => !string.IsNullOrWhiteSpace(configured) ? configured : DefaultPython(root);

    public static string ResolveVenv(string root, string configured)
        => !string.IsNullOrWhiteSpace(configured) ? configured : DefaultVenv(root);

    public static string ResolveVenvPython(string root, string configured)
        => VenvPython(ResolveVenv(root, configured));

    public static string ResolveUser(string root, string configured)
        => !string.IsNullOrWhiteSpace(configured) ? configured : DefaultUser(root);

    public static string ResolveOutput(string root, string configured)
        => !string.IsNullOrWhiteSpace(configured) ? configured : DefaultOutput(root);

    public static string ResolveInput(string root, string configured)
        => !string.IsNullOrWhiteSpace(configured) ? configured : DefaultInput(root);
}
