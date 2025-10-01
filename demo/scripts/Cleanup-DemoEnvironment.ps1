#Requires -Version 5.1

<#
.SYNOPSIS
    Cleanup ForkerDotNet demo environment
.DESCRIPTION
    Removes test files and resets demo directories for next demonstration
.PARAMETER DemoPath
    Root path for demo directories (default: C:\ForkerDemo)
.PARAMETER ResetDatabase
    Also reset SQLite database (removes all job history)
.PARAMETER KeepEvidence
    Keep evidence exports for governance review
.EXAMPLE
    .\Cleanup-DemoEnvironment.ps1
    .\Cleanup-DemoEnvironment.ps1 -ResetDatabase
    .\Cleanup-DemoEnvironment.ps1 -KeepEvidence
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$DemoPath = "C:\ForkerDemo",

    [Parameter()]
    [switch]$ResetDatabase,

    [Parameter()]
    [switch]$KeepEvidence
)

$ErrorActionPreference = "Stop"

# Import demo utilities
. "$PSScriptRoot\Demo-Utilities.ps1"

Write-DemoHeader "ForkerDotNet Demo Environment Cleanup"

# Step 1: Confirm cleanup
Write-Host ""
Write-Host "This will remove all test files from:" -ForegroundColor Yellow
Write-Host "  - $DemoPath\Reservoir" -ForegroundColor Gray
Write-Host "  - $DemoPath\Input" -ForegroundColor Gray
Write-Host "  - $DemoPath\DestinationA" -ForegroundColor Gray
Write-Host "  - $DemoPath\DestinationB" -ForegroundColor Gray
Write-Host "  - $DemoPath\Archive" -ForegroundColor Gray
Write-Host "  - $DemoPath\Quarantine" -ForegroundColor Gray

if ($ResetDatabase) {
    Write-Host "  - SQLite database (all job history)" -ForegroundColor Yellow
}

Write-Host ""
$response = Read-Host "Continue? (y/N)"
if ($response -ne "y") {
    Write-Host "Cleanup cancelled" -ForegroundColor Yellow
    exit 0
}

# Step 2: Clean directories
Write-Host ""
Write-DemoStep "1" "Cleaning demo directories"

$directories = @(
    "$DemoPath\Reservoir",
    "$DemoPath\Input",
    "$DemoPath\DestinationA",
    "$DemoPath\DestinationB",
    "$DemoPath\Archive",
    "$DemoPath\Quarantine"
)

foreach ($dir in $directories) {
    if (Test-Path $dir) {
        $files = Get-ChildItem -Path $dir -File -Filter "*.svs", "*.tiff", "*.ndpi", "*.scn"
        $fileCount = $files.Count

        if ($fileCount -gt 0) {
            Remove-Item -Path $files.FullName -Force
            Write-DemoStatus "Removed $fileCount file(s) from $(Split-Path $dir -Leaf)" "Success"
        } else {
            Write-DemoStatus "No files to remove from $(Split-Path $dir -Leaf)" "Info"
        }
    } else {
        Write-DemoStatus "Directory not found: $dir" "Warning"
    }
}

# Step 3: Clean logs (optional)
Write-Host ""
Write-DemoStep "2" "Cleaning log files"

$logPath = "$DemoPath\Logs"
if (Test-Path $logPath) {
    $oldLogs = Get-ChildItem -Path $logPath -Filter "*.log" | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-7) }

    if ($oldLogs.Count -gt 0) {
        Remove-Item -Path $oldLogs.FullName -Force
        Write-DemoStatus "Removed $($oldLogs.Count) old log file(s)" "Success"
    } else {
        Write-DemoStatus "No old log files to remove" "Info"
    }
} else {
    Write-DemoStatus "Log directory not found" "Info"
}

# Step 4: Clean evidence exports (optional)
if (-not $KeepEvidence) {
    Write-Host ""
    Write-DemoStep "3" "Cleaning evidence exports"

    $evidenceDirs = Get-ChildItem -Path $DemoPath -Directory -Filter "Evidence-*"

    if ($evidenceDirs.Count -gt 0) {
        foreach ($evidenceDir in $evidenceDirs) {
            Remove-Item -Path $evidenceDir.FullName -Recurse -Force
        }
        Write-DemoStatus "Removed $($evidenceDirs.Count) evidence export(s)" "Success"
    } else {
        Write-DemoStatus "No evidence exports to remove" "Info"
    }
} else {
    Write-DemoStatus "Keeping evidence exports" "Info"
}

# Step 5: Reset database (optional)
if ($ResetDatabase) {
    Write-Host ""
    Write-DemoStep "4" "Resetting SQLite database"

    $dbPath = Get-ForkerDatabasePath

    if (Test-Path $dbPath) {
        # Stop ForkerDotNet service before resetting database
        $serviceRunning = $false
        $service = Get-Service -Name "ForkerDotNet" -ErrorAction SilentlyContinue
        if ($service -and $service.Status -eq "Running") {
            $serviceRunning = $true
            Write-DemoStatus "Stopping ForkerDotNet service..." "Info"
            Stop-Service -Name "ForkerDotNet"
            Start-Sleep -Seconds 2
        }

        # Backup database
        $backupPath = "$dbPath.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Copy-Item -Path $dbPath -Destination $backupPath
        Write-DemoStatus "Database backed up to: $backupPath" "Success"

        # Remove database
        Remove-Item -Path $dbPath -Force
        Write-DemoStatus "Database removed (will be recreated on next service start)" "Success"

        # Remove WAL and SHM files
        if (Test-Path "$dbPath-wal") {
            Remove-Item -Path "$dbPath-wal" -Force
        }
        if (Test-Path "$dbPath-shm") {
            Remove-Item -Path "$dbPath-shm" -Force
        }

        # Restart service if it was running
        if ($serviceRunning) {
            Write-DemoStatus "Restarting ForkerDotNet service..." "Info"
            Start-Service -Name "ForkerDotNet"
            Start-Sleep -Seconds 3
            Write-DemoStatus "Service restarted with fresh database" "Success"
        }
    } else {
        Write-DemoStatus "Database not found" "Info"
    }
}

# Step 6: Display summary
Write-Host ""
Write-DemoSummary @"
Demo Environment Cleanup Complete

✓ Test files removed from all demo directories
✓ Old log files cleaned
$(if (-not $KeepEvidence) { "✓ Evidence exports removed" } else { "• Evidence exports preserved" })
$(if ($ResetDatabase) { "✓ Database reset (backup created)" } else { "• Database preserved (use -ResetDatabase to reset)" })

Demo environment is ready for next demonstration!

Next Steps:
1. Run Demo-Setup.ps1 if directories were removed
2. Run any scenario script to test:
   - Run-Scenario1-EndToEnd.ps1
   - Run-Scenario2-Corruption.ps1
   - Run-Scenario3-ConcurrentAccess.ps1
   - Run-Scenario4-CrashRecovery.ps1
   - Run-Scenario5-StabilityDetection.ps1
"@
