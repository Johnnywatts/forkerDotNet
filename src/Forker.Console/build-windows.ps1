#Requires -Version 7
<#
.SYNOPSIS
    Build ForkerDotNet Console for Windows containers (NHS deployment)

.DESCRIPTION
    Builds a Windows container image compatible with NHS servers running
    Docker without WSL (Windows containers only).

.PARAMETER Package
    Create deployment package (tar + zip) for transfer to NHS servers

.PARAMETER Test
    Run the built container locally for testing

.EXAMPLE
    .\build-windows.ps1
    # Build Windows container

.EXAMPLE
    .\build-windows.ps1 -Package
    # Build and create deployment package

.EXAMPLE
    .\build-windows.ps1 -Package -Test
    # Build, package, and test locally
#>

param(
    [switch]$Package,
    [switch]$Test
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  ForkerDotNet Console - Windows Container Build     ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check Docker mode
Write-Host "[1/4] Checking Docker configuration..." -ForegroundColor Yellow

try {
    $dockerInfo = docker version --format '{{.Server.Os}}' 2>$null
} catch {
    Write-Host "  ✗ Docker not running or not installed" -ForegroundColor Red
    Write-Host "    Start Docker Desktop or install Docker" -ForegroundColor Gray
    exit 1
}

if ($dockerInfo -ne "windows") {
    Write-Host "  ⚠ Docker is in Linux containers mode" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  To build Windows containers, switch Docker mode:" -ForegroundColor Yellow
    Write-Host "    • Docker Desktop: Right-click tray icon → 'Switch to Windows containers'" -ForegroundColor Gray
    Write-Host "    • Windows Server: Already in Windows mode by default" -ForegroundColor Gray
    Write-Host ""

    $response = Read-Host "  Would you like to continue anyway? (build will fail) [y/N]"
    if ($response -ne 'y' -and $response -ne 'Y') {
        Write-Host "  Aborted." -ForegroundColor Gray
        exit 0
    }
} else {
    Write-Host "  ✓ Docker in Windows containers mode" -ForegroundColor Green
}

# Step 2: Build Windows container
Write-Host ""
Write-Host "[2/4] Building Windows container image..." -ForegroundColor Yellow
Write-Host "  This may take 5-10 minutes on first run (downloads ~280MB base image)" -ForegroundColor Gray
Write-Host ""

$buildStart = Get-Date

docker build -f Dockerfile.windows -t forker-console:latest .

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "  ✗ Build failed" -ForegroundColor Red
    exit 1
}

$buildDuration = (Get-Date) - $buildStart
Write-Host ""
Write-Host "  ✓ Build successful ($($buildDuration.TotalSeconds.ToString('F0'))s)" -ForegroundColor Green

# Step 3: Show image details
Write-Host ""
Write-Host "[3/4] Image details:" -ForegroundColor Yellow
docker images forker-console:latest --format "table {{.Repository}}\t{{.Tag}}\t{{.Size}}\t{{.CreatedAt}}"

# Step 4: Package for deployment
if ($Package) {
    Write-Host ""
    Write-Host "[4/4] Creating deployment package..." -ForegroundColor Yellow

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $version = "1.0.0"  # TODO: Read from version file
    $packageName = "forker-console-windows-$version-$timestamp"
    $tarFile = "$packageName.tar"
    $zipFile = "$packageName.zip"

    Write-Host "  Saving Docker image..." -ForegroundColor Gray
    docker save -o $tarFile forker-console:latest

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ✗ Failed to save image" -ForegroundColor Red
        exit 1
    }

    $tarSize = (Get-Item $tarFile).Length / 1MB
    Write-Host "  ✓ Image saved: $tarFile ($($tarSize.ToString('F1')) MB)" -ForegroundColor Green

    Write-Host "  Compressing package..." -ForegroundColor Gray
    Compress-Archive -Path $tarFile -DestinationPath $zipFile -Force

    $zipSize = (Get-Item $zipFile).Length / 1MB
    Write-Host "  ✓ Package compressed: $zipFile ($($zipSize.ToString('F1')) MB)" -ForegroundColor Green

    # Cleanup tar file
    Remove-Item $tarFile
    Write-Host "  ✓ Cleanup complete" -ForegroundColor Green

    Write-Host ""
    Write-Host "  Deployment package ready:" -ForegroundColor Cyan
    Write-Host "    File: $zipFile" -ForegroundColor White
    Write-Host "    Size: $($zipSize.ToString('F1')) MB" -ForegroundColor White
    Write-Host ""
    Write-Host "  Transfer to NHS server:" -ForegroundColor Yellow
    Write-Host "    Copy-Item $zipFile \\nhs-server\C$\Temp\" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  On NHS server, run:" -ForegroundColor Yellow
    Write-Host "    Expand-Archive $zipFile" -ForegroundColor Gray
    Write-Host "    docker load -i $packageName.tar" -ForegroundColor Gray
    Write-Host "    docker run -d --name forker-console -p 127.0.0.1:5000:5000 -v C:\ForkerDemo\forker.db:C:\data\forker.db:ro forker-console:latest" -ForegroundColor Gray
} else {
    Write-Host ""
    Write-Host "[4/4] Skipping package creation" -ForegroundColor Gray
    Write-Host "  Use -Package flag to create deployment package" -ForegroundColor Gray
}

