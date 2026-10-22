param(
    [string]$Root = ''
)

. (Join-Path $PSScriptRoot 'lib\Common.ps1')
$Root = Resolve-Root $Root
$python = Get-PythonExe $Root
$pythonDir = Split-Path -Parent $python

$env:TMP = Join-Path $Root 'temp'
$env:TEMP = $env:TMP
New-Item -ItemType Directory -Path $env:TMP -Force | Out-Null

Write-Step "Verifying bundled Python 3.12.10"
if (-not (Test-Path -LiteralPath $python)) {
    Write-Warn "Bundled runtime is missing: $python"
    Write-Host "Trying to copy a local Python 3.12 installation ..."

    $candidates = New-Object System.Collections.ArrayList
    $launcher = Get-Command py -ErrorAction SilentlyContinue
    if ($launcher) {
        try {
            $prefix = & py -3.12 -c "import sys;print(sys.prefix)" 2>$null
            if ($prefix) { $null = $candidates.Add($prefix.Trim()) }
        } catch { }
    }
    $null = $candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312'))
    $null = $candidates.Add('C:\Python312')

    $source = $null
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'python.exe')) {
            $candidateVersion = & (Join-Path $candidate 'python.exe') -c "import sys;print(sys.version.split()[0])" 2>$null
            if ($candidateVersion -like '3.12.*') { $source = $candidate; break }
        }
    }

    if (-not $source) {
        throw "No local Python 3.12 found. Place a portable CPython 3.12.10 in $pythonDir and rerun."
    }

    Write-Host "Copying base runtime from $source (site-packages excluded) ..."
    New-Item -ItemType Directory -Path $pythonDir -Force | Out-Null
    $excludeDirs = @(
        (Join-Path $source 'Lib\site-packages'),
        (Join-Path $source 'tcl'),
        (Join-Path $source 'Doc'),
        (Join-Path $source 'include'),
        (Join-Path $source 'libs'),
        (Join-Path $source 'share')
    )
    & robocopy.exe $source $pythonDir /E /XD @excludeDirs /XF NEWS.txt readme.rst /NFL /NDL /NJH /NJS /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "robocopy failed with exit code $LASTEXITCODE" }
}

$version = & $python -c "import sys;print(sys.version.split()[0])"
Write-Host "Python version: $version"
if ($version -notlike '3.12.*') {
    throw "Expected Python 3.12.x but found $version"
}

Write-Step "Ensuring pip is available"
& $python -c "import pip" 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    & $python -m ensurepip --upgrade
    if ($LASTEXITCODE -ne 0) { throw "ensurepip failed" }
}

& $python -m pip --version
if ($LASTEXITCODE -ne 0) { throw "pip is not usable" }

Write-Ok "Bundled Python is ready: $python"
exit 0
