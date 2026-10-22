using ComfyLauncher.Models;

namespace ComfyLauncher.Services;

public sealed record CommandSpec(string Arguments, string Display, string WorkingDirectory);

public static class CommandBuilder
{
    public static CommandSpec Build(AppSettings settings, string comfyDirectory, string pythonExecutable)
    {
        var args = new List<string> { "main.py" };

        void Flag(string name)
        {
            if (!args.Contains(name)) args.Add(name);
        }

        void Option(string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            args.Add(name);
            args.Add(ProcessUtil.Quote(value));
        }

        var port = settings.GetInt(settings.Port, 8188);
        if (port <= 0 || port > 65535) port = 8188;

        if (settings.ListenAll)
        {
            Option("--listen", "0.0.0.0");
        }
        else if (!string.IsNullOrWhiteSpace(settings.ListenAddress) &&
                 settings.ListenAddress != "127.0.0.1" &&
                 settings.ListenAddress != "localhost")
        {
            Option("--listen", settings.ListenAddress);
        }

        args.Add("--port");
        args.Add(port.ToString());

        Option("--base-directory", settings.BaseDirectory);
        Option("--user-directory", settings.UserDirectory);
        Option("--models-directory", settings.ModelsDirectory);
        Option("--output-directory", settings.OutputDirectory);
        Option("--input-directory", settings.InputDirectory);

        var vram = settings.VramMode ?? "";
        if (vram.StartsWith("低显存")) Flag("--lowvram");
        else if (vram.StartsWith("普通显存")) Flag("--normalvram");
        else if (vram.StartsWith("高显存")) Flag("--highvram");
        else if (vram.StartsWith("不保留")) Flag("--novram");
        else if (vram.StartsWith("CPU")) Flag("--cpu");

        var precision = settings.Precision ?? "";
        if (precision.StartsWith("强制 FP16")) Flag("--force-fp16");
        else if (precision.StartsWith("强制 FP32")) Flag("--force-fp32");
        else if (precision.StartsWith("VAE FP16")) Flag("--fp16-vae");
        else if (precision.StartsWith("VAE FP32")) Flag("--fp32-vae");
        else if (precision.StartsWith("VAE BF16")) Flag("--bf16-vae");

        var preview = settings.PreviewMethod ?? "无";
        if (preview != "无" && !string.IsNullOrWhiteSpace(preview))
        {
            Option("--preview-method", preview);
            var size = settings.GetInt(settings.PreviewSize, 512);
            if (size is >= 64 and <= 4096)
            {
                args.Add("--preview-size");
                args.Add(size.ToString());
            }
        }

        var attention = settings.Attention ?? "";
        if (attention.Contains("PyTorch")) Flag("--use-pytorch-cross-attention");
        else if (attention.Contains("Split")) Flag("--use-split-cross-attention");
        else if (attention.Contains("Quad")) Flag("--use-quad-cross-attention");
        else if (attention.Contains("禁用 xFormers")) Flag("--disable-xformers");

        if (settings.DisableSmartMemory) Flag("--disable-smart-memory");

        var torchProfile = settings.TorchProfile ?? "";
        if (torchProfile.Contains("DirectML", StringComparison.OrdinalIgnoreCase)) Flag("--directml");
        else if (torchProfile.StartsWith("CPU", StringComparison.OrdinalIgnoreCase)) Flag("--cpu");

        var verbose = settings.VerboseLevel ?? "";
        if (verbose.StartsWith("调试"))
        {
            args.Add("--verbose");
            args.Add("DEBUG");
        }
        else if (verbose.StartsWith("详细"))
        {
            args.Add("--verbose");
        }
        else if (verbose.StartsWith("仅警告"))
        {
            args.Add("--verbose");
            args.Add("WARNING");
        }

        if (!string.IsNullOrWhiteSpace(settings.ExtraArgs))
        {
            args.Add(settings.ExtraArgs.Trim());
        }

        var arguments = string.Join(" ", args);
        var display = $"{ProcessUtil.Quote(pythonExecutable)} {arguments}";
        return new CommandSpec(arguments, display, comfyDirectory);
    }
}
