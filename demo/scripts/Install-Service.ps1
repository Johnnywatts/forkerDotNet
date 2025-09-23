#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Installs ForkerDotNet as a Windows service for demo purposes
.DESCRIPTION
    Configures and installs the ForkerDotNet service with demo-specific settings
.PARAMETER ServiceName
    Name of the Windows service (default: ForkerDotNetDemo)
.PARAMETER DemoMode
    Configure service for demo environment
.PARAMETER Uninstall
    Uninstall the service instead of installing
.EXAMPLE
    .\Install-Service.ps1
    .\Install-Service.ps1 -Uninstall
    .\Install-Service.ps1 -ServiceName "ForkerDemo" -DemoMode
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$ServiceName = "ForkerDotNetDemo",

    [Parameter()]
    [switch]$DemoMode,

    [Parameter()]
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

# Function to write colored output
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

# Find ForkerDotNet service executable
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$serviceExe = Join-Path $repoRoot "src\Forker.Service\bin\Release\net8.0\Forker.Service.exe"

if ($DemoMode) {
    Write-Host "ForkerDotNet Demo Service Installation" -ForegroundColor Green
    Write-Host "=====================================" -ForegroundColor Green
} else {
    Write-Host "ForkerDotNet Service Installation" -ForegroundColor Green
    Write-Host "=================================" -ForegroundColor Green
}
Write-Host ""

if ($Uninstall) {
    # Uninstall service
    Write-Status "Uninstalling ForkerDotNet service..." "Info"

    try {
        # Stop service if running
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
            }
        } else {
            Write-Status "Service not found: $ServiceName" "Warning"
        }
    } catch {
        Write-Status "Uninstall failed: $($_.Exception.Message)" "Error"
    }

    return
}

# Install service
Write-Status "Installing ForkerDotNet service..." "Info"

# Check if service executable exists
if (!(Test-Path $serviceExe)) {
    Write-Status "Service executable not found: $serviceExe" "Error"
    Write-Status "Please build the ForkerDotNet solution first" "Info"
    Write-Host "Run: dotnet build src\Forker.Service --configuration Release" -ForegroundColor Yellow
    exit 1
}

Write-Status "Found service executable: $serviceExe" "Success"

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
        return
    }
}

try {
    # Create service configuration
    $serviceDisplayName = if ($DemoMode) { "ForkerDotNet Demo Service" } else { "ForkerDotNet File Processing Service" }
    $serviceDescription = if ($DemoMode) {
        "Demo service for ForkerDotNet clinical file processing demonstrations"
    } else {
        "Production file processing service for medical imaging workflows"
    }

    # Install service
    Write-Status "Creating Windows service..." "Info"

    $createResult = & sc.exe create $ServiceName `
        binPath= "`"$serviceExe`"" `
        DisplayName= $serviceDisplayName `
        start= demand `
        type= own

    if ($LASTEXITCODE -ne 0) {
        Write-Status "Service creation failed" "Error"
        return
    }

    # Set service description
    & sc.exe description $ServiceName $serviceDescription

    # Configure service recovery options
    Write-Status "Configuring service recovery options..." "Info"
    & sc.exe failure $ServiceName reset= 60 actions= restart/5000/restart/10000/restart/30000

    # Set service to start automatically (for demo convenience)
    if ($DemoMode) {
        Write-Status "Setting service to manual start for demo..." "Info"
        & sc.exe config $ServiceName start= demand
    }

    Write-Status "Service installed successfully: $ServiceName" "Success"

    # Ask if user wants to start the service now
    Write-Host ""
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
}

# Display service management commands
Write-Host ""
Write-Host "Service Management Commands:" -ForegroundColor Yellow
Write-Host "Start:   sc start $ServiceName" -ForegroundColor Gray
Write-Host "Stop:    sc stop $ServiceName" -ForegroundColor Gray
Write-Host "Status:  sc query $ServiceName" -ForegroundColor Gray
Write-Host "Remove:  sc delete $ServiceName" -ForegroundColor Gray
Write-Host ""

if ($DemoMode) {
    Write-Host "Demo Service Configuration:" -ForegroundColor Yellow
    Write-Host "- Service name: $ServiceName" -ForegroundColor Gray
    Write-Host "- Start type: Manual (for demo control)" -ForegroundColor Gray
    Write-Host "- Recovery: Automatic restart on failure" -ForegroundColor Gray
    Write-Host "- Logs: Check Windows Event Log for service events" -ForegroundColor Gray
    Write-Host ""
}

Write-Status "Service installation complete!" "Success"