using Avalonia.Data.Converters;
using Avalonia.Media;
using ComfyLauncher.Models;
using System.Globalization;

namespace ComfyLauncher.Infrastructure;

public sealed class LogKindToBrushConverter : IValueConverter
{
    private static readonly IBrush Out = new SolidColorBrush(Color.Parse("#D6DEE8"));
    private static readonly IBrush Err = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush System = new SolidColorBrush(Color.Parse("#7DD3FC"));
    private static readonly IBrush Success = new SolidColorBrush(Color.Parse("#4ADE80"));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#FBBF24"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            LogKind.Err => Err,
            LogKind.System => System,
            LogKind.Success => Success,
            LogKind.Warn => Warn,
            _ => Out
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class StatusStateToBrushConverter : IValueConverter
{
    private static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#4ADE80"));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#FBBF24"));
    private static readonly IBrush Fail = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush Busy = new SolidColorBrush(Color.Parse("#7DD3FC"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            StatusState.Ok => Ok,
            StatusState.Warn => Warn,
            StatusState.Fail => Fail,
            StatusState.Busy => Busy,
            _ => Warn
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToBrushConverter : IValueConverter
{
    private static readonly IBrush On = new SolidColorBrush(Color.Parse("#4ADE80"));
    private static readonly IBrush Off = new SolidColorBrush(Color.Parse("#94A3B8"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? On : Off;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
