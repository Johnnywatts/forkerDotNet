# Hash Timing Test
# Purpose: Measure SHA-256 hash calculation time using the same approach as ForkerDotNet
# This proves whether hash verification can happen within 100ms polling interval

param(
    [string]$SlowDriveFile = "E:\ForkerDotNetTestVolume\DestinationA\484759.svs",
    [string]$FastDriveFile = "C:\ForkerDemo\DestinationA\484759.svs"
)

function Test-HashTiming {
    param(
        [string]$FilePath,
        [string]$Description
    )

    if (-not (Test-Path $FilePath)) {
        Write-Host "File not found: $FilePath" -ForegroundColor Red
        return
    }

    $fileInfo = Get-Item $FilePath
    $fileSizeMB = [math]::Round($fileInfo.Length / 1MB, 2)

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Testing: $Description" -ForegroundColor Cyan
    Write-Host "File: $FilePath" -ForegroundColor White
    Write-Host "Size: $fileSizeMB MB" -ForegroundColor White
    Write-Host "========================================" -ForegroundColor Cyan

    # Use SHA-256 with 1MB buffer (matching ForkerDotNet implementation)
    $bufferSize = 1MB
    $sha256 = [System.Security.Cryptography.SHA256]::Create()

    try {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

        $fileStream = [System.IO.File]::OpenRead($FilePath)
        $buffer = New-Object byte[] $bufferSize
        $totalBytesRead = 0

        while (($bytesRead = $fileStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $sha256.TransformBlock($buffer, 0, $bytesRead, $null, 0) | Out-Null
            $totalBytesRead += $bytesRead
        }

        $sha256.TransformFinalBlock($buffer, 0, 0) | Out-Null
        $hash = [System.BitConverter]::ToString($sha256.Hash).Replace("-", "").ToLower()

        $stopwatch.Stop()
        $fileStream.Close()

        $elapsedMs = $stopwatch.ElapsedMilliseconds
        $elapsedSec = [math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
        $throughputMBps = [math]::Round($fileSizeMB / $stopwatch.Elapsed.TotalSeconds, 2)

        Write-Host ""
        Write-Host "Results:" -ForegroundColor Green
        Write-Host "  Hash:       $hash" -ForegroundColor White
        Write-Host "  Duration:   $elapsedMs ms ($elapsedSec seconds)" -ForegroundColor Yellow
        Write-Host "  Throughput: $throughputMBps MB/s" -ForegroundColor White
        Write-Host ""

        # Compare against 100ms polling interval
        $pollingIntervals = [math]::Floor($elapsedMs / 100)
        if ($elapsedMs -lt 100) {
            Write-Host "✓ Hash calculation completed WITHIN 100ms polling interval" -ForegroundColor Green
        } else {
            Write-Host "✗ Hash calculation took $pollingIntervals polling intervals (100ms each)" -ForegroundColor Red
            Write-Host "  This means verification state could change between polls!" -ForegroundColor Yellow
        }

        return @{
            FilePath = $FilePath
            SizeMB = $fileSizeMB
            DurationMs = $elapsedMs
            DurationSec = $elapsedSec
            ThroughputMBps = $throughputMBps
            Hash = $hash
        }
    }
    catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
        return $null
    }
    finally {
        if ($fileStream) { $fileStream.Dispose() }
        if ($sha256) { $sha256.Dispose() }
    }
}

Write-Host ""
Write-Host "SHA-256 Hash Timing Test" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host "This test measures hash calculation time using the same approach as ForkerDotNet"
Write-Host "Buffer size: 1MB (matching ForkerDotNet HashingService.cs line 16)"
Write-Host "Polling interval: 100ms"
Write-Host ""

# Test slow drive
$slowResult = Test-HashTiming -FilePath $SlowDriveFile -Description "SLOW DRIVE (Spinning Disk)"

# Test fast drive
$fastResult = Test-HashTiming -FilePath $FastDriveFile -Description "FAST DRIVE (SSD)"

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SUMMARY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if ($slowResult -and $fastResult) {
    $speedup = [math]::Round($slowResult.DurationMs / $fastResult.DurationMs, 2)

    Write-Host ""
    Write-Host "Slow Drive: $($slowResult.DurationMs) ms ($($slowResult.DurationSec) sec)" -ForegroundColor White
    Write-Host "Fast Drive: $($fastResult.DurationMs) ms ($($fastResult.DurationSec) sec)" -ForegroundColor White
    Write-Host "Speedup:    ${speedup}x faster on SSD" -ForegroundColor Green
    Write-Host ""

    # Calculate how many polling intervals each takes
    $slowPolls = [math]::Floor($slowResult.DurationMs / 100)
    $fastPolls = [math]::Floor($fastResult.DurationMs / 100)

    Write-Host "Polling Interval Analysis (100ms):" -ForegroundColor Yellow
    Write-Host "  Slow Drive: $slowPolls polling intervals to verify" -ForegroundColor White
    Write-Host "  Fast Drive: $fastPolls polling intervals to verify" -ForegroundColor White
    Write-Host ""

    if ($fastPolls -gt 0) {
        Write-Host "CONCLUSION:" -ForegroundColor Yellow
        Write-Host "Even on fast SSD, hash verification takes ${fastPolls}+ polling intervals." -ForegroundColor Red
        Write-Host "The 'Verifying' state transitions too quickly to capture via API polling." -ForegroundColor Red
        Write-Host "Database-level state change logging is required to observe all transitions." -ForegroundColor Green
    }
}

Write-Host ""
