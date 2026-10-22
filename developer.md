# Developer Guide · ComfyUI SD Launcher

本文面向维护者/二次开发者。用户安装与使用说明见 [README.md](README.md)。

---

## 1. 项目定位

这是一个 Windows 桌面部署器 + 启动器，不是 ComfyUI 的分支。项目负责：

- 生成/管理本机 Python 运行时与虚拟环境；
- 按用户选择的 CUDA/DirectML/CPU 组合安装 PyTorch；
- 克隆/切换 ComfyUI 版本；
- 管理 ComfyUI 模型目录（Checkpoint、Diffusion Model、Text Encoder、VAE、LoRA 等）；
- 启动/停止 ComfyUI，并把 stdout/stderr 显示在嵌入式控制台；
- 提供深/浅色主题、图标、发布信息。

ComfyUI 源码、Python 运行时、模型、日志属于本机部署数据，不进入项目仓库。

---

## 2. 仓库结构

```text
Comfyui-SD-19Pt/
├─ launcher/ComfyLauncher/        # Avalonia 启动器
│  ├─ ComfyLauncher.csproj        # .NET 8 / Avalonia 11.3.22 / 版本信息
│  ├─ app.manifest                # asInvoker、Win10 兼容声明
│  ├─ App.axaml                   # FluentTheme + 深浅色资源字典
│  ├─ Assets/                     # app.ico / logomark-yellow.ico / logomark-ink.ico
│  ├─ Views/MainWindow.axaml(.cs) # UI、控制台、主题标题栏、窗口图标
│  ├─ ViewModels/MainViewModel.cs # 状态、命令、进程、模型/环境编排
│  ├─ Models/                     # AppSettings、LogLine、ModelCategory...
│  ├─ Services/                   # Process/Git/Script/Command/Path
│  └─ Infrastructure/             # RelayCommand、转换器、ObservableObject
├─ scripts/
│  ├─ bootstrap.ps1               # 一键部署（模型默认不下载）
│  ├─ setup-python.ps1            # 校验/复制 Python 3.12 + pip
│  ├─ setup-comfy.ps1             # 克隆/更新/切换 ComfyUI 版本
│  ├─ setup-venv.ps1              # 创建/修复 .venv
│  ├─ setup-torch.ps1             # CUDA/DirectML/CPU + requirements
│  ├─ setup-models.ps1            # 可选的 SD1.5/超分模型下载
│  ├─ start-comfy.ps1             # 命令行启动
│  ├─ check-env.ps1               # 环境检查
│  ├─ publish-launcher.ps1        # dotnet publish
│  ├─ launch-ui.ps1               # 通过 dotnet 主机隐藏启动 DLL
│  ├─ create-shortcut.ps1         # 生成带图标的本地快捷方式
│  ├─ sign-launcher.ps1           # 用受信任证书做 Authenticode 签名
│  ├─ unblock-launcher.ps1        # 解除下载文件锁定
│  ├─ batch_upscale.py            # ComfyUI HTTP API 批量超分
│  └─ lib/                        # Common.ps1 / download.py / filter_requirements.py
├─ workflows/                     # 界面版 + API 版生图/超分
├─ ICO/                           # 图标源（Logomark/字标 SVG/ICO）
├─ docs/                          # 用户文档截图
├─ config/launcher.example.json   # 配置模板
├─ requirements.lock.txt          # 验证环境的依赖锁定
├─ developer.md                    # 开发/构建/发布说明
└─ updata.md                       # 版本更新日志
```

### 本机部署目录（不提交）

```text
runtime/  .venv/  ComfyUI/  cache/  temp/  logs/
input/    output/ user/     config/launcher.json
launcher/**/bin|obj|publish/
```

`.gitignore` 已覆盖以上路径、`*.log`、`*.part`、IDE 文件等。

---

## 3. 构建与发布

### 3.1 前置

- .NET 8 SDK（运行发布产物需 .NET 8 Desktop Runtime）；
- Windows 10/11；
- 首次 `dotnet restore` 需要 NuGet 网络。

