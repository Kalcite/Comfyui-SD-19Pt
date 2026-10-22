param(
    [string]$Root = '',
    [string]$Proxy = 'http://127.0.0.1:10108',
    [int]$TimeoutSec = 15,
    [int]$MinSpeedKBps = 80,
    [bool]$UseProxyFallback = $true,
    [string]$Version = ''
)

. (Join-Path $PSScriptRoot 'lib\Common.ps1')
$Root = Resolve-Root $Root
$target = Join-Path $Root 'ComfyUI'
$url = 'https://github.com/comfyanonymous/ComfyUI.git'

Write-Step "Preparing ComfyUI source"
$ok = Invoke-GitCloneSmart -Url $url -Target $target -Proxy $Proxy -TimeoutSec $TimeoutSec -MinSpeedKBps $MinSpeedKBps -UseProxyFallback $UseProxyFallback
if (-not $ok) { exit 1 }

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    Write-Step "Checking out ComfyUI version: $Version"
    Push-Location $target
    try {
        & git -c http.sslBackend=openssl -c http.proxy= -c https.proxy= fetch --tags --depth 1 origin
        & git -c http.sslBackend=openssl -c http.proxy= -c https.proxy= checkout $Version
        if ($LASTEXITCODE -ne 0) {
            & git -c http.sslBackend=openssl -c http.proxy= -c https.proxy= fetch --depth 1 origin $Version
            & git -c http.sslBackend=openssl -c http.proxy= -c https.proxy= checkout FETCH_HEAD
        }
        if ($LASTEXITCODE -ne 0) {
            Write-Warn "Unable to checkout $Version; keeping the current ComfyUI revision."
            exit 1
        }
    } finally {
        Pop-Location
    }
}

$versionFile = Join-Path $target 'comfyui_version.py'
if (Test-Path -LiteralPath $versionFile) {
    $versionText = Get-Content -LiteralPath $versionFile -Raw
    if ($versionText -match '__version__\s*=\s*"([^"]+)"') {
        Write-Host ("ComfyUI v" + $Matches[1])
    }
}

Write-Ok "ComfyUI is ready: $target"
exit 0
