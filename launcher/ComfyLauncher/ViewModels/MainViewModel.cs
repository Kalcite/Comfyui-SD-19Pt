using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using ComfyLauncher.Infrastructure;
using ComfyLauncher.Models;
using ComfyLauncher.Services;

namespace ComfyLauncher.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);

    private readonly string _root;
    private readonly ComfyRunner _runner = new();
    private readonly GitService _git = new();
    private readonly ScriptRunner _scripts = new();

    private DispatcherTimer? _uptimeTimer;
    private CancellationTokenSource? _monitorCts;
    private DateTime _startedAt;
    private bool _stopRequested;

    private bool _isRunning;
    private bool _isServerReady;
    private bool _isBusy;
    private string _statusText = "未启动";
    private string _statusDetail = "点击“启动”运行 ComfyUI";
    private string _busyText = "";
    private string _commandPreview = "";
    private string _uptimeText = "";
    private string _envSummary = "尚未检查";
    private string _commandInput = "";
    private string _lastSavedLog = "";

    public MainViewModel()
    {
        _root = ProjectPaths.FindRoot();
        Settings = SettingsService.Load(_root);
        Settings.PropertyChanged += OnSettingsChanged;
        ApplyTheme();

        _runner.Log += AppendLog;
        _runner.Exited += code => Dispatcher.UIThread.Post(() => HandleExited(code));
        _git.Log += AppendLog;
        _git.Progress += text => Dispatcher.UIThread.Post(() => BusyText = text);
        _scripts.Log += AppendLog;

        BuildCommands();
        UpdateCommandPreview();
        RefreshModels();

        AppendLog(new LogLine(LogKind.System, "ComfyUI 启动器已就绪。"));
        AppendLog(new LogLine(LogKind.System, $"项目根目录: {_root}"));
    }

    public AppSettings Settings { get; }

    /// <summary>浅色/深色主题开关；切换后立即应用到 Avalonia 主题字典并持久化。</summary>
    public bool IsLightTheme
    {
        get => string.Equals(Settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        set
        {
            var theme = value ? "Light" : "Dark";
            if (string.Equals(Settings.Theme, theme, StringComparison.OrdinalIgnoreCase)) return;
            Settings.Theme = theme;
            ApplyTheme();
            foreach (var line in LogLines) line.ApplyTheme(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDarkTheme));
            SettingsService.Save(Settings);
        }
    }

    public bool IsDarkTheme => !IsLightTheme;

    private void ApplyTheme()
    {
        if (Application.Current == null) return;
        Application.Current.RequestedThemeVariant = IsLightTheme ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    public ObservableCollection<LogLine> LogLines { get; } = new();
    public ObservableCollection<StatusItem> StatusItems { get; } = new();
    public ObservableCollection<ModelItem> Models { get; } = new();
    public ObservableCollection<ModelCategory> ModelCategories { get; } = new();

    public IReadOnlyList<string> TorchProfiles { get; } = new[]
    {
        "CUDA 13.0 + torch 2.9.1",
        "CUDA 12.8 + torch 2.9.1",
        "CUDA 12.6 + torch 2.9.1",
        "CUDA 12.4 + torch 2.6.0",
        "DirectML (torch-directml)",
        "CPU + torch 2.9.1"
    };

    public IReadOnlyList<string> VramModes { get; } = new[]
    {
        "自动", "低显存 (--lowvram)", "普通显存 (--normalvram)", "高显存 (--highvram)",
        "不保留显存 (--novram)", "CPU 模式 (--cpu)"
    };

    public IReadOnlyList<string> PrecisionModes { get; } = new[]
    {
        "自动", "强制 FP16 (--force-fp16)", "强制 FP32 (--force-fp32)",
        "VAE FP16 (--fp16-vae)", "VAE FP32 (--fp32-vae)", "VAE BF16 (--bf16-vae)"
    };

    public IReadOnlyList<string> PreviewMethods { get; } = new[] { "无", "auto", "latent2rgb", "taesd" };

    public IReadOnlyList<string> AttentionModes { get; } = new[]
    {
        "默认", "PyTorch SDPA (--use-pytorch-cross-attention)", "Split (--use-split-cross-attention)",
        "Quad (--use-quad-cross-attention)", "禁用 xFormers (--disable-xformers)"
    };

    public IReadOnlyList<string> VerboseLevels { get; } = new[] { "默认", "调试 (DEBUG)", "详细 (INFO)", "仅警告 (WARNING)" };

    public Func<string, Task<string?>>? FolderPicker { get; set; }
    public Func<string, Task<string?>>? FilePicker { get; set; }
    public Action<string>? ClipboardWriter { get; set; }

    public string RootPath => _root;

    public string ComfyPathResolved => ProjectPaths.ResolveComfy(_root, Settings.ComfyPath);
    public string PythonPathResolved => ProjectPaths.ResolvePython(_root, Settings.PythonPath);
    public string VenvPathResolved => ProjectPaths.ResolveVenv(_root, Settings.VenvPath);
    public string VenvPythonPath => ProjectPaths.VenvPython(VenvPathResolved);
    public string EffectivePython => File.Exists(VenvPythonPath) ? VenvPythonPath : PythonPathResolved;
    public string UserPathResolved => ProjectPaths.ResolveUser(_root, Settings.UserDirectory);
    public string OutputPathResolved => ProjectPaths.ResolveOutput(_root, Settings.OutputDirectory);
    public string InputPathResolved => ProjectPaths.ResolveInput(_root, Settings.InputDirectory);
    public string ModelsPathResolved => string.IsNullOrWhiteSpace(Settings.ModelsDirectory)
        ? Path.Combine(ComfyPathResolved, "models")
        : Settings.ModelsDirectory;

    public string ConfigPath => ProjectPaths.ConfigFile(_root);
    public string ServerUrl => $"http://127.0.0.1:{Settings.GetInt(Settings.Port, 8188)}/";

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            OnPropertyChanged(nameof(IsStopped));
            RefreshCommands();
        }
    }

    public bool IsStopped => !_isRunning;

    public bool IsServerReady
    {
        get => _isServerReady;
        private set
        {
            if (!Set(ref _isServerReady, value)) return;
            OnPropertyChanged(nameof(ServerStateText));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            RefreshCommands();
        }
    }

    public bool IsIdle => !_isBusy;

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        private set => Set(ref _statusDetail, value);
    }

    public string BusyText
    {
        get => _busyText;
        private set
        {
            if (!Set(ref _busyText, value)) return;
            OnPropertyChanged(nameof(HasBusyText));
        }
    }

    public bool HasBusyText => !string.IsNullOrEmpty(_busyText);

    public string CommandPreview
    {
        get => _commandPreview;
        private set => Set(ref _commandPreview, value);
    }

    public string UptimeText
    {
        get => _uptimeText;
        private set => Set(ref _uptimeText, value);
    }

    public string EnvSummary
    {
        get => _envSummary;
        private set => Set(ref _envSummary, value);
    }

    public string ServerStateText => IsServerReady ? "服务已就绪" : IsRunning ? "正在启动" : "未运行";

    public string CommandInput
    {
        get => _commandInput;
        set => Set(ref _commandInput, value);
    }

    public string LastSavedLog
    {
        get => _lastSavedLog;
        private set => Set(ref _lastSavedLog, value);
    }

    public AsyncRelayCommand LaunchCommand { get; private set; } = null!;
    public AsyncRelayCommand StopCommand { get; private set; } = null!;
    public AsyncRelayCommand RestartCommand { get; private set; } = null!;
    public RelayCommand OpenWebCommand { get; private set; } = null!;
    public RelayCommand SaveSettingsCommand { get; private set; } = null!;
    public AsyncRelayCommand CheckEnvironmentCommand { get; private set; } = null!;
    public AsyncRelayCommand SetupAllCommand { get; private set; } = null!;
    public AsyncRelayCommand SetupPythonCommand { get; private set; } = null!;
    public AsyncRelayCommand SetupVenvCommand { get; private set; } = null!;
    public AsyncRelayCommand InstallTorchCommand { get; private set; } = null!;
    public AsyncRelayCommand DownloadModelsCommand { get; private set; } = null!;
    public AsyncRelayCommand CloneComfyCommand { get; private set; } = null!;
    public AsyncRelayCommand BrowseComfyCommand { get; private set; } = null!;
    public AsyncRelayCommand BrowsePythonCommand { get; private set; } = null!;
    public AsyncRelayCommand BrowseVenvCommand { get; private set; } = null!;
    public AsyncRelayCommand BrowseUserCommand { get; private set; } = null!;
    public AsyncRelayCommand BrowseModelsCommand { get; private set; } = null!;
    public AsyncRelayCommand BrowseOutputCommand { get; private set; } = null!;
    public AsyncRelayCommand BrowseInputCommand { get; private set; } = null!;
    public RelayCommand ResetPathsCommand { get; private set; } = null!;
    public RelayCommand RefreshModelsCommand { get; private set; } = null!;
    public RelayCommand OpenModelsCommand { get; private set; } = null!;
    public RelayCommand OpenOutputCommand { get; private set; } = null!;
    public RelayCommand OpenUserCommand { get; private set; } = null!;
    public RelayCommand OpenComfyCommand { get; private set; } = null!;
    public RelayCommand OpenLogsCommand { get; private set; } = null!;
    public RelayCommand OpenConfigFolderCommand { get; private set; } = null!;
    public RelayCommand ClearConsoleCommand { get; private set; } = null!;
    public RelayCommand SaveConsoleCommand { get; private set; } = null!;
    public RelayCommand CopyLogCommand { get; private set; } = null!;
    public RelayCommand CopyCommandLineCommand { get; private set; } = null!;
    public RelayCommand SendCommand { get; private set; } = null!;

    private void BuildCommands()
    {
        var error = new Action<Exception>(ex =>
        {
            AppendLog(new LogLine(LogKind.Err, "操作失败: " + ex.Message));
            StatusDetail = ex.Message;
        });

        LaunchCommand = new AsyncRelayCommand(() => RunBusyAsync("启动 ComfyUI...", LaunchCoreAsync),
            () => !IsRunning && !IsBusy, error);
        StopCommand = new AsyncRelayCommand(() => RunBusyAsync("停止 ComfyUI...", StopCoreAsync),
            () => IsRunning && !IsBusy, error);
        RestartCommand = new AsyncRelayCommand(() => RunBusyAsync("重启 ComfyUI...", RestartCoreAsync),
            () => !IsBusy, error);
        OpenWebCommand = new RelayCommand(() => OpenUrl(ServerUrl));
        SaveSettingsCommand = new RelayCommand(SaveSettingsAndLog);
        CheckEnvironmentCommand = new AsyncRelayCommand(
            () => RunBusyAsync("检查环境...", CheckEnvironmentCoreAsync), () => !IsBusy, error);
        SetupAllCommand = new AsyncRelayCommand(
            () => RunBusyAsync("一键构建环境...", SetupAllCoreAsync), () => !IsBusy, error);
        SetupPythonCommand = new AsyncRelayCommand(
            () => RunBusyAsync("修复内置 Python...", () => RunScriptAsync("setup-python.ps1", $"-Root {Q(_root)}")),
            () => !IsBusy, error);
        SetupVenvCommand = new AsyncRelayCommand(
            () => RunBusyAsync("创建/修复虚拟环境...",
                () => RunScriptAsync("setup-venv.ps1", $"-Root {Q(_root)} -VenvPath {Q(VenvPathResolved)}")),
            () => !IsBusy, error);
        InstallTorchCommand = new AsyncRelayCommand(
            () => RunBusyAsync("安装 PyTorch 与 ComfyUI 依赖（耗时较长）...", InstallTorchCoreAsync),
            () => !IsBusy, error);
        DownloadModelsCommand = new AsyncRelayCommand(
            () => RunBusyAsync("下载模型...", () => RunScriptAsync("setup-models.ps1", $"-Root {Q(_root)}")),
            () => !IsBusy, error);
        CloneComfyCommand = new AsyncRelayCommand(
            () => RunBusyAsync("克隆/更新 ComfyUI...", CloneComfyCoreAsync), () => !IsBusy, error);

        BrowseComfyCommand = new AsyncRelayCommand(() => BrowseFolderAsync("选择 ComfyUI 目录", ComfyPathResolved, p => Settings.ComfyPath = p));
        BrowsePythonCommand = new AsyncRelayCommand(() => BrowseFileAsync("选择 Python 解释器", PythonPathResolved, p => Settings.PythonPath = p));
        BrowseVenvCommand = new AsyncRelayCommand(() => BrowseFolderAsync("选择虚拟环境目录", VenvPathResolved, p => Settings.VenvPath = p));
        BrowseUserCommand = new AsyncRelayCommand(() => BrowseFolderAsync("选择用户目录（工作流/设置）", UserPathResolved, p => Settings.UserDirectory = p));
        BrowseModelsCommand = new AsyncRelayCommand(() => BrowseFolderAsync("选择模型目录", ModelsPathResolved, p => Settings.ModelsDirectory = p));
        BrowseOutputCommand = new AsyncRelayCommand(() => BrowseFolderAsync("选择输出目录", OutputPathResolved, p => Settings.OutputDirectory = p));
        BrowseInputCommand = new AsyncRelayCommand(() => BrowseFolderAsync("选择输入目录", InputPathResolved, p => Settings.InputDirectory = p));
        ResetPathsCommand = new RelayCommand(ResetPaths);
        RefreshModelsCommand = new RelayCommand(RefreshModels);
        OpenModelsCommand = new RelayCommand(() => OpenFolder(ModelsPathResolved));
        OpenOutputCommand = new RelayCommand(() => OpenFolder(OutputPathResolved));
        OpenUserCommand = new RelayCommand(() => OpenFolder(UserPathResolved));
        OpenComfyCommand = new RelayCommand(() => OpenFolder(ComfyPathResolved));
        OpenLogsCommand = new RelayCommand(() => OpenFolder(ProjectPaths.LogsDir(_root)));
        OpenConfigFolderCommand = new RelayCommand(() => OpenFolder(Path.GetDirectoryName(ConfigPath) ?? _root));
        ClearConsoleCommand = new RelayCommand(() => LogLines.Clear());
        SaveConsoleCommand = new RelayCommand(SaveConsole);
        CopyLogCommand = new RelayCommand(CopyLog);
        CopyCommandLineCommand = new RelayCommand(() => Copy(CommandPreview));
        SendCommand = new RelayCommand(() =>
        {
            var text = CommandInput;
            if (string.IsNullOrWhiteSpace(text)) return;
            _runner.Send(text);
            AppendLog(new LogLine(LogKind.System, "> " + text));
            CommandInput = "";
        });
    }

    public async Task InitializeAsync()
    {
        await RunBusyAsync("检查环境...", CheckEnvironmentCoreAsync);
    }

    private async Task RunBusyAsync(string title, Func<Task> action)
    {
        if (IsBusy)
        {
            AppendLog(new LogLine(LogKind.Warn, "已有任务正在执行，请稍候。"));
            return;
        }

        IsBusy = true;
        BusyText = title;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            StatusDetail = "操作已取消";
        }
        catch (Exception ex)
        {
            AppendLog(new LogLine(LogKind.Err, "操作失败: " + ex.Message));
            StatusDetail = ex.Message;
        }
        finally
        {
            BusyText = "";
            IsBusy = false;
            RefreshCommands();
        }
    }

    private async Task LaunchCoreAsync()
    {
        if (IsRunning)
        {
            AppendLog(new LogLine(LogKind.Warn, "ComfyUI 已在运行。"));
            return;
        }

        SaveSettingsAndLog();
        var comfy = ComfyPathResolved;
        var python = EffectivePython;
        var main = Path.Combine(comfy, "main.py");
        if (!File.Exists(main))
        {
            throw new FileNotFoundException($"未找到 {main}。请先在“环境”页克隆 ComfyUI。");
        }
        if (!File.Exists(python))
        {
            throw new FileNotFoundException($"未找到 Python 解释器 {python}。请先创建虚拟环境。");
        }

        var port = Settings.GetInt(Settings.Port, 8188);
        if (port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("端口必须是 1-65535 之间的整数。");
        }
        if (!await ProcessUtil.IsPortFreeAsync(port))
        {
            throw new InvalidOperationException($"端口 {port} 已被占用，请修改端口或结束占用进程。");
        }

        foreach (var dir in new[]
                 {
                     comfy, UserPathResolved, OutputPathResolved, InputPathResolved,
                     ProjectPaths.TempDir(_root), ProjectPaths.LogsDir(_root)
                 })
        {
            Directory.CreateDirectory(dir);
        }

        var spec = CommandBuilder.Build(Settings, comfy, python);
        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = spec.Arguments,
            WorkingDirectory = comfy
        };
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["TMP"] = ProjectPaths.TempDir(_root);
        psi.Environment["TEMP"] = ProjectPaths.TempDir(_root);
        psi.Environment["HF_HOME"] = Path.Combine(_root, "cache", "hf");
        psi.Environment["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (!string.IsNullOrWhiteSpace(Settings.HfMirror))
        {
            psi.Environment["HF_ENDPOINT"] = Settings.HfMirror.Trim();
        }

        _stopRequested = false;
        _runner.Start(psi, spec.Display);
        _startedAt = DateTime.Now;
        IsRunning = true;
        IsServerReady = false;
        StatusText = "启动中";
        StatusDetail = $"等待 {ServerUrl} 返回就绪...";
        StartUptimeTimer();

        _monitorCts?.Cancel();
        _monitorCts = new CancellationTokenSource();
        _ = MonitorServerAsync(port, _monitorCts.Token);
        RefreshCommands();
    }

    private async Task MonitorServerAsync(int port, CancellationToken cancellationToken)
    {
        var url = $"http://127.0.0.1:{port}/";
        var deadline = DateTime.UtcNow.AddMinutes(8);
        while (!cancellationToken.IsCancellationRequested && IsRunning && DateTime.UtcNow < deadline)
        {
            if (await ProcessUtil.HttpReadyAsync(url, 1500))
            {
                Dispatcher.UIThread.Post(() => OnServerReady(url));
                return;
            }
            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (!cancellationToken.IsCancellationRequested && IsRunning)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusDetail = "等待服务就绪超时，请查看控制台日志。";
                AppendLog(new LogLine(LogKind.Warn, "等待 ComfyUI HTTP 服务就绪超时（8 分钟）。"));
            });
        }
    }

    private void OnServerReady(string url)
    {
        if (!IsRunning) return;
        IsServerReady = true;
        StatusText = "运行中";
        StatusDetail = url;
        AppendLog(new LogLine(LogKind.Success, $"ComfyUI 已就绪: {url}"));
        AppendLog(new LogLine(LogKind.System, "提示：可在 ComfyUI 的 Workflow 菜单加载 workflows 目录中的超分/生图工作流。"));
        if (Settings.AutoOpenBrowser) OpenUrl(url);
    }

    private async Task StopCoreAsync()
    {
        _stopRequested = true;
        _monitorCts?.Cancel();
        await _runner.StopAsync();
        IsRunning = false;
        IsServerReady = false;
        StopUptimeTimer();
        StatusText = "已停止";
        StatusDetail = "ComfyUI 进程已结束";
        AppendLog(new LogLine(LogKind.System, "ComfyUI 已停止。"));
        RefreshCommands();
    }

    private async Task RestartCoreAsync()
    {
        if (IsRunning)
        {
            _stopRequested = true;
            _monitorCts?.Cancel();
            await _runner.StopAsync();
            IsRunning = false;
            IsServerReady = false;
            StopUptimeTimer();
        }
        await Task.Delay(700);
        _stopRequested = false;
        await LaunchCoreAsync();
    }

    private void HandleExited(int exitCode)
    {
        var unexpected = !_stopRequested;
        IsRunning = false;
        IsServerReady = false;
        _monitorCts?.Cancel();
        StopUptimeTimer();
        StatusText = unexpected ? $"进程意外退出 ({exitCode})" : "已停止";
        StatusDetail = unexpected ? "查看控制台中的错误信息；可开启自动重启。" : "ComfyUI 进程已结束";
        AppendLog(new LogLine(unexpected ? LogKind.Warn : LogKind.System,
            $"ComfyUI 进程已退出，退出码 {exitCode}。"));
        RefreshCommands();

        if (unexpected && Settings.AutoRestart)
        {
            _ = AutoRestartAsync();
        }
    }

    private async Task AutoRestartAsync()
    {
        await Task.Delay(3000);
        if (IsRunning || _stopRequested) return;
        AppendLog(new LogLine(LogKind.System, "自动重启已触发 ..."));
        await RunBusyAsync("自动重启 ComfyUI...", LaunchCoreAsync);
    }

    private async Task CheckEnvironmentCoreAsync()
    {
        var items = new List<StatusItem>();
        var python = PythonPathResolved;
        var comfy = ComfyPathResolved;
        var venvPython = VenvPythonPath;

        if (!File.Exists(python))
        {
            items.Add(new StatusItem("内置 Python 3.12.10", "未找到: " + python, StatusState.Fail));
        }
        else
        {
            var version = await RunPythonAsync(python, "import sys;print(sys.version.split()[0])", 15);
            if (version.ExitCode == 0)
            {
                var v = version.StdOut.Trim();
                items.Add(new StatusItem("内置 Python 3.12.10", $"{v}  ({python})",
                    v.StartsWith("3.12") ? StatusState.Ok : StatusState.Warn));
            }
            else
            {
                items.Add(new StatusItem("内置 Python 3.12.10", FirstLine(version.StdErr, "无法运行"), StatusState.Fail));
            }
        }

        if (File.Exists(Path.Combine(comfy, "main.py")))
        {
            var ver = ReadComfyVersion(comfy);
            items.Add(new StatusItem("ComfyUI", string.IsNullOrEmpty(ver) ? comfy : $"{ver}  ({comfy})", StatusState.Ok));
        }
        else
        {
            items.Add(new StatusItem("ComfyUI", "未安装：缺少 " + Path.Combine(comfy, "main.py"), StatusState.Fail));
        }

        if (!File.Exists(venvPython))
        {
            items.Add(new StatusItem("虚拟环境", "未创建（可一键创建）: " + VenvPathResolved, StatusState.Warn));
        }
        else
        {
            var pip = await RunPythonAsync(venvPython, "import sys;print(sys.version.split()[0])", 20);
            items.Add(new StatusItem("虚拟环境",
                pip.ExitCode == 0 ? $"{pip.StdOut.Trim()}  ({VenvPathResolved})" : FirstLine(pip.StdErr, "解释器异常"),
                pip.ExitCode == 0 ? StatusState.Ok : StatusState.Fail));
        }

        var torchPython = File.Exists(venvPython) ? venvPython : python;
        var torch = await RunPythonAsync(torchPython,
            "import torch;print(torch.__version__);print(torch.cuda.is_available());print(torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'CPU')",
            180);
        if (torch.ExitCode == 0)
        {
            var lines = torch.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var version = lines.ElementAtOrDefault(0) ?? "?";
            var cuda = lines.ElementAtOrDefault(1) ?? "?";
            var device = lines.ElementAtOrDefault(2) ?? "?";
            items.Add(new StatusItem("PyTorch / CUDA", $"{version}, CUDA={cuda}, {device}  ·  {Settings.TorchProfile}",
                cuda.Equals("True", StringComparison.OrdinalIgnoreCase) ? StatusState.Ok : StatusState.Warn));
        }
        else
        {
            items.Add(new StatusItem("PyTorch / CUDA", FirstLine(torch.StdErr, "未安装或导入失败"), StatusState.Warn));
        }

        var modelRoot = ModelsPathResolved;
        var checkpoints = SafeFiles(Path.Combine(modelRoot, "checkpoints"), "*.safetensors", "*.ckpt", "*.pt");
        var upscalers = SafeFiles(Path.Combine(modelRoot, "upscale_models"), "*.pth", "*.safetensors", "*.onnx");
        items.Add(new StatusItem("模型文件",
            $"{checkpoints.Length} 个生图模型 / {upscalers.Length} 个超分模型  ({modelRoot})",
            checkpoints.Length > 0 && upscalers.Length > 0 ? StatusState.Ok
                : checkpoints.Length > 0 || upscalers.Length > 0 ? StatusState.Warn : StatusState.Fail));

        var git = await ProcessUtil.RunAsync("git", "--version", _root, TimeSpan.FromSeconds(10));
        items.Add(new StatusItem("Git", git.ExitCode == 0 ? git.StdOut.Trim() : "未检测到 git（克隆/更新需要）",
            git.ExitCode == 0 ? StatusState.Ok : StatusState.Warn));

        StatusItems.Clear();
        foreach (var item in items) StatusItems.Add(item);
        var failed = items.Count(i => i.State == StatusState.Fail);
        var warn = items.Count(i => i.State == StatusState.Warn);
        EnvSummary = failed == 0 && warn == 0 ? "环境就绪" : $"{failed} 项缺失 / {warn} 项提醒";
        RefreshModels();
        AppendLog(new LogLine(LogKind.System, "环境检查完成: " + EnvSummary));
    }

    private async Task SetupAllCoreAsync()
    {
        if (!File.Exists(PythonPathResolved))
        {
            AppendLog(new LogLine(LogKind.System, "未找到内置 Python，尝试运行 setup-python.ps1 ..."));
            if (!await RunScriptAsync("setup-python.ps1", $"-Root {Q(_root)}")) return;
        }

        if (!File.Exists(Path.Combine(ComfyPathResolved, "main.py")))
        {
            if (!await CloneComfyCoreAsync()) return;
        }
        else if (!string.IsNullOrWhiteSpace(Settings.ComfyVersion))
        {
            if (!await CheckoutComfyVersionAsync(ComfyPathResolved, Settings.ComfyVersion.Trim())) return;
        }

        if (!File.Exists(VenvPythonPath))
        {
            if (!await RunScriptAsync("setup-venv.ps1", $"-Root {Q(_root)} -VenvPath {Q(VenvPathResolved)}")) return;
        }

        var torch = await RunPythonAsync(VenvPythonPath, "import torch;print(torch.__version__)", 120);
        if (torch.ExitCode != 0)
        {
            if (!await InstallTorchCoreAsync()) return;
        }

        if (Settings.DownloadModelsAfterSetup)
        {
            if (!await RunScriptAsync("setup-models.ps1", $"-Root {Q(_root)}")) return;
        }
        else
        {
            AppendLog(new LogLine(LogKind.System, "已跳过模型下载（可选）。可在“模型”页或 scripts\\setup-models.ps1 单独下载。"));
        }

        await CheckEnvironmentCoreAsync();
        AppendLog(new LogLine(LogKind.Success, "一键构建流程结束。"));
    }

    private (string Backend, string Cuda, string Torch) GetTorchSelection()
    {
        var profile = Settings.TorchProfile ?? "";
        if (profile.Contains("DirectML", StringComparison.OrdinalIgnoreCase))
        {
            return ("DirectML", "", "");
        }
        if (profile.StartsWith("CPU", StringComparison.OrdinalIgnoreCase))
        {
            return ("CPU", "", "2.9.1");
        }

        var cuda = Regex.Match(profile, @"CUDA\s+([\d.]+)").Groups[1].Value;
        var torch = Regex.Match(profile, @"torch\s+([\d.]+)").Groups[1].Value;
        return ("CUDA", string.IsNullOrEmpty(cuda) ? "13.0" : cuda,
            string.IsNullOrEmpty(torch) ? "2.9.1" : torch);
    }

    private async Task<bool> InstallTorchCoreAsync()
    {
        var (backend, cuda, torch) = GetTorchSelection();
        var args = $"-Root {Q(_root)} -VenvPath {Q(VenvPathResolved)} -Backend {backend}";
        if (!string.IsNullOrWhiteSpace(cuda)) args += $" -CudaVersion {cuda}";
        if (!string.IsNullOrWhiteSpace(torch)) args += $" -TorchVersion {torch}";
        if (!string.IsNullOrWhiteSpace(Settings.PipIndex)) args += $" -PipIndex {Q(Settings.PipIndex)}";
        if (!string.IsNullOrWhiteSpace(Settings.GitProxy)) args += $" -Proxy {Q(Settings.GitProxy)}";
        var ok = await RunScriptAsync("setup-torch.ps1", args);
        if (ok)
        {
            await CheckEnvironmentCoreAsync();
        }
        return ok;
    }

    private async Task<bool> CloneComfyCoreAsync()
    {
        var target = ComfyPathResolved;
        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? _root);
        var ok = await _git.CloneOrUpdateAsync(
            "https://github.com/comfyanonymous/ComfyUI.git",
            target,
            Settings.GitProxy,
            Settings.GitUseProxyFallback,
            Settings.GetInt(Settings.GitTimeoutSeconds, 15),
            Settings.GetInt(Settings.GitMinSpeedKBps, 80));
        if (ok)
        {
            AppendLog(new LogLine(LogKind.Success, "ComfyUI 源码已就绪。"));
            if (!string.IsNullOrWhiteSpace(Settings.ComfyVersion))
            {
                ok = await CheckoutComfyVersionAsync(target, Settings.ComfyVersion.Trim());
            }
            await CheckEnvironmentCoreAsync();
        }
        return ok;
    }

    private async Task<bool> CheckoutComfyVersionAsync(string target, string version)
    {
        const string gitOptions = "-c http.sslBackend=openssl -c http.proxy= -c https.proxy= ";
        AppendLog(new LogLine(LogKind.System, $"切换 ComfyUI 版本/提交: {version}"));

        await ProcessUtil.RunAsync("git", $"{gitOptions}-C {Q(target)} fetch --tags --depth 1 origin",
            target, TimeSpan.FromMinutes(3));
        var checkout = await ProcessUtil.RunAsync("git", $"{gitOptions}-C {Q(target)} checkout {Q(version)}",
            target, TimeSpan.FromMinutes(1));
        if (checkout.ExitCode != 0)
        {
            var fetch = await ProcessUtil.RunAsync("git", $"{gitOptions}-C {Q(target)} fetch --depth 1 origin {Q(version)}",
                target, TimeSpan.FromMinutes(3));
            checkout = await ProcessUtil.RunAsync("git", $"{gitOptions}-C {Q(target)} checkout FETCH_HEAD",
                target, TimeSpan.FromMinutes(1));
            if (fetch.ExitCode != 0 || checkout.ExitCode != 0)
            {
                AppendLog(new LogLine(LogKind.Err, $"无法切换到 {version}，ComfyUI 保持当前版本。"));
                return false;
            }
        }

        AppendLog(new LogLine(LogKind.Success, $"ComfyUI 已切换到 {version}。"));
        return true;
    }

    private async Task<bool> RunScriptAsync(string scriptName, string arguments)
    {
        var path = Path.Combine(_root, "scripts", scriptName);
        if (!File.Exists(path))
        {
            AppendLog(new LogLine(LogKind.Err, "脚本不存在: " + path));
            return false;
        }

        var code = await _scripts.RunAsync(path, arguments, _root);
        if (code == 0)
        {
            AppendLog(new LogLine(LogKind.Success, $"{scriptName} 执行成功。"));
            RefreshModels();
            return true;
        }

        AppendLog(new LogLine(LogKind.Err, $"{scriptName} 执行失败，退出码 {code}。"));
        return false;
    }

    private Task<ProcessResult> RunPythonAsync(string python, string code, int timeoutSeconds)
    {
        var env = new Dictionary<string, string>
        {
            ["TMP"] = ProjectPaths.TempDir(_root),
            ["TEMP"] = ProjectPaths.TempDir(_root),
            ["PYTHONUTF8"] = "1",
            ["PYTHONIOENCODING"] = "utf-8",
            ["HF_HOME"] = Path.Combine(_root, "cache", "hf")
        };
        return ProcessUtil.RunAsync(python, "-c " + Q(code), _root, TimeSpan.FromSeconds(timeoutSeconds), env);
    }

    public void RefreshModels()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshModels);
            return;
        }

        Models.Clear();
        ModelCategories.Clear();
        var modelRoot = ModelsPathResolved;

        void AddCategory(string key, string name, string description, string sample,
            string[] patterns, params string[] extraDirectories)
        {
            var dir = Path.Combine(modelRoot, key);
            var files = new List<string>();
            foreach (var pattern in patterns)
            {
                files.AddRange(SafeFiles(dir, pattern));
                foreach (var extra in extraDirectories)
                {
                    files.AddRange(SafeFiles(Path.Combine(modelRoot, extra), pattern));
                }
            }

            files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var category = new ModelCategory(key, name, dir, description, sample, () => OpenFolder(dir))
            {
                Count = files.Count,
                FilesPreview = files.Count == 0
                    ? "目录为空；放入文件后点“刷新”。"
                    : "已找到: " + string.Join(" · ", files.Select(Path.GetFileName).Take(4))
            };
            ModelCategories.Add(category);

            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    Models.Add(new ModelItem(info.Name, name, info.Length, info.FullName));
                }
                catch { }
            }
        }

        AddCategory("checkpoints", "生图大模型 / Checkpoint",
            "SD1.5、SDXL 等单文件 checkpoint；最常用的生图入口。",
            "v1-5-pruned-emaonly.safetensors",
            new[] { "*.safetensors", "*.ckpt", "*.pt" });

        AddCategory("diffusion_models", "扩散模型 / Diffusion Model",
            "Flux、SD3 等分离式 UNET/DiT；新版 ComfyUI 读取此目录（旧版也兼容 unet）。",
            "flux1-dev.safetensors",
            new[] { "*.safetensors", "*.gguf", "*.pt" }, "unet");

        AddCategory("text_encoders", "文本编码器 / 语言解释器",
            "Flux 需要 CLIP-L + T5；旧目录 clip 也兼容。",
            "clip_l.safetensors、t5xxl_fp16.safetensors",
            new[] { "*.safetensors", "*.pt", "*.gguf" }, "clip");

        AddCategory("vae", "VAE / 潜空间解码器",
            "Flux 使用 ae.safetensors；SD1.5 常用 vae-ft-mse-840000-ema-pruned。",
            "ae.safetensors、vae-ft-mse-840000-ema-pruned.safetensors",
            new[] { "*.safetensors", "*.pt", "*.pth" });

        AddCategory("loras", "LoRA / 风格微调",
            "Flux LoRA、SD LoRA、角色/风格 LoRA 都放在这里。",
            "my_style_flux.safetensors、detail_sd15.safetensors",
            new[] { "*.safetensors", "*.pt" });

        AddCategory("upscale_models", "超分模型 / Upscale",
            "4x 放大模型，供 Upscale Image (using Model) 使用。",
            "4x-UltraSharp.pth、RealESRGAN_x4plus.pth",
            new[] { "*.pth", "*.safetensors", "*.onnx" });

        AddCategory("controlnet", "ControlNet",
            "姿态、深度、边缘等控制模型。",
            "control_v11p_sd15_canny.pth",
            new[] { "*.safetensors", "*.pth", "*.ckpt" });

        AddCategory("clip_vision", "CLIP Vision",
            "IP-Adapter / 参考图工作流使用的视觉编码器。",
            "clip_vision_g.safetensors",
            new[] { "*.safetensors", "*.pt", "*.pth" });

        AddCategory("embeddings", "Embeddings / Textual Inversion",
            "正向/负向文本嵌入（如 easynegative）。",
            "easynegative.safetensors",
            new[] { "*.safetensors", "*.pt" });
    }

    private static string[] SafeFiles(string dir, params string[] patterns)
    {
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var pattern in patterns)
        {
            try { list.AddRange(Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly)); }
            catch { }
        }
        return list.Distinct().ToArray();
    }

    private static string ReadComfyVersion(string comfy)
    {
        try
        {
            var file = Path.Combine(comfy, "comfyui_version.py");
            if (!File.Exists(file)) return "";
            var match = Regex.Match(File.ReadAllText(file), "__version__\\s*=\\s*[\"']([^\"']+)[\"']");
            return match.Success ? "v" + match.Groups[1].Value : "";
        }
        catch
        {
            return "";
        }
    }

    private static string FirstLine(string text, string fallback)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? fallback : line!;
    }

    private void SaveSettingsAndLog()
    {
        var result = SettingsService.Save(Settings);
        AppendLog(new LogLine(LogKind.System, "配置已保存: " + result));
    }

    private void ResetPaths()
    {
        Settings.ComfyPath = ProjectPaths.DefaultComfy(_root);
        Settings.PythonPath = ProjectPaths.DefaultPython(_root);
        Settings.VenvPath = ProjectPaths.DefaultVenv(_root);
        Settings.UserDirectory = ProjectPaths.DefaultUser(_root);
        Settings.OutputDirectory = ProjectPaths.DefaultOutput(_root);
        Settings.InputDirectory = ProjectPaths.DefaultInput(_root);
        Settings.ModelsDirectory = "";
        UpdateCommandPreview();
        RefreshModels();
        AppendLog(new LogLine(LogKind.System, "已恢复默认路径。"));
    }

    private async Task BrowseFolderAsync(string title, string current, Action<string> apply)
    {
        if (FolderPicker == null) return;
        var picked = await FolderPicker(title);
        if (string.IsNullOrWhiteSpace(picked)) return;
        apply(picked);
        UpdateCommandPreview();
        RefreshModels();
        SaveSettingsAndLog();
    }

    private async Task BrowseFileAsync(string title, string current, Action<string> apply)
    {
        if (FilePicker == null) return;
        var picked = await FilePicker(title);
        if (string.IsNullOrWhiteSpace(picked)) return;
        apply(picked);
        UpdateCommandPreview();
        SaveSettingsAndLog();
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            if (IsElevated())
            {
                // 以管理员运行时，直接把 URL 交给桌面 Shell，让浏览器由普通权限的 Explorer 启动，
                // 避免 Chrome/Edge 继承管理员权限后提示“以管理员身份重启”。
                Process.Start(new ProcessStartInfo("explorer.exe", url) { UseShellExecute = true });
            }
            else
            {
                // 默认浏览器 + 链接方式；不指定浏览器可执行文件，避免权限/重启警告。
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true, Verb = "open" });
            }
        }
        catch (Exception ex)
        {
            try
            {
                Process.Start(new ProcessStartInfo("rundll32.exe", "url.dll,FileProtocolHandler " + url)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch
            {
                AppendLog(new LogLine(LogKind.Err, "打开浏览器失败: " + ex.Message));
            }
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            // 交给 Windows Shell 打开目录，避免直接启动 explorer.exe 时在部分环境出现 0xc0000142。
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex)
        {
            try
            {
                var fallback = new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true };
                fallback.ArgumentList.Add(path);
                Process.Start(fallback);
            }
            catch
            {
                AppendLog(new LogLine(LogKind.Err, "打开目录失败: " + ex.Message));
            }
        }
    }

    private void SaveConsole()
    {
        try
        {
            var path = Path.Combine(ProjectPaths.LogsDir(_root), $"console-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var lines = LogLines.Select(l => $"[{l.Time}] [{l.Tag}] {l.Text}");
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            LastSavedLog = path;
            AppendLog(new LogLine(LogKind.Success, "控制台日志已保存: " + path));
        }
        catch (Exception ex)
        {
            AppendLog(new LogLine(LogKind.Err, "保存日志失败: " + ex.Message));
        }
    }

    private void CopyLog()
    {
        var text = string.Join(Environment.NewLine, LogLines.Select(l => l.Text));
        Copy(text);
    }

    private void Copy(string text)
    {
        try
        {
            ClipboardWriter?.Invoke(text);
            AppendLog(new LogLine(LogKind.System, "已复制到剪贴板。"));
        }
        catch (Exception ex)
        {
            AppendLog(new LogLine(LogKind.Err, "复制失败: " + ex.Message));
        }
    }

    public void AppendLog(LogLine line)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AppendLog(line));
            return;
        }

        var text = AnsiRegex.Replace(line.Text, "");
        var entry = new LogLine(line.Kind, text);
        entry.ApplyTheme(IsLightTheme);
        LogLines.Add(entry);

        var max = Settings.GetInt(Settings.ConsoleMaxLines, 4000);
        if (max < 200) max = 200;
        if (LogLines.Count > max)
        {
            for (var i = 0; i < LogLines.Count - max + 1; i++) LogLines.RemoveAt(0);
        }
    }

    private void UpdateCommandPreview()
    {
        try
        {
            var spec = CommandBuilder.Build(Settings, ComfyPathResolved, EffectivePython);
            CommandPreview = spec.Display;
        }
        catch (Exception ex)
        {
            CommandPreview = "生成命令失败: " + ex.Message;
        }
        OnPropertyChanged(nameof(ServerUrl));
        OnPropertyChanged(nameof(EffectivePython));
        OnPropertyChanged(nameof(ComfyPathResolved));
        OnPropertyChanged(nameof(PythonPathResolved));
        OnPropertyChanged(nameof(VenvPathResolved));
        OnPropertyChanged(nameof(VenvPythonPath));
        OnPropertyChanged(nameof(UserPathResolved));
        OnPropertyChanged(nameof(OutputPathResolved));
        OnPropertyChanged(nameof(InputPathResolved));
        OnPropertyChanged(nameof(ModelsPathResolved));
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateCommandPreview();
        if (e.PropertyName is nameof(AppSettings.ModelsDirectory) or nameof(AppSettings.ComfyPath))
        {
            RefreshModels();
        }
    }

    private void StartUptimeTimer()
    {
        StopUptimeTimer();
        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) =>
        {
            if (!IsRunning)
            {
                StopUptimeTimer();
                return;
            }
            var elapsed = DateTime.Now - _startedAt;
            UptimeText = $"运行 {elapsed:hh\\:mm\\:ss}  ·  PID {_runner.Pid?.ToString() ?? "-"}";
        };
        _uptimeTimer.Start();
        UptimeText = "运行 00:00:00";
    }

    private void StopUptimeTimer()
    {
        _uptimeTimer?.Stop();
        _uptimeTimer = null;
        UptimeText = "";
    }

    private void RefreshCommands()
    {
        if (LaunchCommand == null) return;
        LaunchCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RestartCommand.RaiseCanExecuteChanged();
        CheckEnvironmentCommand.RaiseCanExecuteChanged();
        SetupAllCommand.RaiseCanExecuteChanged();
        SetupPythonCommand.RaiseCanExecuteChanged();
        SetupVenvCommand.RaiseCanExecuteChanged();
        InstallTorchCommand.RaiseCanExecuteChanged();
        DownloadModelsCommand.RaiseCanExecuteChanged();
        CloneComfyCommand.RaiseCanExecuteChanged();
    }

    private static string Q(string value) => ProcessUtil.Quote(value);

    public async Task ShutdownAsync()
    {
        _stopRequested = true;
        _monitorCts?.Cancel();
        await _runner.StopAsync();
        SettingsService.Save(Settings);
    }
}
