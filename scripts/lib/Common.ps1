# Shared helpers for the ComfyUI deployment scripts (Windows PowerShell 5.1+).
$ErrorActionPreference = 'Stop'

function Resolve-Root {
    param([string]$Root)
    if ([string]::IsNullOrWhiteSpace($Root)) {
        return (Split-Path -Parent $PSScriptRoot)
    }
    return (Resolve-Path -LiteralPath $Root).Path
}

function Get-PythonExe {
    param([string]$Root)
    return (Join-Path (Join-Path (Join-Path $Root 'runtime') 'python312') 'python.exe')
}

function Get-VenvPython {
    param([string]$VenvPath)
    return (Join-Path (Join-Path $VenvPath 'Scripts') 'python.exe')
}

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host ("==> " + $Message) -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Message)
    Write-Host ("[OK] " + $Message) -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host ("[WARN] " + $Message) -ForegroundColor Yellow
}

function Quote-Arg {
    param([string]$Value)
    if ($null -eq $Value) { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }
    return '"' + ($Value -replace '"', '\"') + '"'
}

function Get-GitProxyArgs {
    param([string]$Proxy)
    if ([string]::IsNullOrWhiteSpace($Proxy)) {
        return @('-c', 'http.proxy=', '-c', 'https.proxy=')
    }
    return @('-c', ('http.proxy=' + $Proxy), '-c', ('https.proxy=' + $Proxy))
}

function Test-TcpPort {
    param([string]$HostName, [int]$Port, [int]$TimeoutMs = 1500)
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $task = $client.ConnectAsync($HostName, $Port)
        if (-not $task.Wait($TimeoutMs)) { $client.Close(); return $false }
        $client.Close()
        return $true
    } catch {
        return $false
    }
}

