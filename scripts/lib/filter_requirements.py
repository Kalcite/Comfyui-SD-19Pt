"""为 ComfyUI 依赖安装生成过滤后的 requirements 文件。

用法: python filter_requirements.py <源文件> <目标文件> <uv|pip>
  uv : 排除 torch / torchvision / torchaudio（避免覆盖已安装的 CUDA 版本）
  pip: 排除可选的 comfyui-workflow-templates 元包（避免解析器长时间回溯）
"""
import sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) != 4:
        print("usage: filter_requirements.py <src> <dst> <uv|pip>", file=sys.stderr)
        return 2
    src, dst, mode = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3].lower()
    lines = src.read_text(encoding="utf-8").splitlines()
    if mode == "uv":
        drop = {"torch", "torchvision", "torchaudio"}
        keep = [line for line in lines if line.strip() not in drop]
    else:
        keep = [line for line in lines
                if not line.strip().lower().startswith("comfyui-workflow-templates")]
    dst.write_text("\n".join(keep) + "\n", encoding="utf-8", newline="\n")
    print(f"[filter] {src.name} -> {dst.name}: {len(lines)} -> {len(keep)} lines ({mode})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
