using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ComfyLauncher.Models;

namespace ComfyLauncher.Services;

public enum GitAttemptResult
{
    Success,
    Failed,
    NoResponse,
    TooSlow
}

/// <summary>
/// Git 克隆/更新，带“15 秒无响应或速率过低自动切换代理”的逻辑。
/// 直连成功时不使用任何代理；失败时若配置了回退代理且端口可达，则使用代理重试。
/// </summary>
public sealed class GitService
{
    private static readonly Regex SpeedRegex = new(@"([\d.]+)\s*(B|KiB|MiB|GiB)/s", RegexOptions.Compiled);
    private static readonly Regex PercentRegex = new(@"(\d{1,3})%", RegexOptions.Compiled);
    private static readonly Regex ProgressNoise = new(@"^(remote: )?(Enumerating|Counting|Compressing|Receiving|Resolving|Total)", RegexOptions.Compiled);

    public event Action<LogLine>? Log;
    public event Action<string>? Progress;

    public async Task<bool> CloneOrUpdateAsync(
        string repositoryUrl,
        string targetDirectory,
        string proxyUrl,
        bool useProxyFallback,
        int timeoutSeconds,
        int minSpeedKBps,
        CancellationToken cancellationToken = default)
    {
        var isRepo = Directory.Exists(Path.Combine(targetDirectory, ".git"));
        if (isRepo)
        {
            return await UpdateAsync(repositoryUrl, targetDirectory, proxyUrl, useProxyFallback, timeoutSeconds, minSpeedKBps, cancellationToken);
        }

        if (Directory.Exists(targetDirectory) && Directory.GetFileSystemEntries(targetDirectory).Length > 0)
        {
            Log?.Invoke(new LogLine(LogKind.Err,
                $"目标目录已存在且非空，但不是 Git 仓库：{targetDirectory}。请先移走或删除它。"));
            return false;
        }

        Log?.Invoke(new LogLine(LogKind.System, "开始直连克隆 ComfyUI（GitHub）..."));
        var direct = await CloneAttemptAsync("origin", repositoryUrl, targetDirectory, null, timeoutSeconds, minSpeedKBps, cancellationToken);
        if (direct == GitAttemptResult.Success) return true;

        if (!useProxyFallback)
        {
            Log?.Invoke(new LogLine(LogKind.Err, "直连克隆失败，且未启用代理回退。"));
            return false;
        }

        if (!TryNormalizeProxy(proxyUrl, out var proxy, out var host, out var port))
        {
            Log?.Invoke(new LogLine(LogKind.Err, $"代理地址无效：{proxyUrl}"));
            return false;
        }

        if (!await ProcessUtil.TcpAliveAsync(host, port))
        {
            Log?.Invoke(new LogLine(LogKind.Warn,
                $"检测到直连克隆{(direct == GitAttemptResult.TooSlow ? "速率过低" : "无响应/失败")}，但代理 {proxy} 端口不可达；请启动代理后重试。"));
            return false;
        }

        CleanupFailedClone(targetDirectory, existedBefore: false);
        Log?.Invoke(new LogLine(LogKind.System,
            $"直连结果：{Describe(direct)}，切换到代理 {proxy} 重试（限时 {timeoutSeconds}s，最低 {minSpeedKBps} KB/s）..."));
        var viaProxy = await CloneAttemptAsync("proxy", repositoryUrl, targetDirectory, proxy, timeoutSeconds, minSpeedKBps, cancellationToken);
        if (viaProxy == GitAttemptResult.Success) return true;

        Log?.Invoke(new LogLine(LogKind.Err, $"通过代理克隆仍失败：{Describe(viaProxy)}"));
        return false;
    }

    private async Task<GitAttemptResult> CloneAttemptAsync(
        string label,
        string url,
        string target,
        string? proxy,
        int timeoutSeconds,
        int minSpeedKBps,
        CancellationToken cancellationToken)
    {
        var existedBefore = Directory.Exists(target);
        var args = BuildGitOptions(proxy) +
                   $" clone --depth 1 --single-branch --progress {Q(url)} {Q(target)}";
        var result = await RunMonitoredAsync(
            label,
            args,
            Path.GetDirectoryName(target) ?? Environment.CurrentDirectory,
            timeoutSeconds,
            minSpeedKBps,
            "克隆 ComfyUI",
            cancellationToken);

        if (result.Result != GitAttemptResult.Success)
        {
            CleanupFailedClone(target, existedBefore);
        }

        return result.Result;
    }