function Get-DirBytes {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    $sum = (Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
    if ($null -eq $sum) { return 0 }
    return [int64]$sum
}

function Stop-ProcessTree {
    param([int]$ProcessId)
    try { & taskkill.exe /PID $ProcessId /T /F 2>$null | Out-Null } catch { }
}

function Invoke-GitCloneAttempt {
    param(
        [string]$Url,
        [string]$Target,
        [string]$Proxy,
        [int]$TimeoutSec = 15,
        [int]$MinSpeedKBps = 80
    )

    $parent = Split-Path -Parent $Target
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $log = Join-Path $env:TEMP ("git-clone-" + [guid]::NewGuid().ToString('N') + ".log")

    $proxyArgs = Get-GitProxyArgs $Proxy
    $argList = @('-c', 'http.sslBackend=openssl') + $proxyArgs + @(
        'clone', '--depth', '1', '--single-branch', '--progress', $Url, $Target)
    $argLine = ($argList | ForEach-Object { Quote-Arg $_ }) -join ' '

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.Arguments = $argLine
    $psi.WorkingDirectory = $parent
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    $psi.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    $psi.EnvironmentVariables['GCM_INTERACTIVE'] = 'never'
    $psi.EnvironmentVariables['GIT_ASKPASS'] = 'echo'

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    $null = $proc.Start()
    $errTask = $proc.StandardError.ReadToEndAsync()
    $outTask = $proc.StandardOutput.ReadToEndAsync()

    $start = Get-Date
    $lastSize = -1
    $lastChange = $start
    $history = New-Object System.Collections.ArrayList

    while (-not $proc.HasExited) {
        Start-Sleep -Milliseconds 800
        if ($proc.HasExited) { break }

        $size = Get-DirBytes (Join-Path $Target '.git')
        $now = Get-Date
        if ($size -ne $lastSize) {
            $lastSize = $size
            $lastChange = $now
        }

        $silent = ($now - $lastChange).TotalSeconds
        $elapsed = ($now - $start).TotalSeconds

        if ($silent -gt $TimeoutSec -and $elapsed -gt $TimeoutSec) {
            Stop-ProcessTree $proc.Id
            Write-Warn ("git clone produced no new data for {0:N1}s; aborting." -f $silent)
            Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
            return 'NoResponse'
        }

        if ($elapsed -gt $TimeoutSec) {
            $null = $history.Add([pscustomobject]@{ Time = $elapsed; Bytes = $size })
            if ($history.Count -gt 8) { $history.RemoveAt(0) }
            if ($history.Count -ge 4) {
                $first = $history[0]
                $last = $history[$history.Count - 1]
                $span = $last.Time - $first.Time
                if ($span -ge 4) {
                    $kbps = (($last.Bytes - $first.Bytes) / 1KB) / $span
                    if ($kbps -lt $MinSpeedKBps) {
                        Stop-ProcessTree $proc.Id
                        Write-Warn ("git clone speed {0:N0} KB/s is below {1} KB/s; aborting." -f $kbps, $MinSpeedKBps)
                        Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
                        return 'TooSlow'
                    }
                }
            }
        }
    }

    try { $proc.WaitForExit(5000) | Out-Null } catch { }
    $stderr = ''
    try { $stderr = $errTask.Result } catch { }
    $stdout = ''
    try { $stdout = $outTask.Result } catch { }
    if ($stdout) { Write-Host $stdout }
    if ($stderr) { Write-Host $stderr }

    if ($proc.ExitCode -eq 0) {
        Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
        return 'Success'
    }

    Write-Warn ("git clone failed with exit code {0}." -f $proc.ExitCode)
    Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
    return 'Failed'
}

function Invoke-GitCloneSmart {
    param(
        [string]$Url,
        [string]$Target,
        [string]$Proxy = 'http://127.0.0.1:10108',
        [int]$TimeoutSec = 15,
        [int]$MinSpeedKBps = 80,
        [bool]$UseProxyFallback = $true
    )

    $existed = Test-Path -LiteralPath $Target

    if (Test-Path -LiteralPath (Join-Path $Target '.git')) {
        Write-Step "Updating existing repository: $Target"
        $proxyArgs = Get-GitProxyArgs ''
        & git -c http.sslBackend=openssl @proxyArgs -C $Target pull --ff-only
        if ($LASTEXITCODE -eq 0) { Write-Ok "Repository updated."; return $true }
        Write-Warn "git pull failed; try again after resolving local changes."
        return $false
    }

    Write-Step "Cloning ComfyUI (direct connection)..."
    $result = Invoke-GitCloneAttempt -Url $Url -Target $Target -Proxy '' -TimeoutSec $TimeoutSec -MinSpeedKBps $MinSpeedKBps
    if ($result -eq 'Success') { Write-Ok "Cloned to $Target"; return $true }

    if (-not $existed -and (Test-Path -LiteralPath $Target)) {
        Remove-Item -LiteralPath $Target -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (-not $UseProxyFallback) {
        Write-Warn "Clone failed and proxy fallback is disabled."
        return $false
    }

    $proxyHost = $null
    $proxyPort = 0
    try {
        $uri = [Uri]$Proxy
        $proxyHost = $uri.Host
        $proxyPort = $uri.Port
    } catch {
        Write-Warn "Invalid proxy address: $Proxy"
        return $false
    }

    if (-not (Test-TcpPort -HostName $proxyHost -Port $proxyPort)) {
        Write-Warn ("Proxy {0} is not reachable; please start it and retry." -f $Proxy)
        return $false
    }

    Write-Step ("Direct clone was {0}; retrying via proxy {1} ..." -f $result, $Proxy)
    $proxyResult = Invoke-GitCloneAttempt -Url $Url -Target $Target -Proxy $Proxy -TimeoutSec $TimeoutSec -MinSpeedKBps $MinSpeedKBps
    if ($proxyResult -eq 'Success') { Write-Ok "Cloned via proxy."; return $true }

    if (-not $existed -and (Test-Path -LiteralPath $Target)) {
        Remove-Item -LiteralPath $Target -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Warn ("Proxy clone failed: {0}" -f $proxyResult)
    return $false
}

function Invoke-Download {
    param(
        [string]$Root,
        [string]$Url,
        [string]$Destination,
        [string]$Proxy = '',
        [int]$Retries = 6
    )
    $python = Get-PythonExe $Root
    $downloader = Join-Path (Join-Path $Root 'scripts\lib') 'download.py'
    $args = @($downloader, $Url, $Destination, '--retries', $Retries)
    if (-not [string]::IsNullOrWhiteSpace($Proxy)) {
        $args += @('--proxy', $Proxy)
    }
    & $python @args
    return ($LASTEXITCODE -eq 0)
}
