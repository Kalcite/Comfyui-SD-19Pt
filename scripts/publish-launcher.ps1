param(
    [string]$Root = '',
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}
$project = Join-Path $Root 'launcher\ComfyLauncher\ComfyLauncher.csproj'
$output = Join-Path $Root 'launcher\publish'

if (-not (Test-Path -LiteralPath $project)) {
    throw "Project not found: $project"
}

$arguments = @('publish', $project, '-c', 'Release', '-o', $output, '--nologo')
if ($SelfContained) {
    $arguments += @('-r', 'win-x64', '--self-contained', 'true')
}

Write-Host ("dotnet " + ($arguments -join ' ')) -ForegroundColor Cyan
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host ("Published to: " + $output) -ForegroundColor Green
Get-ChildItem -LiteralPath $output -Filter 'ComfyLauncher*' | Select-Object Name, Length | Format-Table -AutoSize
exit 0
