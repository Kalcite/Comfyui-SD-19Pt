param(
    [string]$Root = '',
    [string]$Proxy = 'http://127.0.0.1:10108',
    [string]$ComfyVersion = '',
    [ValidateSet('CUDA', 'DirectML', 'CPU')]
    [string]$Backend = 'CUDA',
    [string]$CudaVersion = '13.0',
    [string]$TorchVersion = '',
    [switch]$WithModels,
    [switch]$SkipModels,
    [switch]$SkipComfy
)

. (Join-Path $PSScriptRoot 'lib\Common.ps1')
$Root = Resolve-Root $Root

Write-Host "ComfyUI bootstrap" -ForegroundColor Cyan
Write-Host "Root: $Root"

& (Join-Path $PSScriptRoot 'setup-python.ps1') -Root $Root
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipComfy) {
    & (Join-Path $PSScriptRoot 'setup-comfy.ps1') -Root $Root -Proxy $Proxy -Version $ComfyVersion
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& (Join-Path $PSScriptRoot 'setup-venv.ps1') -Root $Root
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot 'setup-torch.ps1') -Root $Root -Backend $Backend -CudaVersion $CudaVersion -TorchVersion $TorchVersion
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 模型是可选的：默认不下载；只有显式 -WithModels 才执行。
if ($WithModels) {
    if (-not $SkipModels) {
        & (Join-Path $PSScriptRoot 'setup-models.ps1') -Root $Root -Proxy $Proxy
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
} else {
    Write-Host "Skipping model download (optional). Use -WithModels or scripts\setup-models.ps1." -ForegroundColor Yellow
}

& (Join-Path $PSScriptRoot 'check-env.ps1') -Root $Root
exit $LASTEXITCODE
