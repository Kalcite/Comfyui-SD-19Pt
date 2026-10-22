param(
    [string]$Root = '',
    [string]$ModelsDir = '',
    [string]$Proxy = '',
    [string]$HfMirror = '',
    [ValidateSet('all', 'sd', 'upscalers')]
    [string]$Only = 'all'
)

. (Join-Path $PSScriptRoot 'lib\Common.ps1')
$Root = Resolve-Root $Root
$python = Get-PythonExe $Root
if (-not (Test-Path -LiteralPath $python)) {
    throw "Bundled Python not found: $python. Run setup-python.ps1 first."
}

if ([string]::IsNullOrWhiteSpace($ModelsDir)) {
    $ModelsDir = Join-Path $Root 'ComfyUI\models'
}

$checkpoints = Join-Path $ModelsDir 'checkpoints'
$upscale = Join-Path $ModelsDir 'upscale_models'
New-Item -ItemType Directory -Path $checkpoints -Force | Out-Null
New-Item -ItemType Directory -Path $upscale -Force | Out-Null

function Use-Mirror {
    param([string]$Url)
    if ([string]::IsNullOrWhiteSpace($HfMirror)) { return $Url }
    return ($Url -replace '^https://huggingface\.co', $HfMirror.TrimEnd('/'))
}

$sdUrl = 'https://huggingface.co/stable-diffusion-v1-5/stable-diffusion-v1-5/resolve/main/v1-5-pruned-emaonly.safetensors'
$sdPath = Join-Path $checkpoints 'v1-5-pruned-emaonly.safetensors'

$ultraUrl = 'https://huggingface.co/lokCX/4x-Ultrasharp/resolve/main/4x-UltraSharp.pth'
$ultraPath = Join-Path $upscale '4x-UltraSharp.pth'

$esrganUrl = 'https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth'
$esrganPath = Join-Path $upscale 'RealESRGAN_x4plus.pth'

$failed = $false

if ($Only -eq 'all' -or $Only -eq 'sd') {
    Write-Step "Downloading Stable Diffusion 1.5 checkpoint (~4.3 GB)"
    if (-not (Invoke-Download -Root $Root -Url (Use-Mirror $sdUrl) -Destination $sdPath -Proxy $Proxy)) {
        $failed = $true
    }
}

if ($Only -eq 'all' -or $Only -eq 'upscalers') {
    Write-Step "Downloading 4x-UltraSharp upscaler (~67 MB)"
    if (-not (Invoke-Download -Root $Root -Url (Use-Mirror $ultraUrl) -Destination $ultraPath -Proxy $Proxy)) {
        $failed = $true
    }

    Write-Step "Downloading RealESRGAN x4plus upscaler (~67 MB)"
    if (-not (Invoke-Download -Root $Root -Url $esrganUrl -Destination $esrganPath -Proxy $Proxy)) {
        $failed = $true
    }
}

Write-Step "Model summary"
Get-ChildItem -LiteralPath $ModelsDir -Recurse -File -Include *.safetensors, *.pth, *.ckpt |
    Select-Object FullName, @{Name = 'MB'; Expression = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize

if ($failed) {
    Write-Warn "One or more downloads failed. Re-run this script to resume from .part files."
    exit 1
}

Write-Ok "Requested models are ready in $ModelsDir"
exit 0
