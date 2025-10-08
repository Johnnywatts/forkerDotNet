# ForkerDotNet Console - Docker Deployment Guide

## Platform-Specific Docker Deployment

### Development Machine: Docker + WSL (Linux Containers)
- **OS:** Windows 10/11 with Docker Desktop
- **Backend:** WSL2 (Linux containers)
- **Use:** `Dockerfile` + `docker-compose.yml`

### NHS Servers: Docker without WSL (Windows Containers)
- **OS:** Windows Server 2019/2022
- **Backend:** Windows containers (no WSL)
- **Use:** `Dockerfile.windows` + `docker-compose.windows.yml`

---

## Quick Reference

| Environment | Dockerfile | Compose File | Base Image | Size |
|-------------|-----------|--------------|------------|------|
| **Dev (Linux)** | `Dockerfile` | `docker-compose.yml` | `scratch` | ~15MB |
| **NHS (Windows)** | `Dockerfile.windows` | `docker-compose.windows.yml` | `nanoserver:ltsc2022` | ~300MB |

---

## Development Deployment (Linux Containers)

### Prerequisites
- Docker Desktop with WSL2 enabled
- PowerShell 7.5+

### Build & Run

```powershell
cd C:\Dev\win_repos\forkerDotNet\src\Forker.Console

# Build Linux container
docker build -t forker-console:latest .

# Run with docker-compose
docker-compose up -d

# Access console
Start-Process http://localhost:5000
```

**Image Size:** ~15MB (scratch-based)
**Startup Time:** ~5 seconds

---

## NHS Server Deployment (Windows Containers)

### Prerequisites on NHS Server
- Windows Server 2019/2022
- Docker installed (Windows containers mode)
- PowerShell 7.5.1+
- ForkerDemo database at `C:\ForkerDemo\forker.db`

### Verify Windows Containers Mode

```powershell
# Check Docker version and OS
docker version

# Look for:
# Server:
#  OS/Arch: windows/amd64
```

**If shows linux/amd64:** Switch to Windows containers
```powershell
# In Docker Desktop: Right-click tray icon → Switch to Windows containers
# On Windows Server: Docker is already in Windows containers mode
```

### Option 1: Build Locally on NHS Server

```powershell
# Transfer source code to NHS server
# Or clone from repository

cd C:\ForkerConsole

# Build Windows container
docker build -f Dockerfile.windows -t forker-console:latest .

# Run with Windows compose file
docker-compose -f docker-compose.windows.yml up -d

# Verify
Invoke-RestMethod http://localhost:5000/health
```

### Option 2: Build on Dev, Transfer to NHS

**On Development Machine:**

```powershell
cd C:\Dev\win_repos\forkerDotNet\src\Forker.Console

# Switch Docker Desktop to Windows containers mode
# (Right-click Docker tray icon → Switch to Windows containers)

# Build Windows container
docker build -f Dockerfile.windows -t forker-console:latest .

# Save image to tar file
docker save -o forker-console-windows.tar forker-console:latest

# Compress for transfer
Compress-Archive -Path forker-console-windows.tar -DestinationPath forker-console-windows.zip
```

**Transfer to NHS Server:**

```powershell
# Copy via network share or SCP
Copy-Item forker-console-windows.zip \\nhs-server\C$\Temp\
```

**On NHS Server:**

```powershell
cd C:\Temp

# Extract and load image
Expand-Archive forker-console-windows.zip -DestinationPath .
docker load -i forker-console-windows.tar

# Verify image loaded
docker images forker-console

# Run container
docker run -d `
  --name forker-console `
  -p 127.0.0.1:5000:5000 `
  -v C:\ForkerDemo\forker.db:C:\data\forker.db:ro `
  --restart unless-stopped `
  forker-console:latest

# Check status
docker ps
docker logs forker-console

# Test health
Invoke-RestMethod http://localhost:5000/health
```

---

## Deployment Scripts

### build-windows.ps1

```powershell
#Requires -Version 7
param(
    [switch]$Local,
    [switch]$Package
)

$ErrorActionPreference = "Stop"

