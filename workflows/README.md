# Workflows / 内置工作流

本目录包含可直接在 ComfyUI 中打开的界面版工作流，以及供脚本/API 调用的 API 版。全部由项目维护，不依赖第三方工作流包。

已在 ComfyUI 0.42.0 / PyTorch 2.9.1+cu130 上实际执行验证的工作流：

- SD1.5 文生图
- SD1.5 图生图
- SD1.5 高清修复（512 → 1024）
- 4x 超分

Flux 工作流已按 ComfyUI 节点格式生成并做结构检查，但本地未放置 Flux 大模型，未执行出图；需要自备模型。

## 文件一览

| 文件 | 类型 | 用途 | 默认模型/参数 | 输出 |
| --- | --- | --- | --- | --- |
| `txt2img_sd15.json` | 界面 | SD1.5 文生图 | 512×512，25 步，CFG 7，euler/normal | `output/sd15/` |
| `img2img_sd15.json` | 界面 | SD1.5 图生图 | 输入缩放到 512，denoise 0.6 | `output/sd15/` |
| `txt2img_hires_sd15.json` | 界面 | 文生图 + 2 倍潜空间高清修复 | 512 → 1024，高清阶段 denoise 0.55 | `output/sd15/` |
| `upscale_4x.json` | 界面 | 4 倍超分 | `4x-UltraSharp.pth` | `output/upscaled/` |
| `flux_txt2img.json` | 界面 | Flux.1-dev 文生图（可选） | 1024×1024，20 步，CFG 1，guidance 3.5 | `output/flux/` |
| `api_txt2img_sd15.json` | API | 文生图 API 版 | 同界面版 | `output/sd15/` |
| `api_img2img_sd15.json` | API | 图生图 API 版 | 输入 `example.png` | `output/sd15/` |
| `api_txt2img_hires_sd15.json` | API | 高清修复 API 版 | 512 → 1024 | `output/sd15/` |
| `api_upscale_4x.json` | API | 4x 超分 API 版 | `4x-UltraSharp.pth` | `output/upscaled/` |
| `api_flux_txt2img.json` | API | Flux API 版（可选） | 需 Flux 模型 | `output/flux/` |

> ComfyUI 启动器配置的输出目录是 `I:\Comfyui_Graph\output`；如果使用默认 ComfyUI 配置，可能在 `ComfyUI\output`。以实际启动参数为准。

## 在 ComfyUI 界面中使用

1. 启动 ComfyUI，打开 WebUI；
2. 把对应 JSON **拖入** ComfyUI 画布，或 `Workflow → Open`；
3. 检查模型名称是否与本地 `ComfyUI\models` 一致；
4. 图生图/超分工作流把 `Load Image` 的 `example.png` 换成自己的图片（图片需位于 `ComfyUI\input` 或在节点中上传）；
5. 修改提示词、尺寸、seed 等参数；
6. `Queue`，到输出目录查看。

`example.png` 是 ComfyUI 自带的示例输入；如果不存在，请在 Load Image 节点选择自己的图片。

### 文生图

- 512×512，25 步，CFG 7，euler/normal；
- 8GB 显存建议先用 512；OOM 时改用启动器“低显存”或降低尺寸；
- 只使用 `v1-5-pruned-emaonly.safetensors`，路径 `ComfyUI\models\checkpoints`。

### 图生图

- `Load Image → Image Scale(512×512) → VAE Encode → KSampler(denoise 0.6) → VAE Decode`；
- `denoise` 越低越接近原图：0.3~0.45 轻微改风格，0.55~0.7 明显重绘；
- 想保留原图比例时，把 `Image Scale` 的 `crop` 设为 `disabled`，并让宽高保持原比例；
- 输入图建议先控制在 512~768 长边，避免 8GB 显存不足。

### 高清修复（Hires Fix）

