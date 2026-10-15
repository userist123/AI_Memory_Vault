$ErrorActionPreference = "Stop"

$root = Join-Path $env:LOCALAPPDATA "AI_Memory_Vault_QdrantFix"
$exe = Join-Path $root "bin\qdrant.exe"
$storage = Join-Path $root "storage"
$stdoutLog = Join-Path $root "qdrant.stdout.log"
$stderrLog = Join-Path $root "qdrant.stderr.log"
$healthUrl = "http://127.0.0.1:6333/healthz"

if (-not (Test-Path -LiteralPath $exe)) {
    throw "Qdrant binary not found at '$exe'. Install the official Windows x64 release first."
}

New-Item -ItemType Directory -Force -Path $storage | Out-Null

$listeners = @(Get-NetTCPConnection -LocalPort 6333, 6334 -State Listen -ErrorAction SilentlyContinue)
if ($listeners.Count -gt 0) {
    try {
        $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 3
        if ($health -ne "healthz check passed") {
            throw "Port 6333 is occupied but did not return the expected Qdrant health response."
        }
        if (@($listeners | Where-Object { $_.LocalAddress -ne "127.0.0.1" }).Count -gt 0) {
            throw "Qdrant is listening outside loopback. Stop it and restart with this script."
        }
        Write-Output "Qdrant is already running on 127.0.0.1:6333."
        return
    }
    catch {
        throw "Ports 6333/6334 are already in use; refusing to start another Qdrant process. $($_.Exception.Message)"
    }
}

$env:QDRANT__STORAGE__STORAGE_PATH = $storage
$env:QDRANT__SERVICE__HOST = "127.0.0.1"
$env:QDRANT__SERVICE__HTTP_PORT = "6333"
$env:QDRANT__SERVICE__GRPC_PORT = "6334"

$process = Start-Process -FilePath $exe -WorkingDirectory $root -PassThru -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog

for ($attempt = 0; $attempt -lt 20; $attempt++) {
    Start-Sleep -Seconds 1
    if ($process.HasExited) {
        $errorTail = if (Test-Path $stderrLog) { Get-Content $stderrLog -Tail 30 } else { @() }
        throw "Qdrant exited during startup (code $($process.ExitCode)). $($errorTail -join [Environment]::NewLine)"
    }

    try {
        $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 2
        if ($health -eq "healthz check passed") {
            $listeners = @(Get-NetTCPConnection -LocalPort 6333, 6334 -State Listen -ErrorAction SilentlyContinue)
            if ($listeners.Count -lt 2 -or @($listeners | Where-Object { $_.LocalAddress -ne "127.0.0.1" }).Count -gt 0) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                throw "Qdrant health passed but ports are not bound exclusively to 127.0.0.1."
            }
            Write-Output "Qdrant started successfully (PID $($process.Id))."
            Write-Output "HTTP endpoint: http://127.0.0.1:6333"
            Write-Output "gRPC endpoint: 127.0.0.1:6334"
            Write-Output "Storage: $storage"
            Write-Output "Logs: $stdoutLog ; $stderrLog"
            return
        }
    }
    catch {
        if ($attempt -eq 19) {
            $errorTail = if (Test-Path $stderrLog) { Get-Content $stderrLog -Tail 30 } else { @() }
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Qdrant did not become healthy within 20 seconds. $($_.Exception.Message) $($errorTail -join [Environment]::NewLine)"
        }
    }
}

throw "Qdrant startup timed out."
