#!/usr/bin/env python3
"""通过 ComfyUI HTTP API 批量执行 4x 超分。

示例:
    python scripts/batch_upscale.py input\\*.jpg
    python scripts/batch_upscale.py .\\photos --recursive --model 4x-UltraSharp.pth
    python scripts/batch_upscale.py photo.png --server http://127.0.0.1:8188

只依赖 Python 标准库；ComfyUI 服务需先启动。
"""
from __future__ import annotations

import argparse
import glob
import json
import mimetypes
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff"}
DEFAULT_MODEL = "4x-UltraSharp.pth"


def log(message: str) -> None:
    print(message, flush=True)


def request_json(url: str, payload: dict | None = None, timeout: float = 60) -> dict:
    data = None
    headers = {}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as response:
            body = response.read()
            return json.loads(body) if body else {}
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", "replace")
        raise RuntimeError(f"HTTP {exc.code} {url}: {detail[:1000]}") from exc


def upload_image(server: str, path: Path) -> str:
    boundary = "----ComfyBatch" + uuid.uuid4().hex
    content_type = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
    chunks = [
        f"--{boundary}\r\n".encode(),
        f'Content-Disposition: form-data; name="image"; filename="{path.name}"\r\n'.encode(),
        f"Content-Type: {content_type}\r\n\r\n".encode(),
        path.read_bytes(),
        f"\r\n--{boundary}\r\n".encode(),
        b'Content-Disposition: form-data; name="overwrite"\r\n\r\ntrue',
        f"\r\n--{boundary}\r\n".encode(),
        b'Content-Disposition: form-data; name="type"\r\n\r\ninput',
        f"\r\n--{boundary}--\r\n".encode(),
    ]
    req = urllib.request.Request(
        server + "/upload/image",
        data=b"".join(chunks),
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
    )
    with urllib.request.urlopen(req, timeout=120) as response:
        info = json.loads(response.read())
    name = info.get("name")
    if not name:
        raise RuntimeError(f"上传响应缺少 name: {info}")
    subfolder = (info.get("subfolder") or "").strip("/")
    return f"{subfolder}/{name}" if subfolder else name


def build_workflow(image_ref: str, model: str, prefix: str) -> dict:
    template = Path(__file__).resolve().parent.parent / "workflows" / "api_upscale_4x.json"
    if template.exists():
        workflow = json.loads(template.read_text(encoding="utf-8"))
    else:
        workflow = {
            "1": {"class_type": "LoadImage", "inputs": {"image": "example.png"}},
            "2": {"class_type": "UpscaleModelLoader", "inputs": {"model_name": model}},
            "3": {
                "class_type": "ImageUpscaleWithModel",
                "inputs": {"upscale_model": ["2", 0], "image": ["1", 0]},
            },
            "4": {
                "class_type": "SaveImage",
                "inputs": {"images": ["3", 0], "filename_prefix": prefix},
            },
        }

    workflow["1"]["inputs"]["image"] = image_ref
    workflow["2"]["inputs"]["model_name"] = model
    workflow["4"]["inputs"]["filename_prefix"] = prefix
    return workflow


def queue_prompt(server: str, workflow: dict) -> str:
    response = request_json(
        server + "/prompt",
        {"prompt": workflow, "client_id": uuid.uuid4().hex},
    )
    if "prompt_id" not in response:
        raise RuntimeError(f"提交失败: {response}")
    return response["prompt_id"]


def describe_status(status: dict) -> str:
    messages = status.get("messages") or []
    parts = []
    for item in messages:
        if isinstance(item, (list, tuple)) and len(item) >= 2:
            kind, data = item[0], item[1]
            if isinstance(data, dict):
                text = data.get("exception_message") or data.get("message") or data.get("node_type")
                if text:
                    parts.append(f"{kind}: {text}")
            elif data:
                parts.append(f"{kind}: {data}")
    return "; ".join(parts) or "执行失败（无详细信息）"


def wait_for_history(server: str, prompt_id: str, timeout: float) -> dict:
    deadline = time.time() + timeout
    url = f"{server}/history/{prompt_id}"
    while time.time() < deadline:
        history = request_json(url, timeout=30)
        if prompt_id in history:
            entry = history[prompt_id]
            status = entry.get("status") or {}
            if status.get("status_str") == "error":
                raise RuntimeError(describe_status(status))
            return entry
        time.sleep(1.0)
    raise TimeoutError(f"等待 {prompt_id} 超时（{timeout:.0f}s）")


