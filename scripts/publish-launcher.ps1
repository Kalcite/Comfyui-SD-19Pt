param(
    [string]$Root = '',
    [switch]$SelfContained,
    [switch]$SkipSign
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

$exe = Join-Path $output 'ComfyLauncher.exe'

if (-not $SkipSign) {
    $signer = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq 'CN=@1999Pt' -and $_.HasPrivateKey } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if ($signer) {
        $signtoolCandidates = @()
        $signtoolCandidates += (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
        $signtoolCandidates += 'C:\Program Files (x86)\Microsoft SDKs\ClickOnce\SignTool\signtool.exe'
        $signtoolCandidates += Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
        $signtool = $signtoolCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

        if (-not $signtool) {
            throw "signtool.exe not found; publish is unsigned. Re-run with -SkipSign or install Windows SDK."
        }

        Write-Host ("Signing with " + $signer.Subject + " ...") -ForegroundColor Cyan
        & $signtool sign /fd SHA256 /sha1 $signer.Thumbprint /v $exe
        if ($LASTEXITCODE -ne 0) {
            throw "signtool failed with exit code $LASTEXITCODE"
        }
        $signature = Get-AuthenticodeSignature $exe
        Write-Host ("Signature: " + $signature.Status + " (" + $signature.StatusMessage + ")") -ForegroundColor Green
    } else {
        Write-Warning "No CN=@1999Pt code-signing certificate. EXE remains unsigned."
        Write-Warning "Run scripts\create-local-signing-cert.ps1 -InstallTrust, then publish again."
    }
}

Write-Host ""
Write-Host ("Published to: " + $output) -ForegroundColor Green
Get-ChildItem -LiteralPath $output -Filter 'ComfyLauncher*' | Select-Object Name, Length | Format-Table -AutoSize
exit 0
