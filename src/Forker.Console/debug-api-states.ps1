# API State Debugging Harness
# Purpose: Capture live API responses during file processing to debug state mapping issues
# Usage: .\debug-api-states.ps1
#
# This script:
# 1. Copies test files from DestinationA to Input to trigger processing
# 2. Polls both C# API (port 8081) and Go Console API (port 5000) every 0.1s
# 3. Captures full JSON responses with timestamps
# 4. Lists Input and Destination directories after each poll
# 5. Automatically stops when no activity for 30 seconds (Input folder empty)
# 6. Writes all output to timestamped log file

param(
    [int]$PollIntervalMs = 100,
    [int]$IdleTimeoutSeconds = 30,
    [int]$MaxDurationSeconds = 300,  # 5 minute safety limit
    [string]$SourceDir = "C:\ForkerDemo\DestinationA",
    [string]$InputDir = "C:\ForkerDemo\Input",
    [string]$DestinationA = "C:\ForkerDemo\DestinationA",
    [string]$DestinationB = "C:\ForkerDemo\DestinationB"
)

# Create timestamped log file
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logFile = "api-debug-$timestamp.log"

function Write-Log {
    param([string]$Message)
    $timeStr = Get-Date -Format "HH:mm:ss.fff"
    $output = "[$timeStr] $Message"
    Write-Host $output
    Add-Content -Path $logFile -Value $output
}

function Get-JsonResponse {
    param([string]$Url)
    try {
        $response = Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec 2
        return $response | ConvertTo-Json -Depth 10
    } catch {
        return "ERROR: $_"
    }
}

function Get-DirectoryListing {
    param([string]$Path)
    try {
        $files = Get-ChildItem -Path $Path -Filter *.svs -ErrorAction SilentlyContinue
        return "Files: $($files.Count) | $(($files | ForEach-Object { $_.Name }) -join ', ')"
    } catch {
        return "ERROR: $_"
    }
}

function Get-MostRecentJobTime {
    param([string]$JobsJson)
    try {
        $jobs = $JobsJson | ConvertFrom-Json
        $recentTime = $null

        foreach ($job in $jobs) {
            if ($job.state -in @('Discovered', 'Queued', 'InProgress', 'Partial')) {
                $jobTime = [DateTime]::Parse($job.createdAt)
                if ($null -eq $recentTime -or $jobTime -gt $recentTime) {
                    $recentTime = $jobTime
                }
            }
        }

        return $recentTime
    } catch {
        return $null
    }
}

Write-Log "==================================="
Write-Log "API State Debugging Harness Started"
Write-Log "==================================="
Write-Log "Poll Interval: $PollIntervalMs ms"
Write-Log "Idle Timeout: $IdleTimeoutSeconds seconds (stops when no active jobs for this duration)"
Write-Log "Max Duration: $MaxDurationSeconds seconds (safety limit)"
Write-Log "Log File: $logFile"
Write-Log ""

# Step 1: Copy files to trigger processing
Write-Log "STEP 1: Copying files from $SourceDir to $InputDir"
try {
    $files = Get-ChildItem -Path $SourceDir -Filter *.svs
    Write-Log "Found $($files.Count) files to copy"

    if ($files.Count -eq 0) {
        Write-Log "ERROR: No .svs files found in $SourceDir"
        Write-Log "Please ensure source directory has test files"
        exit 1
    }

    foreach ($file in $files) {
        Copy-Item -Path $file.FullName -Destination $InputDir -Force
        Write-Log "  Copied: $($file.Name) ($([math]::Round($file.Length / 1MB, 2)) MB)"
    }

    Write-Log "File copy complete - processing should start now"
} catch {
    Write-Log "ERROR copying files: $_"
    exit 1
}

Write-Log ""

# Step 2: Poll APIs and capture states
$pollCount = 0
$lastActiveTime = Get-Date
$startTime = Get-Date
$noActivityCount = 0

Write-Log "STEP 2: Polling APIs every $PollIntervalMs ms until no activity for $IdleTimeoutSeconds seconds"
Write-Log ""