    private async Task<bool> UpdateAsync(
        string url,
        string target,
        string proxyUrl,
        bool useProxyFallback,
        int timeoutSeconds,
        int minSpeedKBps,
        CancellationToken cancellationToken)
    {
        Log?.Invoke(new LogLine(LogKind.System, "检测到已有 ComfyUI 仓库，执行 git pull --ff-only ..."));
        var direct = await UpdateAttemptAsync(target, null, timeoutSeconds, minSpeedKBps, cancellationToken);
        if (direct == GitAttemptResult.Success) return true;

        if (!useProxyFallback || !TryNormalizeProxy(proxyUrl, out var proxy, out var host, out var port) ||
            !await ProcessUtil.TcpAliveAsync(host, port))
        {
            Log?.Invoke(new LogLine(LogKind.Warn, $"更新未成功（{Describe(direct)}）。如果本地改动较多，可手动处理。"));
            return false;
        }

        Log?.Invoke(new LogLine(LogKind.System, $"更新较慢（{Describe(direct)}），切换代理 {proxy} 重试 ..."));
        var viaProxy = await UpdateAttemptAsync(target, proxy, timeoutSeconds, minSpeedKBps, cancellationToken);
        return viaProxy == GitAttemptResult.Success;
    }

    private async Task<GitAttemptResult> UpdateAttemptAsync(
        string target,
        string? proxy,
        int timeoutSeconds,
        int minSpeedKBps,
        CancellationToken cancellationToken)
    {
        var args = BuildGitOptions(proxy) + $" -C {Q(target)} pull --ff-only --progress";
        var result = await RunMonitoredAsync("update", args, target, Math.Max(timeoutSeconds, 30),
            minSpeedKBps, "更新 ComfyUI", cancellationToken);
        return result.Result;
    }

