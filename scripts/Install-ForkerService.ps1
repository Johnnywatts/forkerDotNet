#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs or uninstalls ForkerDotNet as a Windows Service.

.DESCRIPTION
    This script installs ForkerDotNet as a Windows Service using sc.exe.
    It configures the service for automatic startup and crash recovery.
    Can also uninstall the service with the -Uninstall flag.

.PARAMETER ServicePath
    Path to the ForkerDotNet executable. Defaults to the Release build output.

.PARAMETER ServiceName
    Name of the Windows Service. Defaults to "ForkerDotNet".

.PARAMETER DisplayName
    Display name for the Windows Service. Defaults to "ForkerDotNet File Copier Service".

.PARAMETER Description
    Service description.

.PARAMETER Environment
    Environment name (e.g., "Demo", "Production"). Sets DOTNET_ENVIRONMENT for appsettings loading.

.PARAMETER Uninstall
    Uninstall the service instead of installing.

.EXAMPLE
    .\Install-ForkerService.ps1
    Installs the service using default settings.

.EXAMPLE
    .\Install-ForkerService.ps1 -Environment Demo
    Installs service for demo environment (loads appsettings.Demo.json).

.EXAMPLE
    .\Install-ForkerService.ps1 -Uninstall
    Uninstalls the service.

.EXAMPLE
    .\Install-ForkerService.ps1 -ServiceName "ForkerDotNetDemo" -Uninstall
    Uninstalls the demo service.
#>

param(
    [string]$ServicePath = (Join-Path $PSScriptRoot "..\src\Forker.Service\bin\Release\net8.0\Forker.Service.exe"),
    [string]$ServiceName = "ForkerDotNet",
    [string]$DisplayName = "ForkerDotNet File Copier Service",
    [string]$Description = "Production-grade file copier for medical imaging files with dual-target replication and SHA-256 verification",
    [string]$Environment = "",
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

# Helper function for status messages
function Write-Status {
    param($Message, $Type = "Info")

    switch ($Type) {
        "Success" { Write-Host "✓ $Message" -ForegroundColor Green }
        "Warning" { Write-Host "⚠ $Message" -ForegroundColor Yellow }
        "Error"   { Write-Host "✗ $Message" -ForegroundColor Red }
        "Info"    { Write-Host "ℹ $Message" -ForegroundColor Cyan }
        default   { Write-Host "$Message" }
    }
}

Write-Host "ForkerDotNet Service Management" -ForegroundColor Cyan
Write-Host "===============================" -ForegroundColor Cyan
Write-Host ""

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Status "This script must be run as Administrator" "Error"
    exit 1
}

# UNINSTALL MODE
if ($Uninstall) {
    Write-Status "Uninstalling service: $ServiceName" "Info"
    Write-Host ""

    try {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($service) {
            if ($service.Status -eq "Running") {
                Write-Status "Stopping service..." "Info"
                Stop-Service -Name $ServiceName -Force
                Start-Sleep 3
            }

            Write-Status "Removing service..." "Info"
            & sc.exe delete $ServiceName

            if ($LASTEXITCODE -eq 0) {
                Write-Status "Service uninstalled successfully" "Success"
            } else {
                Write-Status "Service removal failed" "Error"
                exit 1
            }
        } else {
            Write-Status "Service not found: $ServiceName" "Warning"
        }
    } catch {
        Write-Status "Uninstall failed: $($_.Exception.Message)" "Error"
        exit 1
    }

    exit 0
}

# INSTALL MODE
Write-Status "Installing service: $ServiceName" "Info"
Write-Host ""

# Check if service executable exists
if (-not (Test-Path $ServicePath)) {
    Write-Status "Service executable not found: $ServicePath" "Error"
    Write-Host ""
    Write-Host "Please build the service first using:" -ForegroundColor Yellow
    Write-Host "  dotnet build src\Forker.Service -c Release" -ForegroundColor Yellow
    exit 1
}

Write-Status "Found service executable: $ServicePath" "Success"

# Build service arguments with environment variable if specified
$binPath = "`"$ServicePath`""
if ($Environment) {
    $binPath = "`"$ServicePath`" --environment $Environment"
    Write-Status "Environment: $Environment (will load appsettings.$Environment.json)" "Info"
}

Write-Host ""
Write-Host "Service Configuration:" -ForegroundColor Green
Write-Host "  Name: $ServiceName"
Write-Host "  Display Name: $DisplayName"
Write-Host "  Executable: $ServicePath"
if ($Environment) {
    Write-Host "  Environment: $Environment"
}
Write-Host ""

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Status "Service already exists: $ServiceName" "Warning"

    $choice = Read-Host "Do you want to reinstall? (y/N)"
    if ($choice -eq "y" -or $choice -eq "Y") {
        Write-Status "Removing existing service..." "Info"

        if ($existingService.Status -eq "Running") {
            Stop-Service -Name $ServiceName -Force
            Start-Sleep 3
        }

        & sc.exe delete $ServiceName
        Start-Sleep 2
    } else {
        Write-Status "Installation cancelled" "Info"
        exit 0
    }
}

try {
    # Install the service
    Write-Status "Creating Windows service..." "Info"

    $createResult = & sc.exe create $ServiceName `
        binPath= $binPath `
        DisplayName= $DisplayName `
        start= auto

    if ($LASTEXITCODE -ne 0) {
        Write-Status "Service creation failed" "Error"
        exit 1
    }

    # Set service description
    & sc.exe description $ServiceName $Description | Out-Null

    # Configure service recovery actions
    Write-Status "Configuring automatic restart on failure..." "Info"
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

    Write-Status "Service installed successfully" "Success"
    Write-Host ""
    Write-Host "Recovery Actions Configured:" -ForegroundColor Green
    Write-Host "  - First failure: Restart after 5 seconds"
    Write-Host "  - Second failure: Restart after 10 seconds"
    Write-Host "  - Subsequent failures: Restart after 30 seconds"
    Write-Host "  - Reset failure counter after 24 hours"
    Write-Host ""

    # Ask if user wants to start the service now
    $startNow = Read-Host "Do you want to start the service now? (y/N)"
    if ($startNow -eq "y" -or $startNow -eq "Y") {
        Write-Status "Starting service..." "Info"

        Start-Service -Name $ServiceName
        Start-Sleep 2

        $service = Get-Service -Name $ServiceName
        if ($service.Status -eq "Running") {
            Write-Status "Service started successfully" "Success"
        } else {
            Write-Status "Service failed to start - check Event Log for details" "Error"
        }
    }

} catch {
    Write-Status "Service installation failed: $($_.Exception.Message)" "Error"
    exit 1
}

# Display service management commands
Write-Host ""
Write-Host "Service Management Commands:" -ForegroundColor Yellow
Write-Host "  Start:     Start-Service -Name $ServiceName" -ForegroundColor Gray
Write-Host "  Stop:      Stop-Service -Name $ServiceName" -ForegroundColor Gray
Write-Host "  Status:    Get-Service -Name $ServiceName" -ForegroundColor Gray
Write-Host "  Uninstall: .\Install-ForkerService.ps1 -ServiceName $ServiceName -Uninstall" -ForegroundColor Gray
Write-Host ""

Write-Status "Installation complete!" "Success"
