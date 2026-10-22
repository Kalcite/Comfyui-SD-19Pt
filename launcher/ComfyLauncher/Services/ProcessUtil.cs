using System.Diagnostics;
using System.Text;

namespace ComfyLauncher.Services;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

public static class ProcessUtil
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        IDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (environment != null)
        {
            foreach (var (key, value) in environment) psi.Environment[key] = value;
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, "", ex.Message, false);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var timeoutTask = timeout.HasValue ? Task.Delay(timeout.Value, cancellationToken) : Task.Delay(Timeout.Infinite, cancellationToken);
        var exitTask = process.WaitForExitAsync(cancellationToken);

        var completed = await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false);
        var timedOut = completed != exitTask;
        if (timedOut)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        var stdout = await SafeRead(stdoutTask);
        var stderr = await SafeRead(stderrTask);
        return new ProcessResult(timedOut ? -2 : process.ExitCode, stdout, stderr, timedOut);
    }

    private static async Task<string> SafeRead(Task<string> task)
    {
        try { return await task.ConfigureAwait(false); } catch { return ""; }
    }

    public static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (!value.Any(char.IsWhiteSpace) && !value.Contains('"')) return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    public static async Task<bool> IsPortFreeAsync(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync("127.0.0.1", port).WaitAsync(TimeSpan.FromMilliseconds(400));
            return false;
        }
        catch
        {
            return true;
        }
    }

    public static async Task<bool> TcpAliveAsync(string host, int port, int timeoutMs = 1500)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> HttpReadyAsync(string url, int timeoutMs = 1500)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var response = await http.GetAsync(url);
            return (int)response.StatusCode is >= 200 and < 500;
        }
        catch
        {
            return false;
        }
    }
}