def download_outputs(server: str, entry: dict, destination: Path, stem: str) -> list[Path]:
    saved = []
    images = []
    for node_output in (entry.get("outputs") or {}).values():
        images.extend(node_output.get("images") or [])

    if not images:
        raise RuntimeError("任务完成但没有输出图片")

    destination.mkdir(parents=True, exist_ok=True)
    for index, image in enumerate(images):
        query = urllib.parse.urlencode({
            "filename": image.get("filename", ""),
            "subfolder": image.get("subfolder", ""),
            "type": image.get("type", "output"),
        })
        with urllib.request.urlopen(f"{server}/view?{query}", timeout=120) as response:
            data = response.read()
        suffix = Path(image.get("filename", ".png")).suffix or ".png"
        target = destination / f"{stem}_4x{suffix}"
        if index > 0:
            target = destination / f"{stem}_4x_{index + 1}{suffix}"
        if target.exists():
            target = destination / f"{stem}_4x_{int(time.time())}{suffix}"
        target.write_bytes(data)
        saved.append(target)
    return saved


def list_models(server: str) -> None:
    info = request_json(server + "/object_info/UpscaleModelLoader", timeout=30)
    node = info.get("UpscaleModelLoader", {})
    required = (node.get("input") or {}).get("required") or {}
    options = required.get("model_name")
    if isinstance(options, list) and options and isinstance(options[0], list):
        log("可用的超分模型:")
        for name in options[0]:
            log("  " + name)
    else:
        log(json.dumps(node, ensure_ascii=False, indent=2)[:2000])


def expand_inputs(patterns: list[str], recursive: bool) -> list[Path]:
    files: list[Path] = []
    for pattern in patterns:
        matches = glob.glob(pattern, recursive=recursive)
        if not matches and Path(pattern).exists():
            matches = [pattern]
        for match in matches:
            path = Path(match)
            if path.is_dir():
                iterator = path.rglob("*") if recursive else path.glob("*")
                files.extend(p for p in iterator if p.is_file() and p.suffix.lower() in IMAGE_EXTENSIONS)
            elif path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS:
                files.append(path)
            else:
                log(f"[跳过] 非图片或不存在: {pattern}")
    seen = set()
    unique = []
    for path in files:
        resolved = path.resolve()
        if resolved not in seen:
            seen.add(resolved)
            unique.append(path)
    return unique


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description="ComfyUI 批量 4x 超分")
    parser.add_argument("inputs", nargs="*", help="图片文件、目录或通配符")
    parser.add_argument("--server", default="http://127.0.0.1:8188")
    parser.add_argument("--model", default=DEFAULT_MODEL, help="upscale_models 目录中的模型文件名")
    parser.add_argument("--prefix", default="upscaled/ComfyUI")
    parser.add_argument("--output-dir", default=str(root / "output" / "upscaled"))
    parser.add_argument("--recursive", action="store_true", help="递归扫描目录")
    parser.add_argument("--timeout", type=float, default=600, help="单张图片超时（秒）")
    parser.add_argument("--retries", type=int, default=1)
    parser.add_argument("--overwrite", action="store_true", help="目标文件已存在时仍然重做")
    parser.add_argument("--list-models", action="store_true", help="列出服务端可用的超分模型后退出")
    args = parser.parse_args()

    server = args.server.rstrip("/")
    try:
        request_json(server + "/system_stats", timeout=10)
    except Exception as exc:
        log(f"无法连接 ComfyUI: {server} ({exc})")
        log("请先在启动器中启动服务，或运行 scripts\\start-comfy.ps1。")
        return 2

    if args.list_models:
        list_models(server)
        return 0

    if not args.inputs:
        parser.error("请提供至少一张图片或一个目录")

    files = expand_inputs(args.inputs, args.recursive)
    if not files:
        log("没有找到可处理的图片。")
        return 1

    output_dir = Path(args.output_dir)
    log(f"ComfyUI: {server}")
    log(f"模型: {args.model}")
    log(f"输出: {output_dir}")
    log(f"待处理: {len(files)} 张")

    succeeded = 0
    failed = 0
    for index, path in enumerate(files, start=1):
        destination = output_dir / f"{path.stem}_4x.png"
        if destination.exists() and not args.overwrite:
            log(f"[{index}/{len(files)}] 已存在，跳过: {destination.name}")
            succeeded += 1
            continue

        log(f"[{index}/{len(files)}] {path}")
        last_error: Exception | None = None
        for attempt in range(1, args.retries + 2):
            try:
                image_ref = upload_image(server, path)
                workflow = build_workflow(image_ref, args.model, args.prefix)
                prompt_id = queue_prompt(server, workflow)
                entry = wait_for_history(server, prompt_id, args.timeout)
                saved = download_outputs(server, entry, output_dir, path.stem)
                log("    -> " + ", ".join(str(p) for p in saved))
                succeeded += 1
                last_error = None
                break
            except Exception as exc:
                last_error = exc
                log(f"    尝试 {attempt} 失败: {exc}")
                time.sleep(1.5)
        if last_error is not None:
            failed += 1

    log("")
    log(f"完成: 成功 {succeeded}，失败 {failed}")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
