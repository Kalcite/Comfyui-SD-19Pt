using System.Diagnostics;
using System.Text;
using ComfyLauncher.Models;

namespace ComfyLauncher.Services;

/// <summary>负责启动、停止 ComfyUI 子进程并转发输出。</summary>
public sealed class ComfyRunner : IDisposable
{
    private Process? _process;
    private bool _disposed;

    public event Action<LogLine>? Log;
    public event Action<int>? Exited;

    public int? Pid
    {
        get
        {
            try { return _process?.Id; } catch { return null; }
        }
    }

    public bool IsRunning
    {
        get
        {
            try { return _process is { HasExited: false }; } catch { return false; }
        }
    }

    public void Start(ProcessStartInfo psi, string displayCommand)
    {
        if (IsRunning) throw new InvalidOperationException("ComfyUI 已在运行。");

        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        psi.StandardInputEncoding = Encoding.UTF8;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Log?.Invoke(new LogLine(LogKind.Out, e.Data));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            var kind = e.Data.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                       e.Data.Contains("Traceback", StringComparison.OrdinalIgnoreCase) ||
                       e.Data.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                ? LogKind.Err
                : LogKind.Out;
            Log?.Invoke(new LogLine(kind, e.Data));
        };
        process.Exited += (_, _) =>
        {
            int code;
            try { code = process.ExitCode; } catch { code = -1; }
            if (ReferenceEquals(_process, process)) _process = null;
            Exited?.Invoke(code);
        };

        Log?.Invoke(new LogLine(LogKind.System, "执行: " + displayCommand));
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
        Log?.Invoke(new LogLine(LogKind.System, $"进程已启动 (PID {process.Id})"));
    }

    public void Send(string text)
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.StandardInput.WriteLine(text);
                _process.StandardInput.Flush();
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke(new LogLine(LogKind.Err, "写入 stdin 失败: " + ex.Message));
        }
    }

    public async Task StopAsync()
    {
        var process = _process;
        _process = null;
        if (process == null) return;

        try
        {
            if (!process.HasExited)
            {
                Log?.Invoke(new LogLine(LogKind.System, "正在结束 ComfyUI 进程树 ..."));
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        catch (TimeoutException)
        {
            Log?.Invoke(new LogLine(LogKind.Warn, "进程未在 10 秒内退出，可能仍有残留子进程。"));
        }
        catch (Exception ex)
        {
            Log?.Invoke(new LogLine(LogKind.Err, "停止进程失败: " + ex.Message));
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch { }
        _process?.Dispose();
    }
}
