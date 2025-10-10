# Test script to verify "Verifying" state is captured by API
# This script copies a large file and polls the API very rapidly to catch the Verifying state

$ErrorActionPreference = "Continue"

Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Testing 'Verifying' State Visibility" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

# Configuration
$apiUrl = "http://localhost:8081/api/monitoring/jobs"
$sourceDir = "C:\ForkerDemo\DestinationA"
$inputDir = "C:\ForkerDemo\Input"
$pollInterval = 100  # Poll every 100ms
$maxPolls = 200      # Poll for up to 20 seconds (200 * 100ms)

# Step 1: Find a large file to copy
Write-Host "STEP 1: Finding a large file to copy..." -ForegroundColor Yellow
$largeFile = Get-ChildItem $sourceDir -Filter "*.svs" | Sort-Object Length -Descending | Select-Object -First 1

if (-not $largeFile) {
    Write-Host "ERROR: No SVS files found in $sourceDir" -ForegroundColor Red
    exit 1
}

Write-Host "  Found: $($largeFile.Name) ($([math]::Round($largeFile.Length / 1GB, 2)) GB)" -ForegroundColor Green
Write-Host ""

# Step 2: Copy the file to Input directory
Write-Host "STEP 2: Copying file to Input directory..." -ForegroundColor Yellow
$destPath = Join-Path $inputDir $largeFile.Name
Copy-Item $largeFile.FullName $destPath -Force
Write-Host "  Copied to: $destPath" -ForegroundColor Green
Write-Host ""

# Step 3: Start rapid polling
Write-Host "STEP 3: Rapid API polling (every ${pollInterval}ms)..." -ForegroundColor Yellow
Write-Host ""

$verifyingStateCaptured = $false
$pollCount = 0

while ($pollCount -lt $maxPolls) {
    $pollCount++

    try {
        $response = Invoke-RestMethod -Uri $apiUrl -Method Get -ErrorAction Stop

        # Find our job
        $ourJob = $response | Where-Object { $_.sourcePath -like "*$($largeFile.Name)" } | Select-Object -First 1

        if ($ourJob) {
            $timestamp = (Get-Date).ToString("HH:mm:ss.fff")

            # Get target states if available
            if ($ourJob.targetOutcomes) {
                foreach ($target in $ourJob.targetOutcomes) {
                    $state = $target.state
                    $targetId = $target.targetId

                    Write-Host "[$timestamp] Poll #$pollCount - Job: $($ourJob.state), Target $targetId`: $state"

                    # Check if we captured Verifying state
                    if ($state -eq "Verifying") {
                        Write-Host ""
                        Write-Host "===========================================" -ForegroundColor Green
                        Write-Host "SUCCESS: 'Verifying' state CAPTURED!" -ForegroundColor Green
                        Write-Host "===========================================" -ForegroundColor Green
                        Write-Host "  Poll Number: $pollCount" -ForegroundColor Green
                        Write-Host "  Time Elapsed: $($pollCount * $pollInterval)ms" -ForegroundColor Green
                        Write-Host "  Job State: $($ourJob.state)" -ForegroundColor Green
                        Write-Host "  Target: $targetId" -ForegroundColor Green
                        Write-Host ""
                        $verifyingStateCaptured = $true
                    }
                }
            } else {
                Write-Host "[$timestamp] Poll #$pollCount - Job State: $($ourJob.state) (no target details)"
            }

            # Stop if job is verified
            if ($ourJob.state -eq "Verified") {
                Write-Host ""
                Write-Host "Job completed - State: Verified" -ForegroundColor Cyan
                break
            }
        }
    }
    catch {
        # API might not be ready yet
        if ($pollCount % 10 -eq 0) {
            Write-Host "  Waiting for API... (poll #$pollCount)" -ForegroundColor Gray
        }
    }

    Start-Sleep -Milliseconds $pollInterval
}

Write-Host ""
Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Test Complete" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan

if ($verifyingStateCaptured) {
    Write-Host "RESULT: Verifying state WAS captured!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "RESULT: Verifying state was NOT captured" -ForegroundColor Red
    Write-Host "  Total polls: $pollCount" -ForegroundColor Red
    Write-Host "  This suggests the state transition is too fast or not being persisted" -ForegroundColor Red
    exit 1
}
