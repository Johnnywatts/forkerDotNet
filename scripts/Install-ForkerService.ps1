#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs ForkerDotNet as a Windows Service with automatic restart and recovery actions.

.DESCRIPTION
    This script installs ForkerDotNet as a Windows Service using sc.exe.
    It configures the service for automatic startup and crash recovery.

.PARAMETER ServicePath
    Path to the published ForkerDotNet executable. Defaults to the Release build output.

.PARAMETER ServiceName
    Name of the Windows Service. Defaults to "ForkerDotNet".

.PARAMETER DisplayName
    Display name for the Windows Service. Defaults to "ForkerDotNet File Copier Service".

.EXAMPLE
    .\Install-ForkerService.ps1
    Installs the service using default paths and settings.

.EXAMPLE
    .\Install-ForkerService.ps1 -ServicePath "C:\ForkerDotNet\Forker.Service.exe"
    Installs the service using a custom executable path.
#>

param(
    [string]$ServicePath = (Join-Path $PSScriptRoot "..\src\Forker.Service\bin\Release\net8.0\win-x64\publish\Forker.Service.exe"),
    [string]$ServiceName = "ForkerDotNet",
    [string]$DisplayName = "ForkerDotNet File Copier Service",
    [string]$Description = "Production-grade file copier for medical imaging files with dual-target replication and SHA-256 verification"
)

$ErrorActionPreference = "Stop"

Write-Host "ForkerDotNet Service Installation Script" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator. Right-click PowerShell and select 'Run as Administrator'."
    exit 1
}

# Check if service executable exists
if (-not (Test-Path $ServicePath)) {
    Write-Error "Service executable not found at: $ServicePath"
    Write-Host ""
    Write-Host "Please publish the service first using:" -ForegroundColor Yellow
    Write-Host "  dotnet publish src\Forker.Service -c Release -r win-x64 --self-contained false" -ForegroundColor Yellow
    exit 1
}

Write-Host "Service Configuration:" -ForegroundColor Green
Write-Host "  Name: $ServiceName"
Write-Host "  Display Name: $DisplayName"
Write-Host "  Executable: $ServicePath"
Write-Host ""

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service '$ServiceName' already exists. Stopping and removing..." -ForegroundColor Yellow

    if ($existingService.Status -eq 'Running') {
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }

    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
    Write-Host "Existing service removed." -ForegroundColor Green
}

# Install the service
Write-Host "Installing Windows Service..." -ForegroundColor Cyan
$serviceArgs = "create `"$ServiceName`" binPath= `"$ServicePath`" start= auto DisplayName= `"$DisplayName`""
$result = sc.exe create $ServiceName binPath= "$ServicePath" start= auto DisplayName= "$DisplayName"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create service. Error: $result"
    exit 1
}

Write-Host "Service installed successfully." -ForegroundColor Green

# Set service description
sc.exe description $ServiceName "$Description" | Out-Null

# Configure service recovery actions (restart on failure)
Write-Host "Configuring automatic restart on failure..." -ForegroundColor Cyan

# Reset failure counter after 24 hours (86400 seconds)
# Restart service after 5 seconds on first failure
# Restart service after 10 seconds on second failure
# Restart service after 30 seconds on subsequent failures
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

if ($LASTEXITCODE -eq 0) {
    Write-Host "Service recovery actions configured:" -ForegroundColor Green
    Write-Host "  - First failure: Restart after 5 seconds"
    Write-Host "  - Second failure: Restart after 10 seconds"
    Write-Host "  - Subsequent failures: Restart after 30 seconds"
    Write-Host "  - Reset failure counter after 24 hours"
} else {
    Write-Warning "Failed to configure service recovery actions. You may need to configure these manually."
}

# Verify installation
$installedService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($installedService) {
    Write-Host ""
    Write-Host "Installation Complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Service Status: $($installedService.Status)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "To start the service, run:" -ForegroundColor Yellow
    Write-Host "  Start-Service -Name $ServiceName" -ForegroundColor White
    Write-Host ""
    Write-Host "To check service status:" -ForegroundColor Yellow
    Write-Host "  Get-Service -Name $ServiceName" -ForegroundColor White
    Write-Host ""
    Write-Host "To view service logs:" -ForegroundColor Yellow
    Write-Host "  Get-EventLog -LogName Application -Source $ServiceName -Newest 50" -ForegroundColor White
    Write-Host ""
    Write-Host "Service configuration file location:" -ForegroundColor Yellow
    Write-Host "  $(Split-Path $ServicePath)\appsettings.json" -ForegroundColor White
    Write-Host ""
} else {
    Write-Error "Service installation verification failed."
    exit 1
}
