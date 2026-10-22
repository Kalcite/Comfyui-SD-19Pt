using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ComfyLauncher.Models;

/// <summary>启动器全部可持久化配置。空路径表示使用项目默认值。</summary>
public sealed class AppSettings : INotifyPropertyChanged
{
    public string ProjectRoot { get; set; } = "";

    private string _comfyPath = "";
    private string _pythonPath = "";
    private string _venvPath = "";
    private string _baseDirectory = "";
    private string _userDirectory = "";
    private string _modelsDirectory = "";
    private string _outputDirectory = "";
    private string _inputDirectory = "";
    private string _port = "8188";
    private string _listenAddress = "127.0.0.1";
    private bool _listenAll;
    private string _vramMode = "自动";
    private string _precision = "自动";
    private string _previewMethod = "无";
    private string _previewSize = "512";
    private string _attention = "默认";
    private bool _disableSmartMemory;
    private string _verboseLevel = "默认";
    private string _extraArgs = "";
    private bool _autoOpenBrowser = true;
    private bool _autoRestart;
    private string _consoleMaxLines = "4000";
    private bool _autoScrollConsole = true;
    private string _gitProxy = "http://127.0.0.1:10108";
    private string _gitTimeoutSeconds = "15";
    private string _gitMinSpeedKBps = "80";
    private bool _gitUseProxyFallback = true;
    private string _torchIndex = "https://download.pytorch.org/whl/cu130";
    private string _pipIndex = "";
    private string _hfMirror = "";
    private bool _keepConsoleOnRestart = true;
    private string _theme = "Dark";

    public string ComfyPath { get => _comfyPath; set => Set(ref _comfyPath, value); }
    public string PythonPath { get => _pythonPath; set => Set(ref _pythonPath, value); }
    public string VenvPath { get => _venvPath; set => Set(ref _venvPath, value); }
    public string BaseDirectory { get => _baseDirectory; set => Set(ref _baseDirectory, value); }
    public string UserDirectory { get => _userDirectory; set => Set(ref _userDirectory, value); }
    public string ModelsDirectory { get => _modelsDirectory; set => Set(ref _modelsDirectory, value); }
    public string OutputDirectory { get => _outputDirectory; set => Set(ref _outputDirectory, value); }
    public string InputDirectory { get => _inputDirectory; set => Set(ref _inputDirectory, value); }
    public string Port { get => _port; set => Set(ref _port, value); }
    public string ListenAddress { get => _listenAddress; set => Set(ref _listenAddress, value); }
    public bool ListenAll { get => _listenAll; set => Set(ref _listenAll, value); }
    public string VramMode { get => _vramMode; set => Set(ref _vramMode, value); }
    public string Precision { get => _precision; set => Set(ref _precision, value); }
    public string PreviewMethod { get => _previewMethod; set => Set(ref _previewMethod, value); }
    public string PreviewSize { get => _previewSize; set => Set(ref _previewSize, value); }
    public string Attention { get => _attention; set => Set(ref _attention, value); }
    public bool DisableSmartMemory { get => _disableSmartMemory; set => Set(ref _disableSmartMemory, value); }
    public string VerboseLevel { get => _verboseLevel; set => Set(ref _verboseLevel, value); }
    public string ExtraArgs { get => _extraArgs; set => Set(ref _extraArgs, value); }
    public bool AutoOpenBrowser { get => _autoOpenBrowser; set => Set(ref _autoOpenBrowser, value); }
    public bool AutoRestart { get => _autoRestart; set => Set(ref _autoRestart, value); }
    public string ConsoleMaxLines { get => _consoleMaxLines; set => Set(ref _consoleMaxLines, value); }
    public bool AutoScrollConsole { get => _autoScrollConsole; set => Set(ref _autoScrollConsole, value); }
    public string GitProxy { get => _gitProxy; set => Set(ref _gitProxy, value); }
    public string GitTimeoutSeconds { get => _gitTimeoutSeconds; set => Set(ref _gitTimeoutSeconds, value); }
    public string GitMinSpeedKBps { get => _gitMinSpeedKBps; set => Set(ref _gitMinSpeedKBps, value); }
    public bool GitUseProxyFallback { get => _gitUseProxyFallback; set => Set(ref _gitUseProxyFallback, value); }
    public string TorchIndex { get => _torchIndex; set => Set(ref _torchIndex, value); }
    public string PipIndex { get => _pipIndex; set => Set(ref _pipIndex, value); }
    public string HfMirror { get => _hfMirror; set => Set(ref _hfMirror, value); }
    public bool KeepConsoleOnRestart { get => _keepConsoleOnRestart; set => Set(ref _keepConsoleOnRestart, value); }
    public string Theme { get => _theme; set => Set(ref _theme, value); }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public void CopyFrom(AppSettings other)
    {
        foreach (var p in typeof(AppSettings).GetProperties())
        {
            if (p.CanRead && p.CanWrite && p.PropertyType == typeof(string) && p.Name != nameof(ProjectRoot))
            {
                var v = (string?)p.GetValue(other) ?? "";
                if ((string?)p.GetValue(this) != v) p.SetValue(this, v);
            }
            else if (p.CanRead && p.CanWrite && p.PropertyType == typeof(bool))
            {
                var v = (bool)p.GetValue(other)!;
                if ((bool)p.GetValue(this)! != v) p.SetValue(this, v);
            }
        }
    }

    public int GetInt(string value, int fallback)
        => int.TryParse(value, out var n) ? n : fallback;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
