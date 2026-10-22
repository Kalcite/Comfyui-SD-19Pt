param(
    [string]$Root = '',
    [string]$PfxPath = '',
    [string]$PfxPassword = '',
    [string]$Thumbprint = '',
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}
$exe = Join-Path $Root 'launcher\publish\ComfyLauncher.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Launcher not found: $exe. Run publish-launcher.ps1 first."
}

$cert = $null
if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
    $secure = ConvertTo-SecureString $PfxPassword -AsPlainText -Force
    $cert = Get-PfxCertificate -FilePath $PfxPath -Password $secure
} elseif (-not [string]::IsNullOrWhiteSpace($Thumbprint)) {
    $cert = Get-ChildItem "Cert:\CurrentUser\My\$Thumbprint" -ErrorAction Stop
} else {
    Write-Host "No certificate supplied." -ForegroundColor Yellow
    Write-Host "Use a trusted code-signing certificate:"
    Write-Host "  -PfxPath <file.pfx> -PfxPassword <password>"
    Write-Host "  or -Thumbprint <cert thumbprint> (CurrentUser\\My)"
    Write-Host "Do not commit the .pfx or password."
    exit 2
}

Write-Host "Signing $exe with $($cert.Subject) ..." -ForegroundColor Cyan
$result = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert `
    -TimestampServer $TimestampServer -HashAlgorithm SHA256
if ($result.Status -ne 'Valid') {
    throw "Signing failed: $($result.Status) $($result.StatusMessage)"
}
Write-Host "Signed successfully." -ForegroundColor Green
exit 0
