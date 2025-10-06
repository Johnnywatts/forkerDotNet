#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Sets up the ForkerDotNet demo environment
.DESCRIPTION
    Creates demo directories, installs ForkerDotNet service, and prepares environment for clinical demonstration
.PARAMETER DemoPath
    Root path for demo directories (default: C:\ForkerDemo)
.PARAMETER SkipServiceInstall
    Skip ForkerDotNet service installation
.EXAMPLE
    .\Demo-Setup.ps1
    .\Demo-Setup.ps1 -DemoPath "D:\Demo" -SkipServiceInstall
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$DemoPath = "C:\ForkerDemo",

    [Parameter()]
    [switch]$SkipServiceInstall
)

$ErrorActionPreference = "Stop"

Write-Host "ForkerDotNet Demo Environment Setup" -ForegroundColor Green
Write-Host "===================================" -ForegroundColor Green
Write-Host ""

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

# Step 1: Create demo directories
Write-Status "Creating demo directories..." "Info"

$directories = @(
    "$DemoPath\Reservoir",
    "$DemoPath\Input",
    "$DemoPath\DestinationA",
    "$DemoPath\DestinationB",
    "$DemoPath\Archive",
    "$DemoPath\Quarantine",
    "$DemoPath\Logs"
)

foreach ($dir in $directories) {
    if (!(Test-Path $dir)) {
        New-Item -Path $dir -ItemType Directory -Force | Out-Null
        Write-Status "Created: $dir" "Success"
    } else {
        Write-Status "Exists: $dir" "Info"
    }
}

# Step 2: Set directory permissions
Write-Status "Setting directory permissions..." "Info"

try {
    # Grant full control to current user and SYSTEM
    $acl = Get-Acl $DemoPath
    $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $env:USERNAME, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow"
    )
    $acl.SetAccessRule($accessRule)
    Set-Acl -Path $DemoPath -AclObject $acl
    Write-Status "Directory permissions configured" "Success"
} catch {
    Write-Status "Warning: Could not set directory permissions - $($_.Exception.Message)" "Warning"
}

# Step 3: Check .NET 8 installation
Write-Status "Checking .NET 8 installation..." "Info"

try {
    $dotnetVersion = & dotnet --version 2>$null
    if ($dotnetVersion -and $dotnetVersion.StartsWith("8.")) {
        Write-Status ".NET 8 detected: $dotnetVersion" "Success"
    } else {
        Write-Status ".NET 8 not found - please install .NET 8 SDK" "Error"
        Write-Host "Download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    }
} catch {
    Write-Status ".NET runtime not found - please install .NET 8" "Error"
}

# Step 4: Build demo applications
Write-Status "Building demo applications..." "Info"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$demoSolution = Join-Path $repoRoot "demo\Demo.sln"

if (Test-Path $demoSolution) {
    try {
        Push-Location (Split-Path $demoSolution)

        Write-Status "Restoring packages..." "Info"
        & dotnet restore 2>$null

        Write-Status "Building solution..." "Info"
        & dotnet build --configuration Release --no-restore 2>$null

        if ($LASTEXITCODE -eq 0) {
            Write-Status "Demo applications built successfully" "Success"
        } else {
            Write-Status "Build failed - check output above" "Error"
        }
    } finally {
        Pop-Location
    }
} else {
    Write-Status "Demo solution not found: $demoSolution" "Warning"
}

# Step 5: Install ForkerDotNet service (optional)
if (-not $SkipServiceInstall) {
    Write-Status "Installing ForkerDotNet service..." "Info"

    $serviceInstallScript = Join-Path $PSScriptRoot "Install-Service.ps1"
    if (Test-Path $serviceInstallScript) {
        try {
            & $serviceInstallScript -DemoMode
            Write-Status "ForkerDotNet service installation attempted" "Success"
        } catch {
            Write-Status "Service installation failed: $($_.Exception.Message)" "Error"
        }
    } else {
        Write-Status "Service install script not found - manual installation required" "Warning"
    }
}

# Step 6: Create desktop shortcuts (optional)
Write-Status "Creating demo shortcuts..." "Info"

$shortcuts = @{
    "Demo Dashboard" = "http://localhost:5000"
    "Demo File Explorer" = $DemoPath
}

$desktop = [Environment]::GetFolderPath("Desktop")

foreach ($shortcut in $shortcuts.GetEnumerator()) {
    try {
        if ($shortcut.Value.StartsWith("http")) {
            # Create URL shortcut
            $shortcutPath = Join-Path $desktop "$($shortcut.Key).url"
            "[InternetShortcut]`nURL=$($shortcut.Value)" | Out-File -FilePath $shortcutPath -Encoding ASCII
        } else {
            # Create folder shortcut
            $shell = New-Object -ComObject WScript.Shell
            $shortcutPath = Join-Path $desktop "$($shortcut.Key).lnk"
            $link = $shell.CreateShortcut($shortcutPath)
            $link.TargetPath = $shortcut.Value
            $link.Save()
        }
        Write-Status "Created shortcut: $($shortcut.Key)" "Success"
    } catch {
        Write-Status "Could not create shortcut: $($shortcut.Key)" "Warning"
    }
}

# Step 7: Display setup summary
Write-Host ""
Write-Host "Setup Summary" -ForegroundColor Green
Write-Host "=============" -ForegroundColor Green
Write-Host ""

Write-Status "Demo directories created in: $DemoPath" "Success"
Write-Status "Place medical imaging files in: $DemoPath\Reservoir" "Info"
Write-Status "Dashboard will be available at: http://localhost:5000" "Info"
Write-Status "Log files will be stored in: $DemoPath\Logs" "Info"

Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "1. Copy medical imaging files to $DemoPath\Reservoir"
Write-Host "2. Start Demo.Dashboard: dotnet run --project demo\src\Demo.Dashboard"
Write-Host "3. Run Demo.Controller: dotnet run --project demo\src\Demo.Controller"
Write-Host "4. Use Demo.FileDropper to orchestrate file movement"
Write-Host ""

Write-Status "Demo environment setup complete!" "Success"