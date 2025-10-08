# Build script for ForkerDotNet Console
# Builds Linux containers (for WSL/Docker Desktop development)
# For Windows containers (NHS deployment), use: build-windows.ps1

param(
    [switch]$Docker,
    [switch]$Run,
    [switch]$Stop
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  ForkerDotNet Console - Build (Linux Containers)    ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Detect Docker mode
try {
    $dockerOS = docker version --format '{{.Server.Os}}' 2>$null
    if ($dockerOS -eq "windows") {
        Write-Host "⚠ WARNING: Docker is in Windows containers mode" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  This script builds Linux containers for development." -ForegroundColor Yellow
        Write-Host "  You are currently in Windows containers mode." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  Options:" -ForegroundColor Cyan
        Write-Host "    1. Switch to Linux containers: Right-click Docker tray → 'Switch to Linux containers'" -ForegroundColor Gray
        Write-Host "    2. Use Windows build:          .\build-windows.ps1" -ForegroundColor Gray
        Write-Host ""

        $response = Read-Host "  Continue anyway? (build will likely fail) [y/N]"
        if ($response -ne 'y' -and $response -ne 'Y') {
            Write-Host ""
            Write-Host "Build cancelled. Switch Docker mode or use build-windows.ps1" -ForegroundColor Gray
            exit 0
        }
    }
} catch {
    Write-Host "⚠ Could not detect Docker mode (Docker may not be running)" -ForegroundColor Yellow
    Write-Host ""
}

if ($Docker) {
    Write-Host "Building Docker image (Linux container)..." -ForegroundColor Yellow
    Write-Host ""

    docker build -t forker-console:latest .

    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "  ✓ Docker image built successfully" -ForegroundColor Green

        # Show image size
        Write-Host ""
        Write-Host "Image details:" -ForegroundColor Yellow
        docker images forker-console:latest --format "table {{.Repository}}\t{{.Tag}}\t{{.Size}}\t{{.CreatedAt}}"
    } else {
        Write-Host ""
        Write-Host "  ✗ Docker build failed" -ForegroundColor Red
        exit 1
    }
}

if ($Run) {
    Write-Host "Starting console with Docker Compose..." -ForegroundColor Yellow
    Write-Host ""

    docker-compose up -d

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Console started" -ForegroundColor Green
        Write-Host ""
        Write-Host "  Console URL:   http://localhost:5000" -ForegroundColor Cyan
        Write-Host "  Health check:  http://localhost:5000/health" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Commands:" -ForegroundColor Yellow
        Write-Host "  View logs:  docker-compose logs -f" -ForegroundColor Gray
        Write-Host "  Stop:       docker-compose down" -ForegroundColor Gray
        Write-Host "  Restart:    docker-compose restart" -ForegroundColor Gray
    } else {
        Write-Host "  ✗ Failed to start console" -ForegroundColor Red
        exit 1
    }
}

if ($Stop) {
    Write-Host "Stopping console..." -ForegroundColor Yellow
    docker-compose down

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Console stopped" -ForegroundColor Green
    }
}

if (-not $Docker -and -not $Run -and -not $Stop) {
    Write-Host "Usage:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Development (Linux containers):" -ForegroundColor Cyan
    Write-Host "    .\build.ps1 -Docker           # Build Linux image" -ForegroundColor Gray
    Write-Host "    .\build.ps1 -Run              # Start console" -ForegroundColor Gray
    Write-Host "    .\build.ps1 -Stop             # Stop console" -ForegroundColor Gray
    Write-Host "    .\build.ps1 -Docker -Run      # Build and run" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  NHS Deployment (Windows containers):" -ForegroundColor Cyan
    Write-Host "    .\build-windows.ps1           # Build Windows image" -ForegroundColor Gray
    Write-Host "    .\build-windows.ps1 -Package  # Create deployment package" -ForegroundColor Gray
    Write-Host "    .\build-windows.ps1 -Test     # Build and test locally" -ForegroundColor Gray
    Write-Host ""
}
