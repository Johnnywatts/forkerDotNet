#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls the ForkerDotNet Windows Service.

.DESCRIPTION
    This script stops and removes the ForkerDotNet Windows Service.

.PARAMETER ServiceName
    Name of the Windows Service to uninstall. Defaults to "ForkerDotNet".

.EXAMPLE
    .\Uninstall-ForkerService.ps1
    Uninstalls the ForkerDotNet service.
#>

param(
    [string]$ServiceName = "ForkerDotNet"
)

$ErrorActionPreference = "Stop"

Write-Host "ForkerDotNet Service Uninstallation Script" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script must be run as Administrator."
    exit 1
}

# Check if service exists
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Warning "Service '$ServiceName' not found. Nothing to uninstall."
    exit 0
}

Write-Host "Found service: $($service.DisplayName)" -ForegroundColor Green
Write-Host "Current status: $($service.Status)" -ForegroundColor Cyan
Write-Host ""

# Stop the service if running
if ($service.Status -eq 'Running') {
    Write-Host "Stopping service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 2
    Write-Host "Service stopped." -ForegroundColor Green
}

# Remove the service
Write-Host "Removing service..." -ForegroundColor Yellow
sc.exe delete $ServiceName | Out-Null

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Service '$ServiceName' has been successfully uninstalled." -ForegroundColor Green
    Write-Host ""
} else {
    Write-Error "Failed to remove service."
    exit 1
}
