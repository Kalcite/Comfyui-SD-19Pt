param(
    [string]$Root = '',
    [string]$Proxy = 'http://127.0.0.1:10108',
    [int]$TimeoutSec = 15,
    [int]$MinSpeedKBps = 80,
    [bool]$UseProxyFallback = $true
)

. (Join-Path $PSScriptRoot 'lib\Common.ps1')
$Root = Resolve-Root $Root
$target = Join-Path $Root 'ComfyUI'
$url = 'https://github.com/comfyanonymous/ComfyUI.git'

Write-Step "Preparing ComfyUI source"
$ok = Invoke-GitCloneSmart -Url $url -Target $target -Proxy $Proxy -TimeoutSec $TimeoutSec -MinSpeedKBps $MinSpeedKBps -UseProxyFallback $UseProxyFallback
if (-not $ok) { exit 1 }

$versionFile = Join-Path $target 'comfyui_version.py'
if (Test-Path -LiteralPath $versionFile) {
    Write-Host (Get-Content -LiteralPath $versionFile -First 1)
}

Write-Ok "ComfyUI is ready: $target"
exit 0
