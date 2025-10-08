#Requires -Version 5.1

<#
.SYNOPSIS
    Starts ForkerDotNet service in Production mode
.DESCRIPTION
    Convenience script to start ForkerDotNet with the Production environment configuration.
    Uses ASPNETCORE_ENVIRONMENT=Production to load appsettings.Production.json overlay.
.EXAMPLE
    .\Start-ForkerProduction.ps1
.NOTES
    Database Location: Production database path (configured in appsettings.Production.json)
    Source Directory: Production source path
    Target Directories: Production targets

    WARNING: This starts the service in PRODUCTION mode. Ensure all paths and
    configurations are correct before running.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

# Set Production environment
$env:ASPNETCORE_ENVIRONMENT = "Production"

Write-Host ""
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  Starting ForkerDotNet in PRODUCTION Mode" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  [WARN] Running in PRODUCTION mode" -ForegroundColor Yellow
Write-Host "  [INFO] Environment: Production" -ForegroundColor Gray
Write-Host "  [INFO] Configuration: appsettings.Production.json" -ForegroundColor Gray
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
    Write-Host "  [DEBUG] ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT" -ForegroundColor DarkGray
    Write-Host ""
    dotnet run --environment Production
} finally {
    Pop-Location
}