- 先用 SD1.5 在 512×512 生成基础图；
- `Latent Upscale` 用 `bislerp` 放大到 1024×1024；
- 第二个 `KSampler` 以 denoise 0.55 补细节；
- 8GB 显存下 1024 阶段可能较慢；OOM 时把 `Latent Upscale` 改为 768×768，或把第二个采样器 denoise 降到 0.45。

### 4x 超分

- 默认 `4x-UltraSharp.pth`，也可换成 `RealESRGAN_x4plus.pth`；
- 输入图放在 `ComfyUI\input` 或用 Load Image 上传；
- 512→2048 的 4x 结果在 8GB 显存上已验证；
- 批量处理用 `scripts\batch_upscale.py`，不必逐张点 UI。

### Flux（可选，不自带模型）

需要先按 README 的模型目录放置：

```text
ComfyUI\models\diffusion_models\flux1-dev.safetensors
ComfyUI\models\text_encoders\clip_l.safetensors
ComfyUI\models\text_encoders\t5xxl_fp16.safetensors
ComfyUI\models\vae\ae.safetensors
```

工作流使用：

- `UNETLoader`：`flux1-dev.safetensors`，weight_dtype `default`（8GB 可改 `fp8_e4m3fn` 降低显存）；
- `DualCLIPLoader`：`t5xxl_fp16` + `clip_l`，type `flux`；
- `VAELoader`：`ae.safetensors`；
- `FluxGuidance`：3.5；
- 1024×1024，20 步，CFG 1.0，euler/simple；
- 8GB 显存跑 Flux 可能 OOM，建议 `fp8_e4m3fn`、降低分辨率或使用 GGUF 量化模型。

Flux 文件名必须与本地一致；如果你用 `flux1-dev-fp8.safetensors` 或 GGUF，请在工作流中改 loader 的文件名/节点。

## API 调用

PowerShell 示例（文生图）：

```powershell
$wf = Get-Content .\workflows\api_txt2img_sd15.json -Raw | ConvertFrom-Json
$body = @{ prompt = $wf; client_id = "my-client" } | ConvertTo-Json -Depth 50
$r = Invoke-RestMethod http://127.0.0.1:8188/prompt -Method Post `
  -ContentType 'application/json' -Body $body
$r.prompt_id

# 查询结果
Invoke-RestMethod "http://127.0.0.1:8188/history/$($r.prompt_id)"
```

图生图/超分 API 版需要把 `LoadImage` 的 `image` 改成 `ComfyUI\input` 中的文件名，例如：

```json
"4": { "class_type": "LoadImage", "inputs": { "image": "my-photo.jpg" } }
```

批量超分：

```powershell
.\.venv\Scripts\python.exe .\scripts\batch_upscale.py .\input --recursive `
  --model 4x-UltraSharp.pth --output-dir .\output\upscaled
```

## 参数速查

| 参数 | 建议 |
| --- | --- |
| 尺寸 | SD1.5 512×512 / 512×768；Flux 1024×1024 |
| 步数 | SD1.5 20~30；Flux 20 |
| CFG | SD1.5 6~8；Flux 1.0 |
| 采样器 | SD1.5 euler/normal；Flux euler/simple |
| 图生图 denoise | 0.3~0.45 轻改；0.55~0.7 重绘 |
| Hires denoise | 0.45~0.6 |
| 超分 | 4x-UltraSharp 通用；RealESRGAN 写实 |

## 验证记录

- `txt2img_sd15.json`：历史验证，512×512 约 7 秒；
- `img2img_sd15.json` 与 `api_img2img_sd15.json`：API 执行 success；
- `txt2img_hires_sd15.json` 与 `api_txt2img_hires_sd15.json`：API 执行 success，输出 1024×1024；
- `upscale_4x.json` 与 `api_upscale_4x.json`：已验证 512→2048；
- `flux_txt2img.json`：节点/连线/输入结构检查通过；本地无 Flux 模型，未执行。

工作流是项目自有 JSON；模型仍遵循各自许可证（SD1.5 CreativeML Open RAIL-M、Flux 非商业/商业许可按官方条款）。
