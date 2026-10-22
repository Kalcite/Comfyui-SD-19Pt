using ComfyLauncher.Infrastructure;

namespace ComfyLauncher.Models;

/// <summary>ComfyUI 模型目录分类（Checkpoint、扩散模型、文本编码器、VAE、LoRA 等）。</summary>
public sealed class ModelCategory : ObservableObject
{
    private int _count;
    private string _filesPreview = "";

    public ModelCategory(string key, string name, string path, string description, string sample, Action openFolder)
    {
        Key = key;
        Name = name;
        Path = path;
        Description = description;
        Sample = sample;
        OpenCommand = new RelayCommand(openFolder);
    }

    public string Key { get; }
    public string Name { get; }
    public string Path { get; }
    public string Description { get; }
    public string Sample { get; }
    public RelayCommand OpenCommand { get; }

    public int Count
    {
        get => _count;
        set
        {
            if (!Set(ref _count, value)) return;
            OnPropertyChanged(nameof(CountText));
        }
    }

    public string CountText => Count == 0 ? "空目录" : $"{Count} 个文件";

    public string SampleText => "样例: " + Sample;

    public string FilesPreview
    {
        get => _filesPreview;
        set => Set(ref _filesPreview, value);
    }
}
