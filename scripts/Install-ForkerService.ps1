#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs or uninstalls ForkerDotNet as a Windows Service using config-driven metadata.

.DESCRIPTION
    This script reads service metadata (name, display name, description) from appsettings files
    and installs ForkerDotNet as a Windows Service. All service properties are driven by the
    configuration file, ensuring consistency between installation and runtime.

.PARAMETER Environment
    Environment name (e.g., "Demo", "SlowDrive").
    Reads from appsettings.{Environment}.json for service metadata.
    If not specified, uses appsettings.json (production defaults).

.PARAMETER ServicePath
    Path to the ForkerDotNet executable. Defaults to the Release build output.

.PARAMETER Uninstall
    Uninstall the service instead of installing.
    Reads service name from config file to ensure correct service is removed.

.EXAMPLE
    .\Install-ForkerService.ps1
    Installs service using production settings (appsettings.json).

.EXAMPLE
    .\Install-ForkerService.ps1 -Environment Demo
    Installs demo service using settings from appsettings.Demo.json.
    Service name, display name, and description all come from the config file.

.EXAMPLE
    .\Install-ForkerService.ps1 -Environment Demo -Uninstall
    Uninstalls the demo service (reads service name from appsettings.Demo.json).

.NOTES
    All service metadata (ServiceName, ServiceDisplayName, ServiceDescription) must be
    defined in the appsettings file. This ensures config is the single source of truth.
#>

param(
    [string]$Environment = "",
    [string]$ServicePath = (Join-Path $PSScriptRoot "..\src\Forker.Service\bin\Release\net8.0\Forker.Service.exe"),
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

# Determine config file path
$serviceDir = Join-Path $PSScriptRoot "..\src\Forker.Service"
$configFileName = if ($Environment) { "appsettings.$Environment.json" } else { "appsettings.json" }
$configPath = Join-Path $serviceDir $configFileName

# Verify config file exists
if (-not (Test-Path $configPath)) {
    Write-Status "Configuration file not found: $configPath" "Error"
    exit 1
}

Write-Status "Reading configuration from: $configFileName" "Info"

# Read and parse config file
try {
    $configContent = Get-Content $configPath -Raw
    $config = $configContent | ConvertFrom-Json
} catch {
    Write-Status "Failed to parse configuration file: $($_.Exception.Message)" "Error"
    exit 1
}

# Extract service metadata from config
$ServiceName = $config.ServiceName
$DisplayName = $config.ServiceDisplayName
$Description = $config.ServiceDescription

# Validate required fields
if (-not $ServiceName) {
    Write-Status "ServiceName not found in $configFileName" "Error"
    Write-Host "Please add 'ServiceName' field to the configuration file" -ForegroundColor Yellow
    exit 1
}

# Use defaults if optional fields missing
if (-not $DisplayName) {
    $DisplayName = "$ServiceName Service"
    Write-Status "ServiceDisplayName not in config, using default: $DisplayName" "Warning"
}

if (-not $Description) {
    $Description = "ForkerDotNet file processing service"
    Write-Status "ServiceDescription not in config, using default: $Description" "Warning"
}

Write-Host ""
Write-Host "Service Configuration (from $configFileName):" -ForegroundColor Green
Write-Host "  Name: $ServiceName"
Write-Host "  Display Name: $DisplayName"
Write-Host "  Description: $Description"
if ($Environment) {
    Write-Host "  Environment: $Environment"
}
Write-Host ""

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
}

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
if ($Environment) {
    Write-Host "  Uninstall: .\Install-ForkerService.ps1 -Environment $Environment -Uninstall" -ForegroundColor Gray
} else {
    Write-Host "  Uninstall: .\Install-ForkerService.ps1 -Uninstall" -ForegroundColor Gray
}
Write-Host ""

Write-Status "Installation complete!" "Success"
