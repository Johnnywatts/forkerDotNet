#Requires -Version 5.1

# Simple ForkerDotNet Test Script
# Tests basic file replication without fancy formatting

param(
    [string]$DemoPath = "C:\ForkerDemo",
    [int]$FileSizeMB = 10
)

Write-Host "========================================" -ForegroundColor Green
Write-Host "Simple ForkerDotNet Test" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

# Step 1: Create test file
Write-Host "Step 1: Creating test file ($FileSizeMB MB)..." -ForegroundColor Cyan
$testFileName = "test-$(Get-Date -Format 'yyyyMMdd-HHmmss').svs"
$testFilePath = Join-Path "$DemoPath\Reservoir" $testFileName

# Create file with random data
$bytes = New-Object byte[] (1MB)
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$stream = [System.IO.File]::Create($testFilePath)

for ($i = 0; $i -lt $FileSizeMB; $i++) {
    $rng.GetBytes($bytes)
    $stream.Write($bytes, 0, $bytes.Length)
}

$stream.Close()
$rng.Dispose()

Write-Host "[OK] Created: $testFileName ($FileSizeMB MB)" -ForegroundColor Green
Write-Host ""

# Step 2: Calculate source hash
Write-Host "Step 2: Calculating source hash..." -ForegroundColor Cyan
$sourceHash = (Get-FileHash -Path $testFilePath -Algorithm SHA256).Hash
Write-Host "[OK] Source Hash: $sourceHash" -ForegroundColor Green
Write-Host ""

# Step 3: Move to Input folder
Write-Host "Step 3: Moving file to Input folder..." -ForegroundColor Cyan
$inputPath = Join-Path "$DemoPath\Input" $testFileName
Move-Item -Path $testFilePath -Destination $inputPath -Force
Write-Host "[OK] File moved to Input" -ForegroundColor Green
Write-Host "[INFO] ForkerDotNet should detect it within 5 seconds" -ForegroundColor Yellow
Write-Host ""

# Step 4: Wait for replication
Write-Host "Step 4: Waiting for replication..." -ForegroundColor Cyan
$destA = Join-Path "$DemoPath\DestinationA" $testFileName
$destB = Join-Path "$DemoPath\DestinationB" $testFileName

$startTime = Get-Date
$timeout = 120 # 2 minutes
$completed = $false

while (-not $completed -and ((Get-Date) - $startTime).TotalSeconds -lt $timeout) {
    $aExists = Test-Path $destA
    $bExists = Test-Path $destB

    if ($aExists -and $bExists) {
        $aSize = (Get-Item $destA).Length
        $bSize = (Get-Item $destB).Length
        $expectedSize = $FileSizeMB * 1MB

        if ($aSize -ge $expectedSize -and $bSize -ge $expectedSize) {
            $completed = $true
            Write-Host "[OK] Both files copied completely" -ForegroundColor Green
        } else {
            $progressA = [math]::Round(($aSize / $expectedSize) * 100, 0)
            $progressB = [math]::Round(($bSize / $expectedSize) * 100, 0)
            Write-Host "`r  Progress: A=$progressA% B=$progressB%   " -NoNewline -ForegroundColor Yellow
            Start-Sleep -Seconds 2
        }
    } else {
        Write-Host "." -NoNewline -ForegroundColor Gray
        Start-Sleep -Seconds 2
    }
}

Write-Host ""

if (-not $completed) {
    Write-Host "[ERROR] Timeout waiting for replication" -ForegroundColor Red
    Write-Host "[INFO] Check if ForkerDotNet service is running" -ForegroundColor Yellow
    exit 1
}

$duration = ((Get-Date) - $startTime).TotalSeconds
Write-Host "[OK] Replication completed in $([math]::Round($duration, 1)) seconds" -ForegroundColor Green
Write-Host ""

# Step 5: Verify hashes
Write-Host "Step 5: Verifying file integrity..." -ForegroundColor Cyan
$hashA = (Get-FileHash -Path $destA -Algorithm SHA256).Hash
$hashB = (Get-FileHash -Path $destB -Algorithm SHA256).Hash

Write-Host "  Source:        $sourceHash" -ForegroundColor Cyan
Write-Host "  Destination A: $hashA" -ForegroundColor $(if ($hashA -eq $sourceHash) { "Green" } else { "Red" })
Write-Host "  Destination B: $hashB" -ForegroundColor $(if ($hashB -eq $sourceHash) { "Green" } else { "Red" })
Write-Host ""

if ($hashA -eq $sourceHash -and $hashB -eq $sourceHash) {
    Write-Host "[OK] Hash verification PASSED - No corruption" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Hash verification FAILED" -ForegroundColor Red
    exit 1
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Test Complete - SUCCESS" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Results:" -ForegroundColor Cyan
Write-Host "  File Size:    $FileSizeMB MB"
Write-Host "  Duration:     $([math]::Round($duration, 1)) seconds"
Write-Host "  Throughput:   $([math]::Round($FileSizeMB / $duration * 60, 1)) MB/min per target"
Write-Host "  Data Integrity: VERIFIED (SHA-256 match)"
Write-Host ""
Write-Host "ForkerDotNet is working correctly!" -ForegroundColor Green
