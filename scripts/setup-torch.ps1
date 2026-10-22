param(
    [string]$Root = '',
    [string]$VenvPath = '',
    [string]$TorchIndex = 'https://download.pytorch.org/whl/cu130',
    [string]$PipIndex = '',
    [string]$TorchVersion = '2.9.1',
    [string]$TorchvisionVersion = '0.24.1',
    [string]$TorchaudioVersion = '2.9.1',
    [string]$Proxy = '',
    [switch]$SkipTorch
)

. (Join-Path $PSScriptRoot 'lib\Common.ps1')
$Root = Resolve-Root $Root
if ([string]::IsNullOrWhiteSpace($VenvPath)) { $VenvPath = Join-Path $Root '.venv' }
$venvPython = Get-VenvPython $VenvPath

$env:TMP = Join-Path $Root 'temp'
$env:TEMP = $env:TMP
New-Item -ItemType Directory -Path $env:TMP -Force | Out-Null

if (-not (Test-Path -LiteralPath $venvPython)) {
    Write-Warn "Virtual environment not found; creating it first."
    & (Join-Path $PSScriptRoot 'setup-venv.ps1') -Root $Root -VenvPath $VenvPath
    if ($LASTEXITCODE -ne 0) { throw "Unable to create virtual environment." }
}
if (-not (Test-Path -LiteralPath $venvPython)) { throw "Virtual environment is still missing: $venvPython" }

$extraIndex = $PipIndex
if ([string]::IsNullOrWhiteSpace($extraIndex)) {
    $extraIndex = 'https://pypi.tuna.tsinghua.edu.cn/simple'
}

if (-not $SkipTorch) {
    Write-Step "Installing PyTorch (CUDA build)"
    $pipArgs = @(
        '-u', '-m', 'pip', 'install',
        ("torch==" + $TorchVersion),
        ("torchvision==" + $TorchvisionVersion),
        ("torchaudio==" + $TorchaudioVersion),
        '--index-url', $TorchIndex,
        '--extra-index-url', $extraIndex,
        '--progress-bar', 'off',
        '--retries', '5',
        '--timeout', '120',
        '--disable-pip-version-check'
    )
    if (-not [string]::IsNullOrWhiteSpace($Proxy)) { $pipArgs += @('--proxy', $Proxy) }

    & $venvPython @pipArgs
    if ($LASTEXITCODE -ne 0) {
        throw "PyTorch installation failed. Check the network or change the PyTorch index in Settings."
    }

    Write-Step "Verifying CUDA availability"
    & $venvPython -c "import torch; print('torch', torch.__version__); print('cuda', torch.cuda.is_available()); print(torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'CPU only')"
    if ($LASTEXITCODE -ne 0) { throw "PyTorch import failed." }
} else {
    Write-Host "Skipping PyTorch installation (-SkipTorch)."
}

Write-Step "Installing ComfyUI requirements"
$requirements = Join-Path $Root 'ComfyUI\requirements.txt'
if (-not (Test-Path -LiteralPath $requirements)) {
    Write-Warn "ComfyUI requirements not found: $requirements"
    Write-Host "Clone ComfyUI first, then rerun this step."
    exit 2
}

$uv = Join-Path $VenvPath 'Scripts\uv.exe'
$env:UV_CACHE_DIR = Join-Path $Root 'cache\uv'
New-Item -ItemType Directory -Path $env:UV_CACHE_DIR -Force | Out-Null
if (-not (Test-Path -LiteralPath $uv)) {
    Write-Step "Installing uv (fast dependency resolver)"
    & $venvPython -m pip install uv --index-url $extraIndex --progress-bar off --disable-pip-version-check
    if ($LASTEXITCODE -ne 0) {
        Write-Warn "uv installation failed; falling back to pip."
    }
}

if (Test-Path -LiteralPath $uv) {
    $installRequirements = Join-Path $env:TEMP 'comfyui-requirements-uv.txt'
    & $venvPython (Join-Path $PSScriptRoot 'lib\filter_requirements.py') $requirements $installRequirements uv
    if ($LASTEXITCODE -ne 0) { throw "Unable to build filtered requirements file." }

    Write-Host "Using uv with indexes: $extraIndex, https://mirrors.aliyun.com/pypi/simple/"
    & $uv pip install --python $venvPython -r $installRequirements `
        --index-url $extraIndex `
        --extra-index-url 'https://mirrors.aliyun.com/pypi/simple/' `
        --index-strategy unsafe-best-match
    if ($LASTEXITCODE -ne 0) {
        throw "ComfyUI requirements installation failed (uv)."
    }
} else {
    # pip 回退：跳过可选模板包，避免其元包在镜像缺包时触发长时间回溯。
    $installRequirements = Join-Path $env:TEMP 'comfyui-requirements-no-templates.txt'
    & $venvPython (Join-Path $PSScriptRoot 'lib\filter_requirements.py') $requirements $installRequirements pip
    if ($LASTEXITCODE -ne 0) { throw "Unable to build filtered requirements file." }
    Write-Warn "uv unavailable; installing via pip without the optional workflow templates."

    $reqArgs = @('-u', '-m', 'pip', 'install', '-r', $installRequirements,
                 '--extra-index-url', $extraIndex,
                 '--extra-index-url', 'https://mirrors.aliyun.com/pypi/simple/',
                 '--progress-bar', 'off', '--retries', '5', '--timeout', '120',
                 '--disable-pip-version-check')
    if (-not [string]::IsNullOrWhiteSpace($Proxy)) { $reqArgs += @('--proxy', $Proxy) }

    & $venvPython @reqArgs
    if ($LASTEXITCODE -ne 0) {
        throw "ComfyUI requirements installation failed."
    }
}

Write-Ok "PyTorch and ComfyUI dependencies are installed."
exit 0
