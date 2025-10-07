#Requires -Version 5.1

<#
.SYNOPSIS
    Starts ForkerDotNet service in Demo mode
.DESCRIPTION
    Convenience script to start ForkerDotNet with the Demo environment configuration.
    Uses ASPNETCORE_ENVIRONMENT=Demo to load appsettings.Demo.json overlay.
.EXAMPLE
    .\Start-ForkerDemo.ps1
.NOTES
    Database Location: C:\ForkerDemo\forker.db
    Source Directory: C:\ForkerDemo\Input
    Target Directories: C:\ForkerDemo\DestinationA, C:\ForkerDemo\DestinationB
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

# Set Demo environment
$env:ASPNETCORE_ENVIRONMENT = "Demo"

Write-Host ""
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  Starting ForkerDotNet in Demo Mode" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  [INFO] Environment: Demo" -ForegroundColor Gray
Write-Host "  [INFO] Database: C:\ForkerDemo\forker.db" -ForegroundColor Gray
Write-Host "  [INFO] Watching: C:\ForkerDemo\Input" -ForegroundColor Gray
Write-Host "  [INFO] Targets: C:\ForkerDemo\DestinationA, DestinationB" -ForegroundColor Gray
Write-Host ""
Write-Host "  Press Ctrl+C to stop the service" -ForegroundColor Yellow
Write-Host ""
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host ""

# Navigate to service directory
$servicePath = Join-Path $PSScriptRoot "..\src\Forker.Service"
Push-Location $servicePath

try {
    # Run the service with explicit environment variable
    # Note: Setting $env:ASPNETCORE_ENVIRONMENT should be inherited, but we verify it here
    Write-Host "  [DEBUG] ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT" -ForegroundColor DarkGray
    Write-Host ""
    dotnet run --environment Demo
} finally {
    Pop-Location
}
