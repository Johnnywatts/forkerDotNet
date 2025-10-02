# Demo-Utilities.ps1
# Shared utility functions for ForkerDotNet demonstration scripts

#Requires -Version 5.1

# Color-coded output functions
function Write-DemoHeader {
    param([string]$Title)

    $border = "=" * 80
    Write-Host ""
    Write-Host $border -ForegroundColor Green
    Write-Host "  $Title" -ForegroundColor Green
    Write-Host $border -ForegroundColor Green
    Write-Host ""
}

function Write-DemoStep {
    param([string]$Number, [string]$Description)

    Write-Host ""
    Write-Host "STEP ${Number}: $Description" -ForegroundColor Cyan
    Write-Host ("-" * 80) -ForegroundColor DarkGray
}

function Write-DemoStatus {
    param([string]$Message, [string]$Type = "Info")

    $prefix = switch ($Type) {
        "Success" { "[OK]"; $color = "Green" }
        "Warning" { "[WARN]"; $color = "Yellow" }
        "Error"   { "[ERROR]"; $color = "Red" }
        "Info"    { "[INFO]"; $color = "Cyan" }
        default   { "[*]"; $color = "White" }
    }

    Write-Host "  $prefix $Message" -ForegroundColor $color
}

function Write-DemoSummary {
    param([string]$Summary)

    $border = "=" * 80
    Write-Host ""
    Write-Host $border -ForegroundColor Green
    Write-Host $Summary -ForegroundColor White
    Write-Host $border -ForegroundColor Green
}

# Environment validation functions
function Test-DemoEnvironment {
    param([string]$DemoPath)

    Write-DemoStatus "Checking demo environment..." "Info"

    $requiredDirs = @(
        "$DemoPath\Reservoir",
        "$DemoPath\Input",
        "$DemoPath\DestinationA",
        "$DemoPath\DestinationB",
        "$DemoPath\Archive",
        "$DemoPath\Quarantine"
    )

    $allExist = $true
    foreach ($dir in $requiredDirs) {
        if (!(Test-Path $dir)) {
            Write-DemoStatus "Missing directory: $dir" "Error"
            $allExist = $false
        }
    }

    if (-not $allExist) {
        Write-Host ""
        Write-DemoStatus "Run Demo-Setup.ps1 first to create demo environment" "Error"
        throw "Demo environment not configured"
    }

    Write-DemoStatus "Demo environment validated" "Success"
}

function Test-ForkerService {
    Write-DemoStatus "Checking ForkerDotNet service status..." "Info"

    # Check if service is running (Windows Service or console)
    $service = Get-Service -Name "ForkerDotNet" -ErrorAction SilentlyContinue

    if ($service -and $service.Status -eq "Running") {
        Write-DemoStatus "ForkerDotNet Windows Service is running" "Success"
        return $true
    }

    # Check if running as console app
    $process = Get-Process -Name "Forker.Service" -ErrorAction SilentlyContinue
    if ($process) {
        Write-DemoStatus "ForkerDotNet console process detected (PID: $($process.Id))" "Success"
        return $true
    }

    Write-DemoStatus "ForkerDotNet service not running" "Warning"
    Write-Host ""
    Write-Host "Start ForkerDotNet with one of these methods:" -ForegroundColor Yellow
    Write-Host "  1. Windows Service: Start-Service ForkerDotNet" -ForegroundColor Gray
    Write-Host "  2. Console: dotnet run --project src\Forker.Service" -ForegroundColor Gray
    Write-Host ""

    $response = Read-Host "Continue anyway? (y/N)"
    if ($response -ne "y") {
        throw "ForkerDotNet service not running"
    }

    return $false
}

# File generation functions
function New-TestMedicalFile {
    param(
        [string]$Path,
        [int]$SizeMB,
        [string]$Format = "svs"
    )

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $fileName = "TestSlide-$timestamp.$Format"
    $filePath = Join-Path $Path $fileName

    Write-DemoStatus "Generating $SizeMB MB test file..." "Info"

    # Create file with random data (simulates medical imaging data)
    $bufferSize = 1MB
    $buffer = New-Object byte[] $bufferSize
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()

    $stream = [System.IO.File]::Create($filePath)
    try {
        for ($i = 0; $i -lt $SizeMB; $i++) {
            $rng.GetBytes($buffer)
            $stream.Write($buffer, 0, $bufferSize)

            if (($i % 10) -eq 0) {
                $progress = [math]::Round(($i / $SizeMB) * 100, 0)
                Write-Progress -Activity "Generating test file" -Status "$progress% complete" -PercentComplete $progress
            }
        }
    } finally {
        $stream.Close()
        $rng.Dispose()
        Write-Progress -Activity "Generating test file" -Completed
    }

    return Get-Item $filePath
}

