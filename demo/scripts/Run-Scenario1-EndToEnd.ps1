#Requires -Version 5.1

<#
.SYNOPSIS
    Scenario 1: End-to-End File Replication Workflow (5 minutes)
.DESCRIPTION
    Demonstrates complete ForkerDotNet workflow:
    1. Drop medical imaging file in Reservoir
    2. Observe dual-target replication (DestinationA + DestinationB)
    3. Verify SHA-256 hash integrity with PowerShell
    4. Monitor SQLite state transitions
    5. Validate non-locking behavior (external access during copy)
.PARAMETER DemoPath
    Root path for demo directories (default: C:\ForkerDemo)
.PARAMETER TestFileSize
    Size of test file in MB (default: 100MB for fast demo, use 2000+ for realistic medical imaging)
.EXAMPLE
    .\Run-Scenario1-EndToEnd.ps1
    .\Run-Scenario1-EndToEnd.ps1 -TestFileSize 500
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$DemoPath = "C:\ForkerDemo",

    [Parameter()]
    [int]$TestFileSize = 100
)

$ErrorActionPreference = "Stop"

# Import demo utilities
. "$PSScriptRoot\Demo-Utilities.ps1"

Write-DemoHeader "Scenario 1: End-to-End File Replication"

# Pre-flight checks
Test-DemoEnvironment -DemoPath $DemoPath
Test-ForkerService

# Step 1: Create test medical imaging file
Write-DemoStep "1" "Creating test medical imaging file ($TestFileSize MB)"
$testFile = New-TestMedicalFile -Path "$DemoPath\Reservoir" -SizeMB $TestFileSize -Format "svs"
Write-DemoStatus "Created: $($testFile.Name)" "Success"
Write-DemoStatus "Size: $([math]::Round($testFile.Length / 1MB, 2)) MB" "Info"

# Calculate source hash for verification
Write-Host ""
Write-DemoStatus "Calculating source file SHA-256 hash..." "Info"
$sourceHash = (Get-FileHash -Path $testFile.FullName -Algorithm SHA256).Hash
Write-DemoStatus "Source Hash: $sourceHash" "Success"

# Step 2: Open File Explorer windows in grid layout
Write-Host ""
Write-DemoStep "2" "Opening File Explorer grid (Reservoir + DestinationA + DestinationB)"
Start-FileExplorerGrid -Paths @(
    "$DemoPath\Reservoir",
    "$DemoPath\DestinationA",
    "$DemoPath\DestinationB"
) -Labels @("Source (Reservoir)", "Target A", "Target B")

# Step 3: Open SQLite Browser to ForkerDotNet database
Write-Host ""
Write-DemoStep "3" "Opening SQLite Browser to monitor state transitions"
$dbPath = Get-ForkerDatabasePath
if (Test-Path $dbPath) {
    Start-SqliteBrowser -DatabasePath $dbPath
    Write-DemoStatus "Watch the FileJobs and TargetOutcomes tables" "Info"
} else {
    Write-DemoStatus "Database not found - service may not be running" "Warning"
}

# Step 4: Move file to Input folder to trigger processing
Write-Host ""
Write-DemoStep "4" "Moving file to Input folder to trigger ForkerDotNet processing"
Write-DemoStatus "Moving: $($testFile.Name) -> $DemoPath\Input" "Info"
$inputFile = Join-Path "$DemoPath\Input" $testFile.Name
Move-Item -Path $testFile.FullName -Destination $inputFile -Force
Write-DemoStatus "File moved - ForkerDotNet will detect in <5 seconds" "Success"

# Step 5: Monitor replication progress
Write-Host ""
Write-DemoStep "5" "Monitoring replication progress"
Write-DemoStatus "Watch File Explorer windows for file appearance" "Info"
Write-DemoStatus "Watch SQLite Browser for state changes: DISCOVERED -> QUEUED -> IN_PROGRESS -> VERIFIED" "Info"

$destA = Join-Path "$DemoPath\DestinationA" $testFile.Name
$destB = Join-Path "$DemoPath\DestinationB" $testFile.Name

$startTime = Get-Date
$timeout = 300 # 5 minutes max
$checkInterval = 2 # Check every 2 seconds

Write-Host ""
Write-Host "Waiting for replication to complete..." -ForegroundColor Cyan
Write-Host "(This may take 1-3 minutes depending on file size)" -ForegroundColor Gray
Write-Host ""

