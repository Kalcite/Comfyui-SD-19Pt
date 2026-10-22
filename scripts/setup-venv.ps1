param(
    [string]$Root = '',
    [string]$VenvPath = '',
    [switch]$Force
)

. (Join-Path $PSScriptRoot 'lib\Common.ps1')
$Root = Resolve-Root $Root
$python = Get-PythonExe $Root
if (-not (Test-Path -LiteralPath $python)) {
    throw "Bundled Python not found: $python. Run setup-python.ps1 first."
}

if ([string]::IsNullOrWhiteSpace($VenvPath)) {
    $VenvPath = Join-Path $Root '.venv'
}
$venvPython = Get-VenvPython $VenvPath

$env:TMP = Join-Path $Root 'temp'
$env:TEMP = $env:TMP
New-Item -ItemType Directory -Path $env:TMP -Force | Out-Null

if ((Test-Path -LiteralPath $venvPython) -and (-not $Force)) {
    $version = & $venvPython -c "import sys;print(sys.version.split()[0])" 2>$null
    Write-Ok "Virtual environment already exists: $VenvPath (Python $version)"
    exit 0
}

Write-Step "Creating virtual environment at $VenvPath"
if (Test-Path -LiteralPath $VenvPath) {
    Write-Host "Removing existing environment ..."
    Remove-Item -LiteralPath $VenvPath -Recurse -Force
}

& $python -m venv $VenvPath
if ($LASTEXITCODE -ne 0) {
    Write-Warn "venv creation failed; retry without pip and bootstrap manually."
    & $python -m venv --without-pip $VenvPath
    if ($LASTEXITCODE -ne 0) { throw "Unable to create virtual environment." }
    & $venvPython -m ensurepip --upgrade
    if ($LASTEXITCODE -ne 0) { throw "Unable to bootstrap pip in the virtual environment." }
}

Write-Step "Upgrading pip / setuptools / wheel"
& $venvPython -m pip install --upgrade pip setuptools wheel --disable-pip-version-check
if ($LASTEXITCODE -ne 0) { throw "pip bootstrap failed." }

& $venvPython -m pip --version
Write-Ok "Virtual environment is ready: $VenvPath"
exit 0
