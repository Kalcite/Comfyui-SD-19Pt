param(
    [string]$Root = ''
)

. (Join-Path $PSScriptRoot 'lib\Common.ps1')
$Root = Resolve-Root $Root
$issues = 0

Write-Host "ComfyUI environment check" -ForegroundColor Cyan
Write-Host "Root: $Root"

function Report {
    param([string]$Name, [bool]$Ok, [string]$Detail)
    if ($Ok) {
        Write-Host ("[OK]   " + $Name + ": " + $Detail) -ForegroundColor Green
    } else {
        Write-Host ("[MISS] " + $Name + ": " + $Detail) -ForegroundColor Red
        $script:issues = $script:issues + 1
    }
}

$python = Get-PythonExe $Root
if (Test-Path -LiteralPath $python) {
    $version = & $python -c "import sys;print(sys.version.split()[0])" 2>$null
    Report 'Python' ($version -like '3.12.*') ("$version ($python)")
} else {
    Report 'Python' $false $python
}

$comfy = Join-Path $Root 'ComfyUI'
$comfyOk = Test-Path -LiteralPath (Join-Path $comfy 'main.py')
$comfyDetail = $comfy
if ($comfyOk) {
    $versionFile = Join-Path $comfy 'comfyui_version.py'
    if (Test-Path -LiteralPath $versionFile) {
        $versionText = Get-Content -LiteralPath $versionFile -Raw
        if ($versionText -match '__version__\s*=\s*"([^"]+)"') {
            $comfyDetail = ("v" + $Matches[1] + "  " + $comfy)
        }
    }
}
Report 'ComfyUI' $comfyOk $comfyDetail

$venv = Join-Path $Root '.venv'
$venvPython = Get-VenvPython $venv
Report 'Virtualenv' (Test-Path -LiteralPath $venvPython) $venv

if (Test-Path -LiteralPath $venvPython) {
    $torchInfo = & $venvPython -c "import torch;print(torch.__version__);print(torch.cuda.is_available());print(torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'CPU')" 2>$null
    if ($LASTEXITCODE -eq 0) {
        $lines = @($torchInfo)
        Report 'PyTorch' $true (($lines -join ', '))
    } else {
        Report 'PyTorch' $false 'import torch failed'
    }
} else {
    Report 'PyTorch' $false 'virtualenv missing'
}

$models = Join-Path $comfy 'models'
$checkpoints = @(Get-ChildItem -LiteralPath (Join-Path $models 'checkpoints') -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.safetensors', '.ckpt', '.pt' })
$upscalers = @(Get-ChildItem -LiteralPath (Join-Path $models 'upscale_models') -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.pth', '.safetensors', '.onnx' })
Report 'Checkpoints' ($checkpoints.Count -gt 0) (($checkpoints | ForEach-Object { $_.Name }) -join ', ')
Report 'Upscalers' ($upscalers.Count -gt 0) (($upscalers | ForEach-Object { $_.Name }) -join ', ')

$git = Get-Command git -ErrorAction SilentlyContinue
Report 'Git' ($null -ne $git) ($(if ($git) { $git.Source } else { 'not found' }))

if ($issues -gt 0) {
    Write-Host ""
    Write-Warn "$issues item(s) need attention."
    exit 1
}

Write-Host ""
Write-Ok "Environment looks ready. Start with: scripts\start-comfy.ps1"
exit 0
