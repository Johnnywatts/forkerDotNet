# Deployment Quick Reference

## TL;DR

### Development (You - Docker Desktop + WSL)
```powershell
.\build.ps1 -Docker -Run
# Access at http://localhost:5000
```

### NHS Deployment (Pure Windows Server, Docker without WSL)
```powershell
# On dev machine: Build Windows container
.\build-windows.ps1 -Package

# Transfer forker-console-windows-*.zip to NHS server

# On NHS server: Deploy
Expand-Archive forker-console-windows-*.zip
cd forker-console-windows-*
docker load -i *.tar
docker run -d --name forker-console -p 127.0.0.1:5000:5000 -v C:\ForkerDemo\forker.db:C:\data\forker.db:ro forker-console:latest
```

---

## Platform Differences

| Feature | Your Dev Machine | NHS Servers |
|---------|------------------|-------------|
| **OS** | Windows 10/11 | Windows Server 2019/2022 |
| **Docker Backend** | WSL2 (Linux) | Native Windows (no WSL) |
| **Container Type** | Linux containers | Windows containers |
| **Build Script** | `build.ps1` | `build-windows.ps1` |
| **Dockerfile** | `Dockerfile` (scratch) | `Dockerfile.windows` (nanoserver) |
| **Image Size** | ~15MB | ~300MB |
| **PowerShell** | 7.5.3 | 7.5.1 |

---

## Why Two Dockerfiles?

**Linux Containers (Dev):**
- Base: `scratch` (0 bytes)
- Tiny, fast, secure
- **Requires WSL2** (not available on NHS servers)

**Windows Containers (NHS):**
- Base: `nanoserver:ltsc2022` (~280MB)
- Compatible with Windows Server without WSL
- **Works on NHS infrastructure**

**Same Go source code, same functionality, different packaging!**

---

## Common Gotcha: Docker Mode Switching

### On Development Machine

If you see "no matching manifest" or build errors:

```powershell
# Check current mode
docker version --format '{{.Server.Os}}'

# If shows "windows" but you want Linux:
# Right-click Docker Desktop tray icon → "Switch to Linux containers"

# If shows "linux" but you want Windows (for NHS testing):
# Right-click Docker Desktop tray icon → "Switch to Windows containers"
```

### On NHS Servers

- **Always in Windows containers mode** (can't switch, no WSL available)
- Only use `Dockerfile.windows` and `build-windows.ps1`

---

## Step-by-Step: Development to NHS

### Step 1: Development (Your Machine)

```powershell
cd C:\Dev\win_repos\forkerDotNet\src\Forker.Console

# Ensure Docker in Linux containers mode
.\build.ps1 -Docker -Run

# Test locally
Start-Process http://localhost:5000

# Stop when done
.\build.ps1 -Stop
```

### Step 2: NHS Build (Your Machine)

```powershell
# Switch Docker Desktop to Windows containers
# Right-click tray → "Switch to Windows containers"

# Build Windows container and create deployment package
.\build-windows.ps1 -Package

# Result: forker-console-windows-YYYYMMDD-HHMMSS.zip
```

### Step 3: Transfer

```powershell
# Copy to NHS server
Copy-Item "forker-console-windows-*.zip" "\\nhs-server\C$\Temp\"

# Or use SCP if configured
scp forker-console-windows-*.zip nhs-admin@nhs-server:C:\Temp\
```

### Step 4: NHS Deployment

```powershell
# On NHS server (as Administrator)
cd C:\Temp

# Extract package
Expand-Archive forker-console-windows-20251008-143022.zip

cd forker-console-windows-20251008-143022

# Load Docker image
docker load -i forker-console-windows-*.tar

# Verify image
docker images forker-console

# Run container
docker run -d `
    --name forker-console `
    -p 127.0.0.1:5000:5000 `
    -v C:\ForkerDemo\forker.db:C:\data\forker.db:ro `
    -e "FORKER_DB_PATH=C:\data\forker.db" `
    --restart unless-stopped `
    forker-console:latest

# Test
Start-Sleep -Seconds 5
Invoke-RestMethod http://localhost:5000/health

# Open dashboard
Start-Process http://localhost:5000
```

---

## Troubleshooting

### "This image's platform (linux/amd64) does not match the detected host platform"

**Problem:** Trying to run Linux container on Windows Docker (or vice versa)

**Solution:** Use correct build script:
- Dev (WSL): `build.ps1`
- NHS (no WSL): `build-windows.ps1`

### "Failed to compute cache key: not found"

**Problem:** Docker can't download base image (network issue)

**Solution:**
```powershell
# Pre-pull base image
docker pull mcr.microsoft.com/windows/nanoserver:ltsc2022

# Then retry build
.\build-windows.ps1
```

### "Access is denied" when mounting database

**Problem:** Container can't read database file

**Solution:**
```powershell
# On NHS server, grant read access
icacls C:\ForkerDemo\forker.db /grant "Everyone:(R)"

# Restart container
docker restart forker-console
```

---

## Complete File List

```
src/Forker.Console/
├── Dockerfile                   # Linux container (dev)
├── Dockerfile.windows           # Windows container (NHS)
├── docker-compose.yml           # Linux compose (dev)
├── docker-compose.windows.yml   # Windows compose (NHS)
├── build.ps1                    # Linux build script (dev)
├── build-windows.ps1            # Windows build script (NHS)
├── README.md                    # General readme
├── README-DEPLOYMENT.md         # This file
├── DEPLOYMENT.md                # Detailed deployment guide
├── DEPLOYMENT-DOCKER.md         # Docker-specific deployment
└── ... (source code, etc.)
```

---

## Quick Commands Cheat Sheet

### Development
```powershell
# Build and run (Linux)
.\build.ps1 -Docker -Run

# Stop
.\build.ps1 -Stop

# Logs
docker-compose logs -f
```

### NHS Packaging
```powershell
# Switch to Windows containers (Docker Desktop)
# Right-click tray → Switch to Windows containers

# Build and package
.\build-windows.ps1 -Package

# Test locally (optional)
.\build-windows.ps1 -Test
```

### NHS Server
```powershell
# Load and run
docker load -i forker-console-windows-*.tar
docker run -d --name forker-console -p 127.0.0.1:5000:5000 -v C:\ForkerDemo\forker.db:C:\data\forker.db:ro forker-console:latest

# Manage
docker logs forker-console -f       # View logs
docker stop forker-console          # Stop
docker start forker-console         # Start
docker restart forker-console       # Restart
docker rm -f forker-console         # Remove
```

---

## Contact

Questions about deployment? See:
- [DEPLOYMENT-DOCKER.md](DEPLOYMENT-DOCKER.md) - Comprehensive Docker guide
- [DEPLOYMENT.md](DEPLOYMENT.md) - Native Windows deployment (alternative)
- [TESTING.md](TESTING.md) - Testing procedures