function New-CorruptedFile {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    # Copy file and corrupt a random byte in the middle
    Copy-Item -Path $SourcePath -Destination $DestinationPath -Force

    $fileSize = (Get-Item $DestinationPath).Length
    $corruptPosition = [math]::Floor($fileSize / 2)

    $stream = [System.IO.File]::Open($DestinationPath, [System.IO.FileMode]::Open)
    try {
        $stream.Seek($corruptPosition, [System.IO.SeekOrigin]::Begin) | Out-Null
        $corruptByte = [byte](Get-Random -Minimum 0 -Maximum 256)
        $stream.WriteByte($corruptByte)
    } finally {
        $stream.Close()
    }

    Write-DemoStatus "Corrupted 1 byte at position $corruptPosition" "Info"
}

# UI functions
$script:FileExplorerProcesses = @()

function Start-FileExplorerGrid {
    param(
        [string[]]$Paths,
        [string[]]$Labels
    )

    $screenWidth = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width
    $screenHeight = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Height

    $windowWidth = [math]::Floor($screenWidth / $Paths.Count)
    $windowHeight = [math]::Floor($screenHeight / 2)

    for ($i = 0; $i -lt $Paths.Count; $i++) {
        $path = $Paths[$i]
        $label = if ($Labels -and $i -lt $Labels.Count) { $Labels[$i] } else { "Window $($i+1)" }

        if (Test-Path $path) {
            Write-DemoStatus "Opening: $label ($path)" "Info"

            # Launch File Explorer
            $process = Start-Process -FilePath "explorer.exe" -ArgumentList $path -PassThru
            $script:FileExplorerProcesses += $process

            # Wait for window to appear
            Start-Sleep -Milliseconds 500

            # Position window (requires Windows API - optional, may not work in all environments)
            # This is best-effort positioning
        } else {
            Write-DemoStatus "Path not found: $path" "Warning"
        }
    }

    Write-DemoStatus "Opened $($Paths.Count) File Explorer windows" "Success"
}

function Stop-FileExplorerGrid {
    foreach ($process in $script:FileExplorerProcesses) {
        if (-not $process.HasExited) {
            $process.CloseMainWindow() | Out-Null
            Start-Sleep -Milliseconds 200
            if (-not $process.HasExited) {
                $process.Kill()
            }
        }
    }

    $script:FileExplorerProcesses = @()
}

function Start-SqliteBrowser {
    param([string]$DatabasePath)

    # Try to find SQLite Browser in common locations
    $sqliteBrowserPaths = @(
        "C:\Program Files\DB Browser for SQLite\DB Browser for SQLite.exe",
        "C:\Program Files (x86)\DB Browser for SQLite\DB Browser for SQLite.exe",
        "$env:LOCALAPPDATA\Programs\DB Browser for SQLite\DB Browser for SQLite.exe"
    )

    $sqliteBrowser = $null
    foreach ($path in $sqliteBrowserPaths) {
        if (Test-Path $path) {
            $sqliteBrowser = $path
            break
        }
    }

    if ($sqliteBrowser) {
        Start-Process -FilePath $sqliteBrowser -ArgumentList $DatabasePath
        Write-DemoStatus "Opened SQLite Browser" "Success"
    } else {
        Write-DemoStatus "SQLite Browser not found - download from https://sqlitebrowser.org/" "Warning"
        Write-Host ""
        Write-Host "Alternative: Use PowerShell to query database:" -ForegroundColor Yellow
        Write-Host "  Install-Module -Name PSSQLite" -ForegroundColor Gray
        Write-Host "  Invoke-SqliteQuery -Database '$DatabasePath' -Query 'SELECT * FROM FileJobs'" -ForegroundColor Gray
    }
}

