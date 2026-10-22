using System.Diagnostics;
using System.Text;
using ComfyLauncher.Models;

namespace ComfyLauncher.Services;

/// <summary>运行项目 scripts 目录下的 PowerShell 脚本，并把输出写入内嵌控制台。</summary>
public sealed class ScriptRunner
{
    public event Action<LogLine>? Log;

    public string PowerShellPath
    {
        get
        {
            var pwsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
            if (File.Exists(pwsh)) return pwsh;
            var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(sys)) return sys;
            return "powershell.exe";
        }
    }

    public async Task<int> RunAsync(string scriptPath, string arguments, string root, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = PowerShellPath,
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File {ProcessUtil.Quote(scriptPath)} {arguments}",
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.Environment["TMP"] = ProjectPaths.TempDir(root);
        psi.Environment["TEMP"] = ProjectPaths.TempDir(root);
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PIP_DISABLE_PIP_VERSION_CHECK"] = "1";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Log?.Invoke(new LogLine(LogKind.Out, e.Data));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Log?.Invoke(new LogLine(LogKind.Err, e.Data));
        };

        Log?.Invoke(new LogLine(LogKind.System, $"执行脚本: {Path.GetFileName(scriptPath)} {arguments}"));
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
