#!/usr/bin/env python3
"""可续传、带进度与重试的文件下载器（ComfyUI 部署用）。

用法:
    python download.py <url> <输出文件> [--sha256 HEX] [--proxy URL]
                       [--retries N] [--timeout SEC] [--no-resume]

- 默认使用 .part 临时文件，支持 HTTP Range 断点续传；
- 每秒刷新进度、速度与预计剩余时间；
- 目标文件已存在且大小与远端一致时直接跳过；
- 仅在完整校验通过后重命名，失败不会留下半成品。
"""
from __future__ import annotations

import argparse
import hashlib
import os
import re
import shutil
import sys
import time
import urllib.error
import urllib.request

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ComfyUI-Launcher/1.0"
CHUNK = 1024 * 256


def human(n: float) -> str:
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if abs(n) < 1024.0 or unit == "TB":
            return f"{n:.2f} {unit}" if unit != "B" else f"{int(n)} B"
        n /= 1024.0
    return f"{n:.2f} TB"


def make_opener(proxy: str | None):
    """proxy=None 使用系统/环境代理；proxy=\"\" 强制直连；否则使用指定代理。"""
    if proxy is None:
        return urllib.request.build_opener()
    if proxy == "":
        return urllib.request.build_opener(urllib.request.ProxyHandler({}))
    return urllib.request.build_opener(
        urllib.request.ProxyHandler({"http": proxy, "https": proxy})
    )


def remote_size(url: str, timeout: float, proxy: str | None) -> int | None:
    opener = make_opener(proxy)
    req = urllib.request.Request(url, method="HEAD", headers={"User-Agent": UA})
    try:
        with opener.open(req, timeout=timeout) as r:
            length = r.headers.get("Content-Length")
            return int(length) if length and length.isdigit() else None
    except Exception:
        return None


def download(url: str, out: str, sha256: str | None, proxy: str | None,
             retries: int, timeout: float, resume: bool) -> int:
    os.makedirs(os.path.dirname(os.path.abspath(out)) or ".", exist_ok=True)
    part = out + ".part"
    expected = remote_size(url, timeout, proxy)
    if os.path.exists(out):
        size = os.path.getsize(out)
        if expected is None or size == expected:
            if sha256:
                print(f"[download] 已存在，校验 {out} ...")
                if sha256_file(out).lower() != sha256.lower():
                    print("[download] 校验失败，重新下载")
                    os.remove(out)
                else:
                    print(f"[download] 已存在且校验通过: {out}")
                    return 0
            else:
                print(f"[download] 已存在，跳过: {out} ({human(size)})")
                return 0

    opener = make_opener(proxy)
    print("[download] 网络路径: " + ("直连" if proxy == "" else proxy if proxy else "系统代理/默认"))
    for attempt in range(1, retries + 2):
        offset = os.path.getsize(part) if (resume and os.path.exists(part)) else 0
        if offset and expected is not None and offset == expected:
            break
        headers = {"User-Agent": UA, "Accept": "*/*"}
        if offset:
            headers["Range"] = f"bytes={offset}-"
        req = urllib.request.Request(url, headers=headers)
        try:
            print(f"[download] 尝试 {attempt}/{retries + 1}: {url}")
            with opener.open(req, timeout=timeout) as r:
                status = getattr(r, "status", 200)
                mode = "ab"
                if offset and status == 200:
                    # 服务器不支持 Range，从头开始
                    offset = 0
                    mode = "wb"
                total = r.headers.get("Content-Length")
                total = int(total) + offset if total and total.isdigit() else expected
                print(f"[download] HTTP {status}, 已有 {human(offset)}"
                      + (f", 总计 {human(total)}" if total else ""))
                done = offset
                last = time.time()
                last_bytes = done
                with open(part, mode) as f:
                    while True:
                        chunk = r.read(CHUNK)
                        if not chunk:
                            break
                        f.write(chunk)
                        done += len(chunk)
                        now = time.time()
                        if now - last >= 1.0:
                            speed = (done - last_bytes) / (now - last)
                            eta = ""
                            if total and speed > 0:
                                eta = f", 剩余 {human((total - done) / speed)}"
                            pct = f"{(done * 100.0 / total):5.1f}%" if total else "  ?  "
                            print(f"\r[download] {pct} {human(done)}"
                                  f"{'/' + human(total) if total else ''}"
                                  f" @ {human(speed)}/s{eta}      ", end="", flush=True)
                            last, last_bytes = now, done
                print()
                size = os.path.getsize(part)
                if total is not None and size < total:
                    raise IOError(f"连接提前结束: {size}/{total}")
                break
        except (urllib.error.URLError, IOError, TimeoutError, OSError) as exc:
            print(f"\n[download] 失败: {exc}")
            if attempt > retries:
                print("[download] 重试次数用尽")
                return 1
            wait = min(30, 3 * attempt)
            print(f"[download] {wait}s 后重试（保留已下载的 {human(os.path.getsize(part) if os.path.exists(part) else 0)}）")
            time.sleep(wait)

    if sha256:
        print("[download] 计算 SHA-256 ...")
        if sha256_file(part).lower() != sha256.lower():
            print("[download] SHA-256 不匹配，删除 .part")
            os.remove(part)
            return 2
    os.replace(part, out)
    print(f"[download] 完成: {out} ({human(os.path.getsize(out))})")
    return 0


def sha256_file(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def main() -> int:
    ap = argparse.ArgumentParser(description="可续传下载器")
    ap.add_argument("url")
    ap.add_argument("out")
    ap.add_argument("--sha256", default=None)
    ap.add_argument("--proxy", default=None, help="例如 http://127.0.0.1:10808；缺省使用系统代理")
    ap.add_argument("--no-proxy", action="store_true", help="强制直连，不使用任何代理")
    ap.add_argument("--retries", type=int, default=4)
    ap.add_argument("--timeout", type=float, default=60)
    ap.add_argument("--no-resume", action="store_true")
    args = ap.parse_args()
    proxy = "" if args.no_proxy else args.proxy
    try:
        return download(args.url, args.out, args.sha256, proxy,
                        args.retries, args.timeout, not args.no_resume)
    except KeyboardInterrupt:
        print("\n[download] 已中断，保留 .part 以便续传")
        return 130


if __name__ == "__main__":
    sys.exit(main())