### 3.2 命令

```powershell
dotnet restore .\launcher\ComfyLauncher\ComfyLauncher.csproj
dotnet build .\launcher\ComfyLauncher\ComfyLauncher.csproj -c Debug
powershell -ExecutionPolicy Bypass -File .\scripts\publish-launcher.ps1
```

发布产物在 `launcher\publish\`。`run-launcher.bat` 查找顺序：

1. `launcher\ComfyLauncher\bin\Release\net8.0\ComfyLauncher.exe`
2. `launcher\publish\ComfyLauncher.exe`
3. `bin\Debug\net8.0\...`
4. `dotnet run`

### 3.3 版本与图标

`ComfyLauncher.csproj`：

```xml
<ApplicationIcon>Assets\app.ico</ApplicationIcon>
<Company>@1999Pt</Company>
<Authors>@1999Pt</Authors>
<Copyright>Copyright © 2026 @1999Pt</Copyright>
<FileVersion>1.0.0.0</FileVersion>
```

- `Assets/app.ico` 是深色圆角底 + 黄色 Logomark 的多尺寸 ICO；
- `Assets/logomark-yellow.ico` / `logomark-ink.ico` 是透明 Logomark，运行时按主题切换 `Window.Icon`；
- 图标由 `ICO/Logomark *.svg` 渲染，避免白底/白边。

### 3.4 SmartScreen

`app.manifest` 声明 `requestedExecutionLevel level="asInvoker"`，启动器不会请求管理员权限；自动打开 WebUI 时使用系统默认浏览器的链接方式，管理员身份下则交给 Explorer 打开，避免浏览器继承管理员权限后提示“以管理员身份重启”。

`run-launcher.bat` 优先执行：

```text
powershell -File scripts\launch-ui.ps1 -Dll launcher\publish\ComfyLauncher.dll
  → Start-Process dotnet.exe ComfyLauncher.dll -WindowStyle Hidden
```

`dotnet.exe` 由 Microsoft 签名，可绕开未签名自定义 EXE 的 SmartScreen/Smart App Control 拦截；直接运行 EXE 时批处理会先尝试 `Unblock-File`。

文件属性会显示 Company/Copyright，但“未知发布者”弹窗只有在使用受信任证书做 Authenticode 签名后才会消失。私钥 `.pfx` 不入库；签名命令示例：

```powershell
signtool sign /fd SHA256 /f <cert.pfx> /p <password> `
  /tr http://timestamp.digicert.com /td SHA256 `
  .\launcher\publish\ComfyLauncher.exe
```

也可以使用 [scripts/sign-launcher.ps1](scripts/sign-launcher.ps1)：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sign-launcher.ps1 `
  -PfxPath <cert.pfx> -PfxPassword <password>
```

---

## 4. 启动器架构

```
Program.cs → App.axaml → MainWindow
                            │
                     MainViewModel
        ┌───────────────┼────────────────┐
   ComfyRunner      GitService       ScriptRunner
   ProcessUtil      SettingsService  CommandBuilder
        │
   ComfyUI 进程 + stdout/stderr → LogLines → 控制台
