param(
    [string]$Root = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}

$count = 0
foreach ($exe in Get-ChildItem $Root -Recurse -Filter 'ComfyLauncher.exe' -File -ErrorAction SilentlyContinue) {
    try {
        Unblock-File -LiteralPath $exe.FullName
        Write-Host ("Unblocked: " + $exe.FullName)
        $count++
    } catch {
        Write-Warning ("Failed to unblock: " + $exe.FullName)
    }
}

Write-Host ("Done. Unblocked $count file(s).")
Write-Host "Use run-launcher.bat to start without unsigned-EXE SmartScreen."