# Step 5: Test (optional)
if ($Test) {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "Testing Windows container locally..." -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""

    # Check if ForkerDemo database exists
    if (-not (Test-Path "C:\ForkerDemo\forker.db")) {
        Write-Host "⚠ Database not found at C:\ForkerDemo\forker.db" -ForegroundColor Yellow
        Write-Host "  Container will start but may show database errors" -ForegroundColor Gray
        Write-Host ""
    }

    # Stop existing test container
    $existing = docker ps -a --filter "name=forker-console-test" --format "{{.ID}}"
    if ($existing) {
        Write-Host "Stopping existing test container..." -ForegroundColor Gray
        docker stop forker-console-test | Out-Null
        docker rm forker-console-test | Out-Null
    }

    # Start test container
    Write-Host "Starting test container..." -ForegroundColor Yellow
    docker run -d `
        --name forker-console-test `
        -p 127.0.0.1:5000:5000 `
        -v C:\ForkerDemo\forker.db:C:\data\forker.db:ro `
        -e "FORKER_DB_PATH=C:\data\forker.db" `
        -e "FORKER_MODE=demo" `
        forker-console:latest

    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Failed to start container" -ForegroundColor Red
        exit 1
    }

    Write-Host "  ✓ Container started" -ForegroundColor Green
    Write-Host ""
    Write-Host "Waiting for service to start..." -ForegroundColor Gray
    Start-Sleep -Seconds 5

    # Check logs
    Write-Host ""
    Write-Host "Container logs:" -ForegroundColor Yellow
    docker logs forker-console-test

    # Test health endpoint
    Write-Host ""
    Write-Host "Testing health endpoint..." -ForegroundColor Yellow
    try {
        $response = Invoke-RestMethod "http://localhost:5000/health" -TimeoutSec 10
        if ($response.status -eq "healthy") {
            Write-Host "  ✓ Health check passed" -ForegroundColor Green
        } else {
            Write-Host "  ✗ Unexpected response: $($response | ConvertTo-Json)" -ForegroundColor Red
        }
    } catch {
        Write-Host "  ✗ Health check failed: $_" -ForegroundColor Red
        Write-Host ""
        Write-Host "Check logs: docker logs forker-console-test" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "Test container running:" -ForegroundColor Cyan
    Write-Host "  URL:  http://localhost:5000" -ForegroundColor White
    Write-Host "  Name: forker-console-test" -ForegroundColor White
    Write-Host ""
    Write-Host "Commands:" -ForegroundColor Yellow
    Write-Host "  Open:   Start-Process http://localhost:5000" -ForegroundColor Gray
    Write-Host "  Logs:   docker logs forker-console-test -f" -ForegroundColor Gray
    Write-Host "  Stop:   docker stop forker-console-test" -ForegroundColor Gray
    Write-Host "  Remove: docker rm -f forker-console-test" -ForegroundColor Gray
}

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║  Build Complete!                                     ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

if (-not $Test) {
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  • Test locally:  .\build-windows.ps1 -Test" -ForegroundColor Gray
    Write-Host "  • Package:       .\build-windows.ps1 -Package" -ForegroundColor Gray
    Write-Host "  • Deploy to NHS: See DEPLOYMENT-DOCKER.md" -ForegroundColor Gray
    Write-Host ""
}