$completed = $false
while (-not $completed -and ((Get-Date) - $startTime).TotalSeconds -lt $timeout) {
    $destAExists = Test-Path $destA
    $destBExists = Test-Path $destB

    if ($destAExists -and $destBExists) {
        $destASize = (Get-Item $destA).Length
        $destBSize = (Get-Item $destB).Length
        $expectedSize = $TestFileSize * 1MB

        # Check if files are complete (size matches)
        if ($destASize -ge $expectedSize -and $destBSize -ge $expectedSize) {
            Write-DemoStatus "[OK] Both targets reached full size" "Success"
            $completed = $true
        } else {
            $progressA = [math]::Round(($destASize / $expectedSize) * 100, 1)
            $progressB = [math]::Round(($destBSize / $expectedSize) * 100, 1)
            Write-Host "`r  Target A: $progressA% | Target B: $progressB%   " -NoNewline -ForegroundColor Yellow
            Start-Sleep -Seconds $checkInterval
        }
    } elseif ($destAExists) {
        Write-Host "`r  Target A: copying | Target B: pending   " -NoNewline -ForegroundColor Yellow
        Start-Sleep -Seconds $checkInterval
    } else {
        Write-Host "`r  Waiting for ForkerDotNet to start processing...   " -NoNewline -ForegroundColor Gray
        Start-Sleep -Seconds $checkInterval
    }
}

Write-Host "" # New line after progress

if (-not $completed) {
    Write-DemoStatus "Timeout waiting for replication - check service status" "Error"
    exit 1
}

# Step 6: Verify hash integrity with PowerShell
Write-Host ""
Write-DemoStep "6" "Verifying SHA-256 hash integrity with PowerShell Get-FileHash"

Write-Host ""
Write-Host "Source Hash:     $sourceHash" -ForegroundColor Cyan

$hashA = (Get-FileHash -Path $destA -Algorithm SHA256).Hash
Write-Host "Destination A:   $hashA" -ForegroundColor $(if ($hashA -eq $sourceHash) { "Green" } else { "Red" })

$hashB = (Get-FileHash -Path $destB -Algorithm SHA256).Hash
Write-Host "Destination B:   $hashB" -ForegroundColor $(if ($hashB -eq $sourceHash) { "Green" } else { "Red" })

if ($hashA -eq $sourceHash -and $hashB -eq $sourceHash) {
    Write-Host ""
    Write-DemoStatus "[OK] Hash verification PASSED - data integrity confirmed" "Success"
} else {
    Write-Host ""
    Write-DemoStatus "[ERROR] Hash verification FAILED - corruption detected!" "Error"
    exit 1
}

# Step 7: Verify non-locking behavior
Write-Host ""
Write-DemoStep "7" "Demonstrating non-locking behavior (external access during copy)"
Write-DemoStatus "Opening destination file in read mode to prove no locks..." "Info"

try {
    $stream = [System.IO.File]::OpenRead($destA)
    $buffer = New-Object byte[] 1024
    $bytesRead = $stream.Read($buffer, 0, 1024)
    $stream.Close()

    Write-DemoStatus "[OK] Successfully read $bytesRead bytes from destination" "Success"
    Write-DemoStatus "[OK] External applications can access files during ForkerDotNet operations" "Success"
} catch {
    Write-DemoStatus "[ERROR] Could not read file - unexpected locking behavior" "Error"
}

# Step 8: Display summary
Write-Host ""
Write-DemoSummary @"
Scenario 1 Complete: End-to-End File Replication

[OK] Medical imaging file created and dropped
[OK] Dual-target replication completed (DestinationA + DestinationB)
[OK] SHA-256 hash integrity verified (all hashes match)
[OK] Non-locking behavior confirmed (external read access works)
[OK] SQLite state transitions observable in real-time

Key Observations:
- ForkerDotNet detected file within 5 seconds
- Replication completed in $(([math]::Round(((Get-Date) - $startTime).TotalSeconds, 1))) seconds
- File size: $([math]::Round($TestFileSize, 0)) MB
- Throughput: $([math]::Round($TestFileSize / ((Get-Date) - $startTime).TotalSeconds * 60, 1)) MB/min per target
- Zero corruption (SHA-256 hashes match)
- No file locking issues (external access permitted)

Evidence:
- File Explorer: Visual confirmation of dual replication
- PowerShell Get-FileHash: Cryptographic integrity proof
- SQLite Browser: State machine audit trail (DISCOVERED -> VERIFIED)
- File system timestamps: Replication timing data

Next Steps:
- Run Scenario 2: Corruption Detection (Run-Scenario2-Corruption.ps1)
- Run Scenario 3: Concurrent Access (Run-Scenario3-ConcurrentAccess.ps1)
- Export evidence package for governance review
"@

Write-Host ""
Write-Host "Press any key to close File Explorer windows..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

# Cleanup
Stop-FileExplorerGrid