function Get-ForkerDatabasePath {
    # Check common database locations
    $possiblePaths = @(
        "C:\ProgramData\ForkerDotNet\forker.db",
        "$env:APPDATA\ForkerDotNet\forker.db",
        ".\forker.db"
    )

    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            return $path
        }
    }

    # If not found, return default path
    return "C:\ProgramData\ForkerDotNet\forker.db"
}

# Service control functions
function Stop-ForkerService {
    param([switch]$Force)

    $service = Get-Service -Name "ForkerDotNet" -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq "Running") {
        Write-DemoStatus "Stopping ForkerDotNet Windows Service..." "Info"
        Stop-Service -Name "ForkerDotNet" -Force:$Force
        Start-Sleep -Seconds 2
        Write-DemoStatus "Service stopped" "Success"
        return
    }

    $process = Get-Process -Name "Forker.Service" -ErrorAction SilentlyContinue
    if ($process) {
        Write-DemoStatus "Stopping ForkerDotNet console process (PID: $($process.Id))..." "Info"
        if ($Force) {
            $process | Stop-Process -Force
        } else {
            $process | Stop-Process
        }
        Start-Sleep -Seconds 2
        Write-DemoStatus "Process stopped" "Success"
        return
    }

    Write-DemoStatus "ForkerDotNet is not running" "Info"
}

function Start-ForkerService {
    $service = Get-Service -Name "ForkerDotNet" -ErrorAction SilentlyContinue
    if ($service) {
        Write-DemoStatus "Starting ForkerDotNet Windows Service..." "Info"
        Start-Service -Name "ForkerDotNet"
        Start-Sleep -Seconds 3
        Write-DemoStatus "Service started" "Success"
        return
    }

    Write-DemoStatus "ForkerDotNet Windows Service not installed" "Warning"
    Write-Host "Run console version: dotnet run --project src\Forker.Service" -ForegroundColor Yellow
}

function Restart-ForkerService {
    Stop-ForkerService -Force
    Start-Sleep -Seconds 1
    Start-ForkerService
}

# Evidence collection functions
function Export-DemoEvidence {
    param(
        [string]$ScenarioName,
        [string]$OutputPath
    )

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $evidenceFolder = Join-Path $OutputPath "Evidence-$ScenarioName-$timestamp"

    New-Item -Path $evidenceFolder -ItemType Directory -Force | Out-Null

    Write-DemoStatus "Exporting evidence to: $evidenceFolder" "Info"

    # Export database snapshot
    $dbPath = Get-ForkerDatabasePath
    if (Test-Path $dbPath) {
        Copy-Item -Path $dbPath -Destination (Join-Path $evidenceFolder "forker-snapshot.db")
        Write-DemoStatus "Exported database snapshot" "Success"
    }

    # Export log files
    $logPath = "C:\ProgramData\ForkerDotNet\logs"
    if (Test-Path $logPath) {
        Copy-Item -Path "$logPath\*.log" -Destination $evidenceFolder -ErrorAction SilentlyContinue
        Write-DemoStatus "Exported log files" "Success"
    }

    # Create evidence summary
    $summary = @"
ForkerDotNet Evidence Package
Scenario: $ScenarioName
Timestamp: $timestamp

Files in this package:
- forker-snapshot.db: SQLite database snapshot (FileJobs + TargetOutcomes tables)
- *.log: Service log files with state transitions
- README.txt: This file

Verification Steps:
1. Open forker-snapshot.db with DB Browser for SQLite
2. Review FileJobs table for end-to-end state transitions
3. Review TargetOutcomes table for per-target copy/verify status
4. Review log files for detailed operation timeline

Governance Checklist:
☐ Data integrity verified (SHA-256 hashes match)
☐ Dual-target replication confirmed (both targets copied)
☐ State transitions logged (DISCOVERED → VERIFIED)
☐ No file locking issues (external access permitted)
☐ Crash recovery tested (if applicable)
☐ Performance acceptable (throughput > 1 GB/min per target)
"@

    $summary | Out-File -FilePath (Join-Path $evidenceFolder "README.txt") -Encoding UTF8
    Write-DemoStatus "Created evidence summary" "Success"

    Write-Host ""
    Write-DemoStatus "Evidence package ready: $evidenceFolder" "Success"

    return $evidenceFolder
}

# Add Windows Forms assembly for screen dimensions
Add-Type -AssemblyName System.Windows.Forms

Write-Verbose "Demo-Utilities.ps1 loaded successfully"
