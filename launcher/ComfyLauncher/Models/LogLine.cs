using System.ComponentModel;
using Avalonia.Media;

namespace ComfyLauncher.Models;

public enum LogKind
{
    Out,
    Err,
    System,
    Success,
    Warn
}

public sealed class LogLine : INotifyPropertyChanged
{
    private static readonly IBrush DarkOut = new SolidColorBrush(Color.Parse("#D6DEE8"));
    private static readonly IBrush DarkErr = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush DarkSystem = new SolidColorBrush(Color.Parse("#7DD3FC"));
    private static readonly IBrush DarkSuccess = new SolidColorBrush(Color.Parse("#4ADE80"));
    private static readonly IBrush DarkWarn = new SolidColorBrush(Color.Parse("#FBBF24"));

    private static readonly IBrush LightOut = new SolidColorBrush(Color.Parse("#1E293B"));
    private static readonly IBrush LightErr = new SolidColorBrush(Color.Parse("#DC2626"));
    private static readonly IBrush LightSystem = new SolidColorBrush(Color.Parse("#0369A1"));
    private static readonly IBrush LightSuccess = new SolidColorBrush(Color.Parse("#15803D"));
    private static readonly IBrush LightWarn = new SolidColorBrush(Color.Parse("#B45309"));

    private IBrush _foreground = DarkOut;

    public LogLine(LogKind kind, string text)
    {
        Kind = kind;
        Text = text;
        Time = DateTime.Now.ToString("HH:mm:ss");
    }

    public LogKind Kind { get; }
    public string Text { get; }
    public string Time { get; }

    public string Tag => Kind switch
    {
        LogKind.Err => "ERR",
        LogKind.System => "SYS",
        LogKind.Success => "OK",
        LogKind.Warn => "WARN",
        _ => "OUT"
    };

    public IBrush Foreground
    {
        get => _foreground;
        private set
        {
            if (ReferenceEquals(_foreground, value)) return;
            _foreground = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Foreground)));
        }
    }

    /// <summary>主题切换时刷新已有日志的颜色，避免深色文字留在浅色控制台里。</summary>
    public void ApplyTheme(bool light)
    {
        Foreground = (Kind, light) switch
        {
            (LogKind.Err, true) => LightErr,
            (LogKind.System, true) => LightSystem,
            (LogKind.Success, true) => LightSuccess,
            (LogKind.Warn, true) => LightWarn,
            (_, true) => LightOut,
            (LogKind.Err, false) => DarkErr,
            (LogKind.System, false) => DarkSystem,
            (LogKind.Success, false) => DarkSuccess,
            (LogKind.Warn, false) => DarkWarn,
            _ => DarkOut
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public enum StatusState
{
    Unknown,
    Ok,
    Warn,
    Fail,
    Busy
}

public sealed class StatusItem
{
    public StatusItem(string name, string detail, StatusState state)
    {
        Name = name;
        Detail = detail;
        State = state;
    }

    public string Name { get; }
    public string Detail { get; }
    public StatusState State { get; }

    public string Icon => State switch
    {
        StatusState.Ok => "●",
        StatusState.Warn => "▲",
        StatusState.Fail => "✕",
        StatusState.Busy => "◌",
        _ => "○"
    };
}

public sealed class ModelItem
{
    public ModelItem(string name, string type, long bytes, string path)
    {
        Name = name;
        Type = type;
        Bytes = bytes;
        Path = path;
    }

    public string Name { get; }
    public string Type { get; }
    public long Bytes { get; }
    public string Path { get; }

    public string SizeText => Bytes >= 1024L * 1024 * 1024
        ? $"{Bytes / 1024.0 / 1024 / 1024:F2} GB"
        : $"{Bytes / 1024.0 / 1024:F1} MB";
}