while ($true) {
    # Check maximum duration safety limit
    $elapsedTime = (Get-Date) - $startTime
    if ($elapsedTime.TotalSeconds -ge $MaxDurationSeconds) {
        Write-Log ""
        Write-Log "==================================="
        Write-Log "MAXIMUM DURATION REACHED"
        Write-Log "Test ran for $([math]::Round($elapsedTime.TotalSeconds, 1))s (max: ${MaxDurationSeconds}s)"
        Write-Log "Stopping harness for safety"
        Write-Log "==================================="
        break
    }

    $pollCount++
    Write-Log "===== POLL #$pollCount ====="

    # Get active jobs from C# API
    Write-Log "--- C# API (port 8081) ---"
    $csharpJobs = Get-JsonResponse -Url "http://localhost:8081/api/monitoring/jobs?limit=100"
    Write-Log "Jobs List Response:"
    Write-Log $csharpJobs

    # Parse active job IDs
    $activeJobIds = @()
    try {
        $jobsObj = $csharpJobs | ConvertFrom-Json
        foreach ($job in $jobsObj) {
            if ($job.state -in @('Discovered', 'Queued', 'InProgress', 'Partial')) {
                $activeJobIds += $job.jobId
            }
        }
    } catch {
        Write-Log "Could not parse jobs response"
    }

    Write-Log "Active jobs: $($activeJobIds.Count)"

    # Get details for each active job from C# API
    foreach ($jobId in $activeJobIds) {
        Write-Log "Job Details (C# API): $jobId"
        $details = Get-JsonResponse -Url "http://localhost:8081/api/monitoring/jobs/$jobId"
        Write-Log $details
    }

    # Get same jobs from Go Console API
    Write-Log ""
    Write-Log "--- Go Console API (port 5000) ---"
    foreach ($jobId in $activeJobIds) {
        Write-Log "Job Details (Go API): $jobId"
        $details = Get-JsonResponse -Url "http://localhost:5000/api/jobs/$jobId"
        Write-Log $details
    }

    # List directory contents
    Write-Log ""
    Write-Log "--- Directory Listings ---"
    $inputListing = Get-DirectoryListing -Path $InputDir
    Write-Log "Input:        $inputListing"
    Write-Log "DestinationA: $(Get-DirectoryListing -Path $DestinationA)"
    Write-Log "DestinationB: $(Get-DirectoryListing -Path $DestinationB)"

    # Check if we have any active jobs
    if ($activeJobIds.Count -gt 0) {
        $lastActiveTime = Get-Date
        $noActivityCount = 0
        Write-Log "Activity detected - resetting idle timer"
    } else {
        $timeSinceActive = (Get-Date) - $lastActiveTime
        $noActivityCount++
        Write-Log "No active jobs - idle for $([math]::Round($timeSinceActive.TotalSeconds, 1))s (max: ${IdleTimeoutSeconds}s)"

        if ($timeSinceActive.TotalSeconds -ge $IdleTimeoutSeconds) {
            Write-Log ""
            Write-Log "==================================="
            Write-Log "IDLE TIMEOUT REACHED"
            Write-Log "No active jobs for $IdleTimeoutSeconds seconds"
            Write-Log "Processing complete - stopping harness"
            Write-Log "==================================="
            break
        }
    }

    Write-Log ""

    # Wait before next poll
    Start-Sleep -Milliseconds $PollIntervalMs
}

Write-Log ""
Write-Log "==================================="
Write-Log "Polling complete"
Write-Log "Total polls: $pollCount"
Write-Log "Log saved to: $logFile"
Write-Log "==================================="

# Analyze the captured data
Write-Host ""
Write-Host "CAPTURED DATA ANALYSIS:" -ForegroundColor Yellow

# Count unique copyState values
Write-Host ""
Write-Host "Target Copy States Captured:" -ForegroundColor Cyan
$capturedStates = Select-String -Path $logFile -Pattern '"copyState": "([^"]*)"' -AllMatches |
    ForEach-Object { $_.Matches.Groups[1].Value } |
    Group-Object |
    Sort-Object Count -Descending

if ($capturedStates) {
    foreach ($state in $capturedStates) {
        Write-Host "  $($state.Name): $($state.Count) occurrences" -ForegroundColor White
    }
} else {
    Write-Host "  No copyState values found - may indicate field mapping issue!" -ForegroundColor Red
}

# Expected states
Write-Host ""
Write-Host "Expected States (from TargetCopyState.cs):" -ForegroundColor Cyan
$expectedStates = @('Pending', 'Copying', 'Copied', 'Verifying', 'Verified', 'FailedRetryable', 'FailedPermanent')
foreach ($expected in $expectedStates) {
    $found = $capturedStates | Where-Object { $_.Name -eq $expected }
    if ($found) {
        Write-Host "  ✓ $expected" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $expected (not captured)" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "ANALYSIS TIPS:" -ForegroundColor Yellow
Write-Host "1. Review log file: $logFile" -ForegroundColor Cyan
Write-Host "2. Search for field mismatches:" -ForegroundColor Cyan
Write-Host "   Select-String -Path $logFile -Pattern '`"state`": `"`"'" -ForegroundColor DarkGray
Write-Host "3. Compare C# vs Go API responses:" -ForegroundColor Cyan
Write-Host "   Select-String -Path $logFile -Pattern 'Job Details' -Context 15" -ForegroundColor DarkGray
Write-Host "4. Track state transitions for a specific job:" -ForegroundColor Cyan
Write-Host "   Select-String -Path $logFile -Pattern '<job-id>' -Context 5" -ForegroundColor DarkGray
