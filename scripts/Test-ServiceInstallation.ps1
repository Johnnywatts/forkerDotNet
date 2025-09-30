#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Tests the ForkerDotNet Windows Service installation and functionality.

.DESCRIPTION
    This script performs a complete test of the ForkerDotNet service:
    1. Publishes the service
    2. Installs it as a Windows Service
    3. Starts the service
    4. Monitors it for 30 seconds
    5. Optionally drops a test file to verify end-to-end processing

.EXAMPLE
    .\Test-ServiceInstallation.ps1
    Runs the complete service installation test.
#>

param(
    [switch]$SkipPublish,
    [switch]$SkipTestFile
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "ForkerDotNet Service Installation Test" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Publish the service
if (-not $SkipPublish) {
    Write-Host "[1/5] Publishing service..." -ForegroundColor Yellow
    Push-Location $RepoRoot
    try {
        dotnet publish src\Forker.Service -c Release -r win-x64 --self-contained false -o publish
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to publish service"
            exit 1
        }
        Write-Host "Service published successfully." -ForegroundColor Green
    } finally {
        Pop-Location
    }
} else {
    Write-Host "[1/5] Skipping publish (using existing build)..." -ForegroundColor Yellow
}
Write-Host ""

# Step 2: Install the service
Write-Host "[2/5] Installing Windows Service..." -ForegroundColor Yellow
$publishPath = Join-Path $RepoRoot "publish\Forker.Service.exe"
& "$ScriptDir\Install-ForkerService.ps1" -ServicePath $publishPath
Write-Host ""

# Step 3: Start the service
Write-Host "[3/5] Starting service..." -ForegroundColor Yellow
Start-Service -Name ForkerDotNet
Start-Sleep -Seconds 3

$service = Get-Service -Name ForkerDotNet
if ($service.Status -eq 'Running') {
    Write-Host "Service is running!" -ForegroundColor Green
} else {
    Write-Error "Service failed to start. Status: $($service.Status)"
    exit 1
}
Write-Host ""

# Step 4: Monitor the service
Write-Host "[4/5] Monitoring service for 30 seconds..." -ForegroundColor Yellow
Write-Host "Press Ctrl+C to skip monitoring" -ForegroundColor Gray
Write-Host ""

$startTime = Get-Date
$duration = 30
$checkInterval = 5

for ($i = 0; $i -lt $duration; $i += $checkInterval) {
    $service = Get-Service -Name ForkerDotNet
    $elapsed = [math]::Round(((Get-Date) - $startTime).TotalSeconds, 1)

    Write-Host "[$elapsed s] Service Status: $($service.Status)" -ForegroundColor Cyan

    if ($service.Status -ne 'Running') {
        Write-Error "Service stopped unexpectedly!"

        # Try to get recent error logs
        Write-Host ""
        Write-Host "Recent Application Event Log entries:" -ForegroundColor Yellow
        Get-EventLog -LogName Application -Source ForkerDotNet -Newest 5 -ErrorAction SilentlyContinue |
            Format-Table -AutoSize -Wrap

        exit 1
    }

    if ($i + $checkInterval -lt $duration) {
        Start-Sleep -Seconds $checkInterval
    }
}

Write-Host ""
Write-Host "Service has been running successfully for $duration seconds!" -ForegroundColor Green
Write-Host ""

# Step 5: Test file processing (optional)
if (-not $SkipTestFile) {
    Write-Host "[5/5] Testing file processing..." -ForegroundColor Yellow

    $inputDir = "C:\ForkerDotNet\Input"
    $clinicalDir = "C:\ForkerDotNet\Clinical"
    $researchDir = "C:\ForkerDotNet\Research"

    # Ensure directories exist
    @($inputDir, $clinicalDir, $researchDir) | ForEach-Object {
        if (-not (Test-Path $_)) {
            Write-Warning "Directory not found: $_"
            Write-Host "Directories should be created automatically by the service."
        }
    }

    Write-Host "Creating test file in Input directory..." -ForegroundColor Cyan
    $testFileName = "test-medical-image-$(Get-Date -Format 'yyyyMMdd-HHmmss').svs"
    $testFilePath = Join-Path $inputDir $testFileName

    # Create a 5MB test file
    $testData = New-Object byte[] (5MB)
    (New-Object Random).NextBytes($testData)
    [System.IO.File]::WriteAllBytes($testFilePath, $testData)

    Write-Host "Test file created: $testFileName (5MB)" -ForegroundColor Green
    Write-Host "Waiting 15 seconds for processing..." -ForegroundColor Yellow

    # Monitor for file processing
    $timeout = 15
    $elapsed = 0
    $checkInterval = 2
    $processed = $false

    while ($elapsed -lt $timeout -and -not $processed) {
        Start-Sleep -Seconds $checkInterval
        $elapsed += $checkInterval

        $clinicalExists = Test-Path (Join-Path $clinicalDir $testFileName)
        $researchExists = Test-Path (Join-Path $researchDir $testFileName)
        $inputExists = Test-Path $testFilePath

        Write-Host "[$elapsed s] Clinical: $clinicalExists | Research: $researchExists | Input: $inputExists" -ForegroundColor Cyan

        if ($clinicalExists -and $researchExists -and -not $inputExists) {
            $processed = $true
            Write-Host ""
            Write-Host "SUCCESS! File processed correctly:" -ForegroundColor Green
            Write-Host "  - Copied to Clinical folder: YES"
            Write-Host "  - Copied to Research folder: YES"
            Write-Host "  - Removed from Input folder: YES"
        }
    }

    if (-not $processed) {
        Write-Warning "File processing did not complete within $timeout seconds."
        Write-Host "This might be normal for the stability detection delay."
        Write-Host "Check logs for more details: Get-EventLog -LogName Application -Source ForkerDotNet -Newest 10"
    }
} else {
    Write-Host "[5/5] Skipping test file processing..." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Test Complete!" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Service Commands:" -ForegroundColor Yellow
Write-Host "  Check status: Get-Service -Name ForkerDotNet"
Write-Host "  Stop service: Stop-Service -Name ForkerDotNet"
Write-Host "  Start service: Start-Service -Name ForkerDotNet"
Write-Host "  View logs: Get-EventLog -LogName Application -Source ForkerDotNet -Newest 20"
Write-Host "  Uninstall: .\scripts\Uninstall-ForkerService.ps1"
Write-Host ""
