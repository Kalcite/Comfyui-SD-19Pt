param(
    [string]$Root = '',
    [int]$Port = 8188,
    [string]$Listen = '127.0.0.1',
    [switch]$LowVram,
    [switch]$HighVram,
    [switch]$NoBrowser,
    [string]$ExtraArgs = ''
)

. (Join-Path $PSScriptRoot 'lib\Common.ps1')
$Root = Resolve-Root $Root
$comfy = Join-Path $Root 'ComfyUI'
$python = Get-VenvPython (Join-Path $Root '.venv')

if (-not (Test-Path -LiteralPath (Join-Path $comfy 'main.py'))) {
    throw "ComfyUI not found. Run setup-comfy.ps1 first."
}
if (-not (Test-Path -LiteralPath $python)) {
    Write-Warn "Virtual environment not found; falling back to bundled Python."
    $python = Get-PythonExe $Root
}
if (-not (Test-Path -LiteralPath $python)) { throw "No usable Python interpreter." }

$env:TMP = Join-Path $Root 'temp'
$env:TEMP = $env:TMP
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
$env:HF_HOME = Join-Path $Root 'cache\hf'
New-Item -ItemType Directory -Path $env:TMP, $env:HF_HOME -Force | Out-Null

$arguments = @('main.py', '--port', $Port)
if ($Listen -and $Listen -ne '127.0.0.1' -and $Listen -ne 'localhost') {
    $arguments += @('--listen', $Listen)
}
if ($LowVram) { $arguments += '--lowvram' }
if ($HighVram) { $arguments += '--highvram' }
if ($NoBrowser) { $arguments += '--disable-auto-launch' }
if (-not [string]::IsNullOrWhiteSpace($ExtraArgs)) {
    $arguments += $ExtraArgs
}

Write-Host ("Starting: " + $python + " " + ($arguments -join ' ')) -ForegroundColor Cyan
Set-Location -LiteralPath $comfy
& $python @arguments
exit $LASTEXITCODE
