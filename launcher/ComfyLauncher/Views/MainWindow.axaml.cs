using System.Collections.Specialized;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ComfyLauncher.ViewModels;

namespace ComfyLauncher.Views;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private readonly MainViewModel _viewModel;
    private bool _closing;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _viewModel.FolderPicker = PickFolderAsync;
        _viewModel.FilePicker = PickFileAsync;
        _viewModel.ClipboardWriter = text =>
        {
            if (Clipboard != null) _ = Clipboard.SetTextAsync(text);
        };
        _viewModel.LogLines.CollectionChanged += OnLogLinesChanged;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.IsLightTheme) or nameof(MainViewModel.IsDarkTheme))
            {
                UpdateWindowIcon();
                ApplyTitleBarTheme();
            }
        };
        UpdateWindowIcon();

        Opened += OnOpened;
        Closing += OnClosing;
        SizeChanged += (_, _) => UpdateLayoutLimits();
    }

    private void UpdateWindowIcon()
    {
        try
        {
            var asset = _viewModel.IsLightTheme
                ? "avares://ComfyLauncher/Assets/logomark-ink.ico"
                : "avares://ComfyLauncher/Assets/logomark-yellow.ico";
            using var stream = AssetLoader.Open(new Uri(asset));
            Icon = new WindowIcon(stream);
        }
        catch
        {
            // 图标缺失不影响启动；保留默认窗口图标。
        }
    }

    private void ApplyTitleBarTheme()
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        var dark = _viewModel.IsLightTheme ? 0 : 1;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref dark, sizeof(int));
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        UpdateLayoutLimits();
        ApplyTitleBarTheme();
        Dispatcher.UIThread.Post(() =>
        {
            UpdateLayoutLimits();
            if (LaunchScroll != null) LaunchScroll.Offset = new Avalonia.Vector(0, 0);
        }, DispatcherPriority.Loaded);
        await _viewModel.InitializeAsync();
    }

    /// <summary>
    /// TabControl 的内容（ScrollViewer）在某些 DPI/尺寸下会按内容高度撑开，
    /// 把内嵌控制台和状态栏挤出窗口。这里按固定面板的实际高度限制选项卡高度。
    /// </summary>
    private void UpdateLayoutLimits()
    {
        if (MainTabs == null) return;
        var header = HeaderBar?.Bounds.Height ?? 0;
        var console = ConsoleHost?.Bounds.Height ?? 0;
        var status = StatusBar?.Bounds.Height ?? 0;
        if (header <= 0) header = 60;
        if (console <= 0) console = 170;
        if (status <= 0) status = 24;
        var fixedHeight = header + console + status + 45;
        MainTabs.MaxHeight = Math.Max(200, Bounds.Height - fixedHeight);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing) return;
        _closing = true;
        e.Cancel = true;
        await _viewModel.ShutdownAsync();
        Close();
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        if (!_viewModel.Settings.AutoScrollConsole) return;
        var index = _viewModel.LogLines.Count - 1;
        if (index < 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (index < LogList.ItemCount) LogList.ScrollIntoView(index);
        }, DispatcherPriority.Background);
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private async Task<string?> PickFileAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("可执行文件") { Patterns = new[] { "*.exe" } },
                FilePickerFileTypes.All
            }
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
