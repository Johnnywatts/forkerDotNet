# Test Script: State History API End-to-End Verification
# Purpose: Verify that state transitions are logged and accessible via API
# This script can be run repeatedly to test the implementation

param(
    [string]$SourceDir = "C:\ForkerDemo\DestinationA",
    [string]$InputDir = "C:\ForkerDemo\Input",
    [string]$ApiBaseUrl = "http://localhost:8081/api/monitoring",
    [int]$MaxWaitSeconds = 120
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "State History API Test" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Step 0: Pre-flight checks
Write-Host "STEP 0: Pre-flight checks" -ForegroundColor Yellow
Write-Host ""

# Check if API is responding
try {
    $health = Invoke-RestMethod -Uri "$ApiBaseUrl/health" -Method Get -ErrorAction Stop
    Write-Host "  ✓ API is responding" -ForegroundColor Green
    Write-Host "    Process ID: $($health.processId)" -ForegroundColor Gray
    Write-Host "    Uptime: $($health.uptime)" -ForegroundColor Gray
    Write-Host ""
} catch {
    Write-Host "  ✗ API is not responding at $ApiBaseUrl" -ForegroundColor Red
    Write-Host "    Please start the service: dotnet run --project src/Forker.Service" -ForegroundColor Red
    exit 1
}

# Check if source directory exists and has files
if (-not (Test-Path $SourceDir)) {
    Write-Host "  ✗ Source directory not found: $SourceDir" -ForegroundColor Red
    exit 1
}

$availableFiles = Get-ChildItem $SourceDir -Filter "*.svs" | Sort-Object Length
if ($availableFiles.Count -eq 0) {
    Write-Host "  ✗ No SVS files found in $SourceDir" -ForegroundColor Red
    exit 1
}

Write-Host "  ✓ Source directory has $($availableFiles.Count) SVS files" -ForegroundColor Green
Write-Host ""

# Step 1: Select and copy a test file
Write-Host "STEP 1: Copy test file to Input directory" -ForegroundColor Yellow
Write-Host ""

# Use a small file for faster testing (or first file if all are large)
$testFile = $availableFiles | Select-Object -First 1
$testFileName = $testFile.Name
$testFileSizeMB = [math]::Round($testFile.Length / 1MB, 2)

Write-Host "  Selected file: $testFileName" -ForegroundColor White
Write-Host "  File size: $testFileSizeMB MB" -ForegroundColor Gray
Write-Host ""

# Remove any existing file with same name in Input
$destPath = Join-Path $InputDir $testFileName
if (Test-Path $destPath) {
    Write-Host "  Removing existing file from Input..." -ForegroundColor Gray
    Remove-Item $destPath -Force
    Start-Sleep -Seconds 2
}

# Copy the file
Write-Host "  Copying file to Input directory..." -ForegroundColor White
$copyStartTime = Get-Date
Copy-Item $testFile.FullName $destPath -Force
$copyDuration = (Get-Date) - $copyStartTime

Write-Host "  ✓ File copied in $([math]::Round($copyDuration.TotalSeconds, 2))s" -ForegroundColor Green
Write-Host "  Path: $destPath" -ForegroundColor Gray
Write-Host ""

# Step 2: Wait for file to be discovered and get job ID
Write-Host "STEP 2: Wait for job creation and get Job ID" -ForegroundColor Yellow
Write-Host ""

$jobId = $null
$maxAttempts = 20
$attemptDelay = 1

for ($i = 1; $i -le $maxAttempts; $i++) {
    Write-Host "  Attempt $i/$maxAttempts - Querying jobs API..." -ForegroundColor Gray

    try {
        $jobs = Invoke-RestMethod -Uri "$ApiBaseUrl/jobs" -Method Get -ErrorAction Stop
        $matchingJob = $jobs | Where-Object { $_.sourcePath -like "*$testFileName" } | Select-Object -First 1

        if ($matchingJob) {
            $jobId = $matchingJob.jobId
            $jobState = $matchingJob.state
            Write-Host "  ✓ Job found!" -ForegroundColor Green
            Write-Host "    Job ID: $jobId" -ForegroundColor White
            Write-Host "    State: $jobState" -ForegroundColor White
            Write-Host ""
            break
        }
    } catch {
        Write-Host "  API error: $($_.Exception.Message)" -ForegroundColor Red
    }

    Start-Sleep -Seconds $attemptDelay
}

if (-not $jobId) {
    Write-Host "  ✗ Job not found after $maxAttempts attempts" -ForegroundColor Red
    Write-Host "  The file may not have been discovered yet or discovery is not working" -ForegroundColor Red
    exit 1
}

# Step 3: Wait for job to complete
Write-Host "STEP 3: Wait for job completion" -ForegroundColor Yellow
Write-Host ""

$startWait = Get-Date
$jobCompleted = $false

while (((Get-Date) - $startWait).TotalSeconds -lt $MaxWaitSeconds) {
    try {
        $jobDetails = Invoke-RestMethod -Uri "$ApiBaseUrl/jobs/$jobId" -Method Get -ErrorAction Stop
        $currentState = $jobDetails.state

        Write-Host "  Current state: $currentState (elapsed: $([math]::Round(((Get-Date) - $startWait).TotalSeconds, 1))s)" -ForegroundColor Gray

        if ($currentState -eq "Verified") {
            $jobCompleted = $true
            Write-Host "  ✓ Job completed successfully!" -ForegroundColor Green
            Write-Host ""
            break
        }

        if ($currentState -eq "Failed" -or $currentState -eq "Quarantined") {
            Write-Host "  ✗ Job failed with state: $currentState" -ForegroundColor Red
            exit 1
        }

        Start-Sleep -Seconds 2
    } catch {
        Write-Host "  API error: $($_.Exception.Message)" -ForegroundColor Red
        Start-Sleep -Seconds 2
    }
}

if (-not $jobCompleted) {
    Write-Host "  ✗ Job did not complete within $MaxWaitSeconds seconds" -ForegroundColor Red
    Write-Host "  Last known state: $currentState" -ForegroundColor Red
    exit 1
}

# Step 4: Retrieve state history via new API endpoint
Write-Host "STEP 4: Retrieve state history via API" -ForegroundColor Yellow
Write-Host ""

try {
    $stateHistory = Invoke-RestMethod -Uri "$ApiBaseUrl/jobs/$jobId/state-history" -Method Get -ErrorAction Stop
    Write-Host "  ✓ State history retrieved" -ForegroundColor Green
    Write-Host "  Total transitions: $($stateHistory.Count)" -ForegroundColor White
    Write-Host ""
} catch {
    Write-Host "  ✗ Failed to retrieve state history" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Step 5: Verify expected state transitions
Write-Host "STEP 5: Verify state transitions" -ForegroundColor Yellow
Write-Host ""

# Display all transitions in a readable format
Write-Host "  State Transition Timeline:" -ForegroundColor White
Write-Host "  " + ("-" * 80) -ForegroundColor Gray

foreach ($entry in $stateHistory) {
    $timestamp = ([DateTime]$entry.timestamp).ToString("HH:mm:ss.fff")
    $entityInfo = if ($entry.entityId) { "$($entry.entityType) '$($entry.entityId)'" } else { $entry.entityType }
    $stateChange = if ($entry.oldState) { "$($entry.oldState) → $($entry.newState)" } else { "→ $($entry.newState)" }
    $duration = if ($entry.durationMs) { " (${entry.durationMs}ms)" } else { "" }

    Write-Host "  [$timestamp] $entityInfo : $stateChange$duration" -ForegroundColor Cyan
}

Write-Host "  " + ("-" * 80) -ForegroundColor Gray
Write-Host ""

# Step 6: Validate expected transitions exist
Write-Host "STEP 6: Validate expected transitions" -ForegroundColor Yellow
Write-Host ""

$validationResults = @{
    "Job state transitions" = $false
    "Target 'Verifying' states" = $false
    "Target 'Verified' states" = $false
    "Duration measurements" = $false
    "Additional context" = $false
}

# Check for job state transitions
$jobTransitions = $stateHistory | Where-Object { $_.entityType -eq "Job" }
if ($jobTransitions.Count -gt 0) {
    $validationResults["Job state transitions"] = $true
    Write-Host "  ✓ Job state transitions found: $($jobTransitions.Count)" -ForegroundColor Green
} else {
    Write-Host "  ✗ No job state transitions found" -ForegroundColor Red
}

# Check for Verifying states
$verifyingStates = $stateHistory | Where-Object { $_.newState -eq "Verifying" }
if ($verifyingStates.Count -gt 0) {
    $validationResults["Target 'Verifying' states"] = $true
    Write-Host "  ✓ 'Verifying' states captured: $($verifyingStates.Count)" -ForegroundColor Green
    foreach ($state in $verifyingStates) {
        Write-Host "    - Target: $($state.entityId) at $($state.timestamp)" -ForegroundColor Gray
    }
} else {
    Write-Host "  ✗ No 'Verifying' states found - THIS IS THE KEY TEST!" -ForegroundColor Red
}

# Check for Verified states
$verifiedStates = $stateHistory | Where-Object { $_.newState -eq "Verified" }
if ($verifiedStates.Count -gt 0) {
    $validationResults["Target 'Verified' states"] = $true
    Write-Host "  ✓ 'Verified' states captured: $($verifiedStates.Count)" -ForegroundColor Green
} else {
    Write-Host "  ✗ No 'Verified' states found" -ForegroundColor Red
}

# Check for duration measurements on Verified states
$statesWithDuration = $stateHistory | Where-Object { $_.durationMs -ne $null -and $_.durationMs -gt 0 }
if ($statesWithDuration.Count -gt 0) {
    $validationResults["Duration measurements"] = $true
    Write-Host "  ✓ Duration measurements found: $($statesWithDuration.Count)" -ForegroundColor Green

    # Calculate verification times
    $verificationTimes = $verifiedStates | Where-Object { $_.durationMs -gt 0 } | ForEach-Object { $_.durationMs }
    if ($verificationTimes.Count -gt 0) {
        $avgVerification = [math]::Round(($verificationTimes | Measure-Object -Average).Average, 0)
        Write-Host "    - Average verification time: ${avgVerification}ms" -ForegroundColor Gray
    }
} else {
    Write-Host "  ✗ No duration measurements found" -ForegroundColor Red
}

# Check for additional context
$statesWithContext = $stateHistory | Where-Object { $_.additionalContext -ne $null -and $_.additionalContext -ne "" }
if ($statesWithContext.Count -gt 0) {
    $validationResults["Additional context"] = $true
    Write-Host "  ✓ Additional context stored: $($statesWithContext.Count) entries" -ForegroundColor Green
} else {
    Write-Host "  ✗ No additional context found" -ForegroundColor Red
}

Write-Host ""

# Step 7: Final summary
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

$passedTests = ($validationResults.Values | Where-Object { $_ -eq $true }).Count
$totalTests = $validationResults.Count

Write-Host "Test Results: $passedTests/$totalTests passed" -ForegroundColor White
Write-Host ""

foreach ($test in $validationResults.GetEnumerator()) {
    $status = if ($test.Value) { "✓ PASS" } else { "✗ FAIL" }
    $color = if ($test.Value) { "Green" } else { "Red" }
    Write-Host "  $status - $($test.Key)" -ForegroundColor $color
}

Write-Host ""
Write-Host "Test file: $testFileName" -ForegroundColor Gray
Write-Host "Job ID: $jobId" -ForegroundColor Gray
Write-Host ""

# Exit with appropriate code
if ($passedTests -eq $totalTests) {
    Write-Host "✓ ALL TESTS PASSED" -ForegroundColor Green
    Write-Host ""
    exit 0
} else {
    Write-Host "✗ SOME TESTS FAILED" -ForegroundColor Red
    Write-Host ""
    exit 1
}
