param(
    [Parameter(Mandatory = $true)]
    [string]$Dll
)

$ErrorActionPreference = 'Stop'
$dotnet = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    throw "dotnet.exe not found; install .NET 8 Desktop Runtime."
}

# 直接用 ProcessStartInfo 传递带引号的 DLL 参数，避免 Start-Process 把引号当成路径内容。
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $dotnet
$psi.Arguments = '"' + $Dll + '"'
$psi.UseShellExecute = $true
$psi.WindowStyle = 'Hidden'
[System.Diagnostics.Process]::Start($psi) | Out-Null