```

### 4.1 关键类

| 类 | 职责 |
| --- | --- |
| `MainViewModel` | 状态机、命令、环境检查、安装编排、模型分类 |
| `ComfyRunner` | 启动/停止进程树、转发 stdout/stderr、stdin |
| `GitService` | 直连/代理克隆、无响应/低速检测 |
| `ScriptRunner` | 运行 PowerShell 并把输出写入控制台 |
| `ProcessUtil` | 异步执行、超时、端口/TCP/HTTP 检查 |
| `CommandBuilder` | 由设置生成 ComfyUI 参数（含 `--directml`/`--cpu`） |
| `SettingsService` | `config/launcher.json` 读写、默认路径 |
| `ModelCategory` | 模型目录卡片（数量/样例/打开目录） |

### 4.2 主题

`App.axaml` 使用 `ResourceDictionary.ThemeDictionaries`：

- 键：`AppBackground`、`HeaderBackground`、`CardBackground`、`PanelBorder`、`TextPrimary`、`TextMuted`、`TextLabel`、`AccentText`、`AccentStrong`、`PrimaryButton`、`DangerButton`、`GhostBackground`、`GhostForeground`、`ConsoleBackground`、`ConsoleForeground`、`ConsoleInputBackground`、`WarningText`；
- XAML 使用 `{DynamicResource ...}`；
- `MainViewModel.IsLightTheme` 设置 `Application.Current.RequestedThemeVariant`；
- 已有日志通过 `LogLine.ApplyTheme(bool)` 刷新颜色；
- `MainWindow` 通过 DWM `DWMWA_USE_IMMERSIVE_DARK_MODE` 同步标题栏，并切换窗口图标。

### 4.3 打开目录

`OpenFolder` 优先复用现有 Explorer 的 Shell COM（`Shell.Application.Explore`），在后台线程执行并限时 1.5 秒；超时/失败后用 `cmd /c start "" "<path>"` 交给 Shell，最后才回退到 ShellExecute。

不再直接运行 `explorer.exe "path"`，避免部分环境下 `0xc0000142`，也避免 COM 在被注入的 Explorer 中阻塞 UI。

---

## 5. 环境构建脚本

### 5.1 依赖解析

> PowerShell 脚本含中文注释，必须保存为 **UTF-8 with BOM**。Windows PowerShell 5.1 无 BOM 时会按 ANSI 解码，可能把注释末尾字节误判为续行，导致 `Unexpected token '}'` 解析错误。

`setup-torch.ps1`：

1. 校验 `.venv`；
2. 按 `-Backend CUDA|DirectML|CPU` 与 `-CudaVersion` 安装 PyTorch；
3. 若未安装 `uv` 则用 pip 安装 `uv`；
4. `lib/filter_requirements.py` 生成过滤 requirements：
   - uv 模式排除 `torch/torchvision/torchaudio`，防止被 PyPI 换成 CPU 版；
   - pip 回退排除可选 `comfyui-workflow-templates`；
5. uv 安装剩余依赖（清华 + 阿里云镜像）。

### 5.2 后端映射

| Backend | CudaVersion | Index | 启动参数 |
| --- | --- | --- | --- |
| CUDA | 13.0 | `whl/cu130` | 默认 |
| CUDA | 12.8 | `whl/cu128` | 默认 |
| CUDA | 12.6 | `whl/cu126` | 默认 |
| CUDA | 12.4 | `whl/cu124` | 默认 |
| DirectML | - | `torch-directml` | `--directml` |
| CPU | - | `whl/cpu` | `--cpu` |

CUDA 12.4 默认 torch 2.6.0/torchvision 0.21.0/torchaudio 2.6.0；其余默认 2.9.1/0.24.1/2.9.1。`-TorchVersion` 可覆盖。

### 5.3 ComfyUI 版本

`setup-comfy.ps1 -Version <tag|branch|commit>`：

- 先克隆/更新；
- `git fetch --tags --depth 1 origin`；
- `git checkout <Version>`；
- 失败则 `git fetch --depth 1 origin <Version>` + `checkout FETCH_HEAD`；
- 启动器 `CheckoutComfyVersionAsync` 实现同样逻辑。

### 5.4 模型

`setup-models.ps1` 仅下载 SD1.5 + 两个超分模型；默认不参与一键构建：

```powershell
.\scripts\bootstrap.ps1                 # 不下载模型
.\scripts\bootstrap.ps1 -WithModels     # 下载推荐模型
```

Flux 等大模型不自动下载，由用户按 README 的目录放置。

---

## 6. 模型分类扩展

`MainViewModel.RefreshModels()` 中的 `AddCategory(...)` 定义：

```csharp
AddCategory(
    key: "text_encoders",
    name: "文本编码器 / 语言解释器",
    description: "...",
    sample: "clip_l.safetensors、t5xxl_fp16.safetensors",
    patterns: new[] { "*.safetensors", "*.pt", "*.gguf" },
    extraDirectories: "clip"); // 旧目录兼容
