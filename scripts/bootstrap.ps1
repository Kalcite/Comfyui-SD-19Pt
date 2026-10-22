param(
    [string]$Root = '',
    [string]$Proxy = 'http://127.0.0.1:10108',
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
    & (Join-Path $PSScriptRoot 'setup-comfy.ps1') -Root $Root -Proxy $Proxy
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& (Join-Path $PSScriptRoot 'setup-venv.ps1') -Root $Root
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot 'setup-torch.ps1') -Root $Root
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipModels) {
    & (Join-Path $PSScriptRoot 'setup-models.ps1') -Root $Root -Proxy $Proxy
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& (Join-Path $PSScriptRoot 'check-env.ps1') -Root $Root
exit $LASTEXITCODE
