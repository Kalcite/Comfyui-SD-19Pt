# 更新日志

## v1.1.1 — 2026-10-23

### 修复

- **启动慢/无法验证发布者**：根因是未签名 apphost 被 Windows 安全组件检查。使用 Windows SDK 的 `signtool.exe` 和本地 `CN=@1999Pt` 代码签名证书后，签名状态为 Valid，直接双击 EXE 从约 33 秒降到约 1.7 秒。
- **发布流程自动签名**：`scripts/publish-launcher.ps1` 检测到 `CN=@1999Pt` 证书后自动调用 signtool；新增 `scripts/create-local-signing-cert.ps1`（可选安装到当前用户信任存储）。
- **打开目录**：Shell COM 增加 1.5 秒超时，失败后改用 `cmd /c start`，避免阻塞 UI 和直接启动 explorer.exe。

### 说明

- 本地自签名证书只安装在当前用户的 `Root` / `TrustedPublisher`，**仅本机信任**；其他电脑需要导入该证书或使用正式 CA 证书。
- 证书私钥保留在当前用户证书存储中，不进入仓库。

## v1.1.0 — 2026-10-23

### 新增

- **模型管理按目录分类**：Checkpoint、扩散模型（Flux/SD3）、文本编码器/语言解释器、VAE、LoRA、超分、ControlNet、CLIP Vision、Embeddings；兼容旧版 `unet`/`clip` 目录。
- **Flux 示例**：`diffusion_models + text_encoders + vae + loras` 的完整目录样例。
- **推理后端组合**：CUDA 13.0 / 12.8 / 12.6 / 12.4、DirectML、CPU；自动生成 `--directml`/`--cpu` 启动参数。
- **ComfyUI 版本选择**：支持 tag、branch、commit，脚本与启动器都支持切换。
- **模型下载可选**：一键构建默认不下载模型；增加“构建时下载推荐模型”复选框和 `bootstrap.ps1 -WithModels`。
- **自动打开 WebUI**：启动就绪后以系统默认浏览器链接方式打开；`app.manifest` 使用 `asInvoker`，避免浏览器继承管理员权限后提示“以管理员身份重启”。
- **打开目录修复**：优先使用 Shell COM `Shell.Application.Explore`，不再直接启动 `explorer.exe`，修复 `0xc0000142`。
- **安全启动入口**：`run-launcher.bat` 优先通过 `dotnet ComfyLauncher.dll` 启动（微软签名的 dotnet 主机），避免未签名 EXE 触发 SmartScreen；直接运行 EXE 时尝试 `Unblock-File`。
- **快捷方式**：`scripts/create-shortcut.ps1` 生成带图标的“ComfyUI 启动器”快捷方式。
- **代码签名/解除锁定脚本**：`scripts/sign-launcher.ps1` 支持 PFX/证书指纹；`scripts/unblock-launcher.ps1` 解除下载锁定。
- **深浅色主题完善**：控制台、日志、标题栏、窗口图标全部随主题切换；透明 Logomark 图标。
- **文档拆分**：`README.md` 面向用户，`developer.md` 面向开发与构建。

### 修复

- 浅色模式下控制台仍为深色的问题。
- 模型页“打开目录”触发 `explorer.exe 应用程序错误 0xc0000142`。
- 主题切换后已有日志颜色不更新的问题。
- PowerShell 脚本中文注释导致的解析错误（脚本统一为 UTF-8 BOM）。

### 已知说明

- 未做 Authenticode 签名时，直接双击自定义 EXE 仍可能触发 Windows SmartScreen/Smart App Control；推荐使用 `run-launcher.bat`，或使用受信任证书签名。
- CUDA 13.0/torch 2.9.1 已实测；其他 CUDA/DirectML/CPU 组合已实现脚本映射，但切换时会重新下载数 GB 包，未逐一在当前机器重装验证。

## v1.0.0 — 2026-10-22

- 初始部署：内置 Python 3.12.10、`.venv`、ComfyUI 0.42.0、PyTorch 2.9.1+cu130。
- SD1.5 生图、4x 超分模型（4x-UltraSharp、RealESRGAN_x4plus）与工作流。
- Avalonia 启动器：路径配置、启动参数、内嵌控制台、环境构建。
- Git 15 秒无响应/低速检测与 10108 代理回退。
- 深浅色主题、透明 Logomark、发布者/版权信息。