    private async Task<(GitAttemptResult Result, int ExitCode)> RunMonitoredAsync(
        string label,
        string arguments,
        string workingDirectory,
        int timeoutSeconds,
        int minSpeedKBps,
        string phaseName,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        psi.Environment["GIT_ASKPASS"] = "echo";
        psi.Environment["LC_ALL"] = "C";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var lastOutput = DateTime.UtcNow;
        var start = DateTime.UtcNow;
        var lastProgressLog = DateTime.MinValue;
        var lastSpeedKbps = double.MaxValue;
        var slowSince = DateTime.MinValue;
        var lastPercent = -1;
        var errorTail = new StringBuilder();

        void HandleLine(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            lastOutput = DateTime.UtcNow;
            var line = raw.Trim();

            var speedMatch = SpeedRegex.Match(line);
            if (speedMatch.Success)
            {
                var value = double.Parse(speedMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                lastSpeedKbps = speedMatch.Groups[2].Value switch
                {
                    "GiB" => value * 1024 * 1024,
                    "MiB" => value * 1024,
                    "KiB" => value,
                    _ => value / 1024.0
                };
            }

            var percentMatch = PercentRegex.Match(line);
            if (percentMatch.Success && int.TryParse(percentMatch.Groups[1].Value, out var percent))
            {
                lastPercent = percent;
                Progress?.Invoke($"{phaseName}: {percent}%");
            }

            if (line.Contains("fatal:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("error:", StringComparison.OrdinalIgnoreCase))
            {
                errorTail.AppendLine(line);
                Log?.Invoke(new LogLine(LogKind.Err, line));
                return;
            }

            var isProgress = ProgressNoise.IsMatch(line);
            if (!isProgress || (DateTime.UtcNow - lastProgressLog).TotalSeconds >= 1.5)
            {
                lastProgressLog = DateTime.UtcNow;
                Log?.Invoke(new LogLine(LogKind.System, line));
            }
        }

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            Log?.Invoke(new LogLine(LogKind.Err, "无法启动 git: " + ex.Message));
            return (GitAttemptResult.Failed, -1);
        }

        var pumpErr = PumpAsync(process.StandardError, HandleLine, cancellationToken);
        var pumpOut = PumpAsync(process.StandardOutput, HandleLine, cancellationToken);

        try
        {
            while (!process.HasExited)
            {
                await Task.Delay(400, cancellationToken);
                if (process.HasExited) break;

                var now = DateTime.UtcNow;
                var silent = (now - lastOutput).TotalSeconds;
                var elapsed = (now - start).TotalSeconds;

                if (silent > timeoutSeconds && elapsed > timeoutSeconds)
                {
                    Log?.Invoke(new LogLine(LogKind.Err,
                        $"git 已 {silent:F1}s 无任何响应（阈值 {timeoutSeconds}s），终止并准备重试。"));
                    KillTree(process);
                    return (GitAttemptResult.NoResponse, -1);
                }

                if (elapsed > timeoutSeconds && lastSpeedKbps < minSpeedKBps)
                {
                    if (slowSince == DateTime.MinValue) slowSince = now;
                    else if ((now - slowSince).TotalSeconds > 8)
                    {
                        Log?.Invoke(new LogLine(LogKind.Warn,
                            $"git 速率持续低于 {minSpeedKBps} KB/s（当前约 {lastSpeedKbps:F0} KB/s），终止并准备重试。"));
                        KillTree(process);
                        return (GitAttemptResult.TooSlow, -1);
                    }
                }
                else
                {
                    slowSince = DateTime.MinValue;
                }

                Progress?.Invoke(lastPercent >= 0
                    ? $"{phaseName}: {lastPercent}%"
                    : $"{phaseName}: 等待响应 {silent:F0}s");
            }

            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15)); } catch { }
            await Task.WhenAll(pumpErr, pumpOut).WaitAsync(TimeSpan.FromSeconds(5));
            var exit = process.ExitCode;
            if (exit == 0)
            {
                Log?.Invoke(new LogLine(LogKind.Success, $"{phaseName}完成（{label}）。"));
                return (GitAttemptResult.Success, exit);
            }

            Log?.Invoke(new LogLine(LogKind.Err, $"git 退出码 {exit}。{(errorTail.Length > 0 ? " " + errorTail.ToString().Trim() : "")}"));
            return (GitAttemptResult.Failed, exit);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }
        finally
        {
            if (!process.HasExited) KillTree(process);
        }
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> onLine, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var pending = new StringBuilder();
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0) break;
                for (var i = 0; i < read; i++)
                {
                    var ch = buffer[i];
                    if (ch is '\r' or '\n')
                    {
                        if (pending.Length > 0)
                        {
                            onLine(pending.ToString());
                            pending.Clear();
                        }
                    }
                    else
                    {
                        pending.Append(ch);
                        if (pending.Length > 8192)
                        {
                            onLine(pending.ToString());
                            pending.Clear();
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }

        if (pending.Length > 0) onLine(pending.ToString());
    }

    private static string BuildGitOptions(string? proxy)
    {
        var proxyValue = proxy ?? "";
        return "-c http.sslBackend=openssl " +
               $"-c http.proxy={proxyValue} -c https.proxy={proxyValue} " +
               "-c core.askPass= -c credential.helper= ";
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private static void CleanupFailedClone(string target, bool existedBefore)
    {
        if (existedBefore) return;
        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
        catch { }
    }

    private static string Q(string value) => ProcessUtil.Quote(value);

    private static string Describe(GitAttemptResult result) => result switch
    {
        GitAttemptResult.Success => "成功",
        GitAttemptResult.NoResponse => "无响应",
        GitAttemptResult.TooSlow => "速率过低",
        _ => "失败"
    };

    public static bool TryNormalizeProxy(string value, out string proxy, out string host, out int port)
    {
        proxy = "";
        host = "";
        port = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (!text.Contains("://")) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
        if (uri.Port <= 0 || uri.Port > 65535) return false;
        proxy = uri.GetLeftPart(UriPartial.Authority);
        host = uri.Host;
        port = uri.Port;
        return true;
    }
}