```

新增分类只需：

1. 在 `RefreshModels()` 加一条 `AddCategory`；
2. 同步 README 的模型表格；
3. 必要时在 `setup-models.ps1` 加下载逻辑（默认保持可选）。

`ModelCategory` 提供 `CountText`、`SampleText`、`FilesPreview` 和 `OpenCommand`，XAML 自动渲染。

---

## 7. Git 代理实现

`Scripts/lib/Common.ps1::Invoke-GitCloneAttempt`：

- 启动 `git clone --progress`；
- 每 0.8s 检查 `.git` 大小增长；
- `TimeoutSec` 内无增长 → `NoResponse`；
- 速率窗口低于 `MinSpeedKBps` → `TooSlow`；
- 直连失败后检查 `10108` TCP，可达才代理重试；
- `Invoke-GitCloneSmart` 清理半成品目录。

C# `GitService` 使用相同阈值，逐字符读取 `\r` 进度，支持 `--progress` 实时速率。

---

## 8. 配置字段

`config/launcher.json` 主要字段：

```json
{
  "ComfyPath": "", "PythonPath": "", "VenvPath": "",
  "UserDirectory": "", "ModelsDirectory": "",
  "Port": "8188", "VramMode": "自动",
  "TorchProfile": "CUDA 13.0 + torch 2.9.1",
  "ComfyVersion": "",
  "DownloadModelsAfterSetup": false,
  "GitProxy": "http://127.0.0.1:10108",
  "GitTimeoutSeconds": "15", "GitMinSpeedKBps": "80",
  "Theme": "Dark"
}
```

`config/launcher.example.json` 只含默认值；本机 `config/launcher.json` 被忽略。

---

## 9. 测试清单

发布前：

```powershell
dotnet build .\launcher\ComfyLauncher\ComfyLauncher.csproj -c Debug
powershell -ExecutionPolicy Bypass -File .\scripts\check-env.ps1

# 启动器手动验证
# 1) 深浅主题切换；2) 控制台颜色/标题栏；3) 模型页打开目录
# 4) 启动/停止 ComfyUI；5) 发布 EXE 版本信息

# 生成图/超分（可选）
.\.venv\Scripts\python.exe .\scripts\batch_upscale.py .\input
```

API 冒烟：

```powershell
$wf = Get-Content .\workflows\api_txt2img_sd15.json -Raw | ConvertFrom-Json
$body = @{ prompt = $wf; client_id = "dev" } | ConvertTo-Json -Depth 30
Invoke-RestMethod http://127.0.0.1:8188/prompt -Method Post `
  -ContentType application/json -Body $body
```

---

## 10. 提交边界与敏感信息

提交：

- `launcher/` 源码与 Assets；
- `scripts/`、`workflows/`、`docs/`、`ICO/`；
- `README.md`、`developer.md`、`config/launcher.example.json`、`requirements.lock.txt`。

不要提交：

- `runtime/`、`.venv/`、`ComfyUI/`、`cache/`、`temp/`、`logs/`；
- `output/`、`user/`、`input/`、模型；
- `config/launcher.json`（绝对路径）；
- `launcher/**/bin|obj|publish/`；
- `.pfx`、Token、API Key、代理凭据。

检查：

```powershell
git status --short
git ls-files | Select-String -Pattern 'launcher\.json|\.pfx|\.env|token|secret'
```

---

## 11. 推送

本地仓库与远端：

```text
origin https://github.com/Kalcite/Comfyui-SD-19Pt.git
```

维护者手动推送：

```powershell
git push -u origin main
```

提交信息建议按 `feat:`、`fix:`、`docs:`、`chore:` 分类。

---

## 12. 许可

启动器代码 Copyright © 2026 @1999Pt。第三方组件按各自许可证分发。
