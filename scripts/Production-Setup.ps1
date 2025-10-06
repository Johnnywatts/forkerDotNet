#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Sets up the ForkerDotNet production environment
.DESCRIPTION
    Creates production directories at C:\ProgramData\ForkerDotNet and prepares for service installation
.PARAMETER ProductionPath
    Root path for production directories (default: C:\ProgramData\ForkerDotNet)
.PARAMETER SkipServiceInstall
    Skip ForkerDotNet service installation
.EXAMPLE
    .\Production-Setup.ps1
    .\Production-Setup.ps1 -ProductionPath "D:\ForkerDotNet"
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$ProductionPath = "C:\ProgramData\ForkerDotNet",

    [Parameter()]
    [switch]$SkipServiceInstall
)

$ErrorActionPreference = "Stop"

Write-Host "ForkerDotNet Production Environment Setup" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green
Write-Host ""

# Function to write colored output
function Write-Status {
    param($Message, $Type = "Info")

    switch ($Type) {
        "Success" { Write-Host "[OK] $Message" -ForegroundColor Green }
        "Warning" { Write-Host "[WARN] $Message" -ForegroundColor Yellow }
        "Error"   { Write-Host "[ERROR] $Message" -ForegroundColor Red }
        "Info"    { Write-Host "[INFO] $Message" -ForegroundColor Cyan }
        default   { Write-Host "$Message" }
    }
}

# Step 1: Create production directories
Write-Status "Creating production directories..." "Info"

$directories = @(
    "$ProductionPath\Input",
    "$ProductionPath\DestinationA",
    "$ProductionPath\DestinationB",
    "$ProductionPath\Quarantine",
    "$ProductionPath\Processing",
    "$ProductionPath\Logs"
)

foreach ($dir in $directories) {
    if (!(Test-Path $dir)) {
        New-Item -Path $dir -ItemType Directory -Force | Out-Null
        Write-Status "Created: $dir" "Success"
    } else {
        Write-Status "Exists: $dir" "Info"
    }
}

# Step 2: Set directory permissions (Production-grade)
Write-Status "Setting directory permissions for service account..." "Info"

try {
    # Grant full control to SYSTEM and Administrators
    $acl = Get-Acl $ProductionPath

    # SYSTEM account
    $systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "SYSTEM", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow"
    )
    $acl.SetAccessRule($systemRule)

    # Administrators group
    $adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "Administrators", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow"
    )
    $acl.SetAccessRule($adminRule)

    Set-Acl -Path $ProductionPath -AclObject $acl
    Write-Status "Directory permissions configured for production" "Success"
} catch {
    Write-Status "Warning: Could not set directory permissions - $($_.Exception.Message)" "Warning"
}

# Step 3: Check .NET 8 installation
Write-Status "Checking .NET 8 runtime installation..." "Info"

try {
    $dotnetVersion = & dotnet --version 2>$null
    if ($dotnetVersion -and $dotnetVersion.StartsWith("8.")) {
        Write-Status ".NET 8 detected: $dotnetVersion" "Success"
    } else {
        Write-Status ".NET 8 not found - please install .NET 8 Runtime" "Error"
        Write-Host "Download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    }
} catch {
    Write-Status ".NET runtime not found - please install .NET 8" "Error"
}

# Step 4: Build ForkerDotNet service
Write-Status "Building ForkerDotNet service..." "Info"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$serviceSolution = Join-Path $repoRoot "Forker.sln"

if (Test-Path $serviceSolution) {
    try {
        Push-Location $repoRoot

        Write-Status "Restoring packages..." "Info"
        & dotnet restore Forker.sln 2>$null

        Write-Status "Building solution..." "Info"
        & dotnet build Forker.sln --configuration Release --no-restore 2>$null

        if ($LASTEXITCODE -eq 0) {
            Write-Status "ForkerDotNet built successfully" "Success"
        } else {
            Write-Status "Build failed - check output above" "Error"
        }
    } finally {
        Pop-Location
    }
} else {
    Write-Status "Solution not found: $serviceSolution" "Error"
}

# Step 5: Install ForkerDotNet service (optional)
if (-not $SkipServiceInstall) {
    Write-Status "Installing ForkerDotNet Windows Service..." "Info"

    $serviceInstallScript = Join-Path $repoRoot "scripts\Install-ForkerService.ps1"
    if (Test-Path $serviceInstallScript) {
        try {
            # Install with Production environment (uses appsettings.json)
            & $serviceInstallScript
            Write-Status "ForkerDotNet service installation attempted" "Success"
        } catch {
            Write-Status "Service installation failed: $($_.Exception.Message)" "Error"
        }
    } else {
        Write-Status "Service install script not found at: $serviceInstallScript" "Warning"
        Write-Host ""
        Write-Host "Manual installation steps:" -ForegroundColor Yellow
        Write-Host "1. cd $repoRoot\src\Forker.Service" -ForegroundColor Gray
        Write-Host "2. dotnet publish -c Release" -ForegroundColor Gray
        Write-Host "3. sc.exe create ForkerDotNet binPath=<published exe path>" -ForegroundColor Gray
    }
}

# Step 6: Display setup summary
Write-Host ""
Write-Host "Setup Summary" -ForegroundColor Green
Write-Host "=============" -ForegroundColor Green
Write-Host ""

Write-Status "Production directories created in: $ProductionPath" "Success"
Write-Status "Configuration file: src\Forker.Service\appsettings.json" "Info"
Write-Status "Log files will be stored in: $ProductionPath\Logs" "Info"
Write-Status "Database will be created at: $ProductionPath\forker.db" "Info"

Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "1. Review configuration in src\Forker.Service\appsettings.json" -ForegroundColor Gray
Write-Host "2. Start service: Start-Service ForkerDotNet (if installed)" -ForegroundColor Gray
Write-Host "3. Or run manually: cd src\Forker.Service && dotnet run" -ForegroundColor Gray
Write-Host "4. Monitor logs: Get-Content $ProductionPath\Logs\forker-*.txt -Wait" -ForegroundColor Gray
Write-Host ""

Write-Status "Production environment setup complete!" "Success"