Write-Host "ForkerDotNet Console - Windows Container Build" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Check Docker is in Windows containers mode
Write-Host "[1/3] Checking Docker mode..." -ForegroundColor Yellow
$dockerInfo = docker version --format '{{.Server.Os}}'
if ($dockerInfo -ne "windows") {
    Write-Host "✗ Docker is in Linux containers mode" -ForegroundColor Red
    Write-Host "  Switch to Windows containers:" -ForegroundColor Yellow
    Write-Host "  - Docker Desktop: Right-click tray icon → Switch to Windows containers" -ForegroundColor Gray
    Write-Host "  - Windows Server: Already in Windows mode by default" -ForegroundColor Gray
    exit 1
}
Write-Host "  ✓ Docker in Windows containers mode" -ForegroundColor Green

# Build Windows container
Write-Host "[2/3] Building Windows container..." -ForegroundColor Yellow
docker build -f Dockerfile.windows -t forker-console:latest .

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Build successful" -ForegroundColor Green

    # Show image details
    Write-Host ""
    docker images forker-console:latest --format "table {{.Repository}}\t{{.Tag}}\t{{.Size}}"
} else {
    Write-Host "  ✗ Build failed" -ForegroundColor Red
    exit 1
}

# Package for transfer
if ($Package) {
    Write-Host "[3/3] Creating deployment package..." -ForegroundColor Yellow

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $tarFile = "forker-console-windows-$timestamp.tar"
    $zipFile = "forker-console-windows-$timestamp.zip"

    # Save image
    docker save -o $tarFile forker-console:latest

    # Compress
    Compress-Archive -Path $tarFile -DestinationPath $zipFile -Force

    # Cleanup tar
    Remove-Item $tarFile

    $zipSize = (Get-Item $zipFile).Length / 1MB
    Write-Host "  ✓ Package created: $zipFile ($($zipSize.ToString('F1')) MB)" -ForegroundColor Green
    Write-Host ""
    Write-Host "Transfer to NHS server and run:" -ForegroundColor Yellow
    Write-Host "  Expand-Archive $zipFile" -ForegroundColor Gray
    Write-Host "  docker load -i forker-console-windows-*.tar" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
if ($Local) {
    Write-Host "  docker-compose -f docker-compose.windows.yml up -d" -ForegroundColor Gray
} else {
    Write-Host "  Transfer image to NHS server" -ForegroundColor Gray
    Write-Host "  Run: docker load -i forker-console-windows-*.tar" -ForegroundColor Gray
}
```

### deploy-nhs.ps1 (for NHS servers)

```powershell
#Requires -Version 7
#Requires -RunAsAdministrator

param(
    [Parameter(Mandatory=$true)]
    [string]$ImagePath,

    [string]$DatabasePath = "C:\ForkerDemo\forker.db",
    [string]$ContainerName = "forker-console",
    [int]$Port = 5000
)

$ErrorActionPreference = "Stop"

Write-Host "ForkerDotNet Console - NHS Deployment" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Validate prerequisites
Write-Host "[1/6] Validating prerequisites..." -ForegroundColor Yellow

# Check PowerShell version
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Host "✗ PowerShell 7+ required (found $($PSVersionTable.PSVersion))" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ PowerShell $($PSVersionTable.PSVersion)" -ForegroundColor Green

# Check Docker
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host "✗ Docker not found" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ Docker installed" -ForegroundColor Green

# Check Windows containers mode
$dockerInfo = docker version --format '{{.Server.Os}}'
if ($dockerInfo -ne "windows") {
    Write-Host "✗ Docker not in Windows containers mode" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ Windows containers mode" -ForegroundColor Green

# Check database
if (-not (Test-Path $DatabasePath)) {
    Write-Host "⚠ Database not found: $DatabasePath" -ForegroundColor Yellow
    Write-Host "  Console will start when database becomes available" -ForegroundColor Gray
} else {
    Write-Host "  ✓ Database found" -ForegroundColor Green
}

# Load image
Write-Host "[2/6] Loading Docker image..." -ForegroundColor Yellow
if (-not (Test-Path $ImagePath)) {
    Write-Host "✗ Image file not found: $ImagePath" -ForegroundColor Red
    exit 1
}

docker load -i $ImagePath
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Failed to load image" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ Image loaded" -ForegroundColor Green

# Stop existing container
Write-Host "[3/6] Checking for existing container..." -ForegroundColor Yellow
$existing = docker ps -a --filter "name=$ContainerName" --format "{{.ID}}"
if ($existing) {
    Write-Host "  Stopping and removing existing container..." -ForegroundColor Yellow
    docker stop $ContainerName | Out-Null
    docker rm $ContainerName | Out-Null
    Write-Host "  ✓ Old container removed" -ForegroundColor Green
}

# Configure firewall
Write-Host "[4/6] Configuring firewall..." -ForegroundColor Yellow
$rule = Get-NetFirewallRule -DisplayName "ForkerDotNet Console" -ErrorAction SilentlyContinue
if ($rule) {
    Remove-NetFirewallRule -DisplayName "ForkerDotNet Console"
}

New-NetFirewallRule `
    -DisplayName "ForkerDotNet Console" `
    -Direction Inbound `
    -LocalPort $Port `
    -Protocol TCP `
    -Action Allow `
    -Profile Domain,Private | Out-Null

Write-Host "  ✓ Firewall rule created (port $Port)" -ForegroundColor Green

# Start container
Write-Host "[5/6] Starting container..." -ForegroundColor Yellow

docker run -d `
    --name $ContainerName `
    -p "127.0.0.1:${Port}:5000" `
    -v "${DatabasePath}:C:\data\forker.db:ro" `
    -e "FORKER_DB_PATH=C:\data\forker.db" `
    -e "FORKER_MODE=production" `
    --restart unless-stopped `
    --memory 256m `
    --cpus 0.5 `
    forker-console:latest

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Failed to start container" -ForegroundColor Red
    exit 1
}

Start-Sleep -Seconds 5
Write-Host "  ✓ Container started" -ForegroundColor Green

# Verify health
Write-Host "[6/6] Verifying health..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

try {
    $response = Invoke-RestMethod "http://localhost:$Port/health" -TimeoutSec 10
    if ($response.status -eq "healthy") {
        Write-Host "  ✓ Health check passed" -ForegroundColor Green
    }
} catch {
    Write-Host "  ⚠ Health check failed (container may still be starting)" -ForegroundColor Yellow
    Write-Host "  Check logs: docker logs $ContainerName" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Deployment complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Console URL:    http://localhost:$Port" -ForegroundColor Cyan
Write-Host "Container name: $ContainerName" -ForegroundColor Cyan
Write-Host ""
Write-Host "Management commands:" -ForegroundColor Yellow
Write-Host "  Logs:    docker logs $ContainerName -f" -ForegroundColor Gray
Write-Host "  Stop:    docker stop $ContainerName" -ForegroundColor Gray
Write-Host "  Start:   docker start $ContainerName" -ForegroundColor Gray
Write-Host "  Remove:  docker rm -f $ContainerName" -ForegroundColor Gray
```

---

## Container Size Comparison

### Linux Container (scratch)
- **Base Image:** `scratch` (0 bytes)
- **Binary:** ~12MB (static Go binary)
- **Total:** ~15MB
- **Startup:** ~3 seconds
- **Memory:** ~25MB idle

### Windows Container (nanoserver)
- **Base Image:** `mcr.microsoft.com/windows/nanoserver:ltsc2022` (~280MB)
- **Binary:** ~12MB (static Go binary)
- **Total:** ~300MB
- **Startup:** ~5 seconds
- **Memory:** ~40MB idle

**Why Larger?**
- Windows nanoserver includes minimal Windows OS
- Still much smaller than windowsservercore (~5GB!)
- Trade-off: Compatibility with Windows Docker on NHS servers

---

## Troubleshooting

### "No matching manifest for windows/amd64"

**Problem:** Trying to run Linux container on Windows Docker

**Solution:** Use `Dockerfile.windows` instead of `Dockerfile`

### "The container operating system does not match the host"

**Problem:** Docker in wrong mode (Linux vs Windows)

**Solution:** Switch Docker to Windows containers mode
- Docker Desktop: Right-click tray → Switch to Windows containers
- Windows Server: Already in Windows mode

### Build fails with "network timeout"

**Problem:** Windows container base images are large (~280MB)

**Solution:**
- Be patient (first pull takes 5-10 minutes)
- Check internet connection
- Consider pre-pulling base image:
  ```powershell
  docker pull mcr.microsoft.com/windows/nanoserver:ltsc2022
  ```

### Container exits immediately

**Problem:** Database path incorrect or inaccessible

**Solution:** Check logs
```powershell
docker logs forker-console

# Fix database path in volume mount
# -v "C:\ActualPath\forker.db:C:\data\forker.db:ro"
```

---

## Security Considerations

### Windows Container Security

**Limitations vs Linux:**
- ⚠️ Windows containers don't support `USER` directive (runs as ContainerAdministrator)
- ⚠️ Can't use `scratch` base (requires Windows kernel)
- ⚠️ Larger attack surface than Linux scratch

**Mitigations:**
- ✅ Read-only database mount
- ✅ Localhost-only binding (127.0.0.1)
- ✅ Resource limits enforced
- ✅ Minimal nanoserver base (not servercore)
- ✅ No additional software installed

### NHS Scanning

```powershell
# Scan Windows container
docker scan forker-console:latest

# Or use Windows Defender
# Container images in: C:\ProgramData\Docker\windowsfilter
```

---

## Update Process

### Rolling Update

```powershell
# On NHS server

# 1. Pull/load new image
docker load -i forker-console-windows-new.tar

# 2. Tag as new version
docker tag forker-console:latest forker-console:v1.1

# 3. Stop old container
docker stop forker-console

# 4. Rename old container (backup)
docker rename forker-console forker-console-old

# 5. Start new container
docker run -d `
    --name forker-console `
    -p 127.0.0.1:5000:5000 `
    -v C:\ForkerDemo\forker.db:C:\data\forker.db:ro `
    --restart unless-stopped `
    forker-console:v1.1

# 6. Verify
Invoke-RestMethod http://localhost:5000/health

# 7. Remove old container (if successful)
docker rm forker-console-old
```

**Downtime:** ~10 seconds

---

## Production Checklist

### Development Machine
- [ ] Switch Docker Desktop to Windows containers mode
- [ ] Build Windows container: `docker build -f Dockerfile.windows -t forker-console:latest .`
- [ ] Test locally if possible
- [ ] Save image: `docker save -o forker-console-windows.tar forker-console:latest`
- [ ] Compress: `Compress-Archive forker-console-windows.tar ...`
- [ ] Transfer to NHS server

### NHS Server
- [ ] Verify Docker in Windows containers mode
- [ ] Verify database exists: `C:\ForkerDemo\forker.db`
- [ ] Load image: `docker load -i forker-console-windows.tar`
- [ ] Run deployment script: `.\deploy-nhs.ps1 -ImagePath forker-console-windows.tar`
- [ ] Test health: `Invoke-RestMethod http://localhost:5000/health`
- [ ] Open dashboard: `http://localhost:5000`
- [ ] Verify service restarts after reboot
- [ ] Document for ops team

---

## Summary

| | Development | NHS Production |
|---|---|---|
| **Docker Backend** | WSL2 (Linux) | Windows Containers |
| **Dockerfile** | `Dockerfile` | `Dockerfile.windows` |
| **Compose File** | `docker-compose.yml` | `docker-compose.windows.yml` |
| **Base Image** | `scratch` | `nanoserver:ltsc2022` |
| **Size** | ~15MB | ~300MB |
| **Build Script** | `build.ps1 -Docker` | `build-windows.ps1 -Package` |
| **Deploy Script** | `docker-compose up -d` | `deploy-nhs.ps1` |

**Both deployments use the same Go source code and achieve the same functionality!**
