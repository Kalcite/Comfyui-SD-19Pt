param(
    [string]$Root = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}

$dll = Join-Path $Root 'launcher\publish\ComfyLauncher.dll'
$launchScript = Join-Path $Root 'scripts\launch-ui.ps1'
$icon = Join-Path $Root 'launcher\ComfyLauncher\Assets\app.ico'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$shortcutPath = Join-Path $Root 'ComfyUI 启动器.lnk'

if (-not (Test-Path -LiteralPath $dll)) {
    throw "Launcher DLL not found: $dll. Build/publish first."
}
if (-not (Test-Path -LiteralPath $launchScript)) {
    throw "Launch script not found: $launchScript"
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $powershell
$shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$launchScript`" -Dll `"$dll`""
$shortcut.WorkingDirectory = $Root
$shortcut.Description = 'ComfyUI SD Launcher'
if (Test-Path -LiteralPath $icon) {
    $shortcut.IconLocation = $icon
}
$shortcut.Save()

Write-Host ("Created: " + $shortcutPath)
Write-Host "Double-click the shortcut to start without unsigned-EXE SmartScreen."
