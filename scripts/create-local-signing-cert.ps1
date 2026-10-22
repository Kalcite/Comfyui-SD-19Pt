param(
    [switch]$InstallTrust,
    [string]$Subject = 'CN=@1999Pt'
)

$ErrorActionPreference = 'Stop'
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $cert) {
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyUsage DigitalSignature `
        -KeyExportPolicy Exportable `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears(5)
    Write-Host "Created local code-signing certificate: $($cert.Thumbprint)"
} else {
    Write-Host "Reusing local code-signing certificate: $($cert.Thumbprint)"
}

if ($InstallTrust) {
    # 仅当前用户信任；不是公共 CA 证书，其他电脑仍需导入该证书或使用正式证书。
    foreach ($storeName in @('Root', 'TrustedPublisher')) {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'CurrentUser')
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        try { $store.Add($cert) } finally { $store.Close() }
    }
    Write-Host "Installed into CurrentUser Root + TrustedPublisher." -ForegroundColor Yellow
}

$cerPath = Join-Path $env:TEMP 'ComfyLauncher-codesign.cer'
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "Public certificate: $cerPath"
Write-Host ("Thumbprint: " + $cert.Thumbprint)
Write-Host "Sign with: scripts\sign-launcher.ps1 -Thumbprint $($cert.Thumbprint)"
