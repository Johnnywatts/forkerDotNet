# ForkerDotNet Console - Deployment Guide

## Deployment Scenarios

### Scenario 1: Development (Docker + WSL) ✅ Current
- **Platform:** Windows 10/11 with Docker Desktop + WSL2
- **Use Case:** Local development and testing
- **Deployment:** Docker container (Linux-based)

### Scenario 2: NHS Servers (Native Windows) ✅ Production
- **Platform:** Windows Server 2019/2022 (no Docker, no WSL)
- **Use Case:** Test and production environments
- **Deployment:** Native Windows executable + NSSM service

---

## Deployment Path 1: Docker (Development)

**Prerequisites:**
- Docker Desktop with WSL2 backend
- PowerShell 7.5+

**Quick Start:**
```powershell
cd C:\Dev\win_repos\forkerDotNet\src\Forker.Console
.\build.ps1 -Docker -Run

# Access at http://localhost:5000
```

**Details:** See [TESTING.md](TESTING.md)

---

## Deployment Path 2: Native Windows (NHS Servers)

### Build Process

#### Step 1: Cross-Compile for Windows

**On Development Machine (WSL/Linux available):**

```powershell
# Build-Windows.ps1
cd C:\Dev\win_repos\forkerDotNet\src\Forker.Console

# Cross-compile for Windows
docker run --rm `
  -v ${PWD}:/build `
  -w /build `
  golang:1.23-alpine `
  sh -c "go mod download && GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -ldflags='-s -w' -o console.exe ./cmd/console"

Write-Host "✓ Windows executable built: console.exe" -ForegroundColor Green
```

**Result:** `console.exe` (~12MB static binary)

#### Step 2: Package for Deployment

```powershell
# Create deployment package
$version = "1.0.0"
$packageName = "forker-console-$version-windows"

# Create package directory
New-Item -ItemType Directory -Path ".\dist\$packageName" -Force

# Copy files
Copy-Item "console.exe" ".\dist\$packageName\"
Copy-Item "web" ".\dist\$packageName\web" -Recurse
Copy-Item "deploy\*" ".\dist\$packageName\" -Recurse

# Create ZIP
Compress-Archive -Path ".\dist\$packageName\*" -DestinationPath ".\dist\$packageName.zip" -Force

Write-Host "✓ Deployment package created: dist\$packageName.zip" -ForegroundColor Green
```

**Package Contents:**
```
forker-console-1.0.0-windows.zip
├── console.exe              (12MB binary)
├── web/
│   └── static/style.css
├── install.ps1              (Installation script)
├── uninstall.ps1            (Uninstall script)
├── start.ps1                (Start service)
├── stop.ps1                 (Stop service)
└── config.json              (Configuration)
```

### Installation on NHS Servers

#### Prerequisites on Target Server

1. **PowerShell 7.5.1+** ✅ Already installed
2. **NSSM** (Non-Sucking Service Manager) - Download from https://nssm.cc/
3. **Network access** to ForkerDemo folder (local or UNC path)

#### Installation Steps

**Step 1: Transfer Package**

```powershell
# On development machine
scp dist\forker-console-1.0.0-windows.zip nhs-server:C:\Temp\

# OR copy via file share
Copy-Item "dist\forker-console-1.0.0-windows.zip" "\\nhs-server\C$\Temp\"
```

**Step 2: Extract and Install**

```powershell
# On NHS server (as Administrator)
cd C:\Temp

# Extract package
Expand-Archive -Path "forker-console-1.0.0-windows.zip" -DestinationPath "C:\Program Files\ForkerConsole"

cd "C:\Program Files\ForkerConsole"

# Run installer
.\install.ps1
```

**The install.ps1 script will:**
1. Verify prerequisites (PowerShell version, NSSM)
2. Configure database path
3. Install Windows service using NSSM
4. Configure firewall rule (localhost:5000)
5. Start service
6. Verify health endpoint

### Service Management

```powershell
# Start service
.\start.ps1

# Stop service
.\stop.ps1

# Check status
Get-Service ForkerConsole

# View logs
Get-Content "C:\ProgramData\ForkerConsole\logs\console.log" -Tail 50 -Wait

# Restart service
Restart-Service ForkerConsole
```

### Configuration

**config.json:**
```json
{
  "DatabasePath": "C:\\ForkerDemo\\forker.db",
  "ListenAddress": "localhost:5000",
  "Mode": "production",
  "LogLevel": "info",
  "LogPath": "C:\\ProgramData\\ForkerConsole\\logs\\console.log"
}
```

**Environment Variables:**
```powershell
# Set via NSSM or system environment
$env:FORKER_DB_PATH = "C:\ForkerDemo\forker.db"
$env:FORKER_MODE = "production"
```

---

## Deployment Scripts

### install.ps1

```powershell
#Requires -RunAsAdministrator
param(
    [string]$InstallPath = "C:\Program Files\ForkerConsole",
    [string]$DatabasePath = "C:\ForkerDemo\forker.db",
    [string]$ServiceName = "ForkerConsole"
)

$ErrorActionPreference = "Stop"

Write-Host "ForkerDotNet Console - Installation" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

# Check prerequisites
Write-Host "[1/7] Checking prerequisites..." -ForegroundColor Yellow

# Check PowerShell version
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Host "✗ PowerShell 7+ required (found $($PSVersionTable.PSVersion))" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ PowerShell $($PSVersionTable.PSVersion)" -ForegroundColor Green

# Check NSSM
if (-not (Get-Command nssm -ErrorAction SilentlyContinue)) {
    Write-Host "✗ NSSM not found. Install from https://nssm.cc/" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ NSSM installed" -ForegroundColor Green

# Check database
if (-not (Test-Path $DatabasePath)) {
    Write-Host "⚠ Database not found at: $DatabasePath" -ForegroundColor Yellow
    Write-Host "  Service will start when database becomes available" -ForegroundColor Gray
}

# Check if service already exists
Write-Host "[2/7] Checking for existing service..." -ForegroundColor Yellow
$existingService = Get-Service $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "  Service already exists. Stopping..." -ForegroundColor Yellow
    Stop-Service $ServiceName -Force
    nssm remove $ServiceName confirm
    Start-Sleep -Seconds 2
}

# Install service
Write-Host "[3/7] Installing Windows service..." -ForegroundColor Yellow
$exePath = Join-Path $InstallPath "console.exe"

nssm install $ServiceName $exePath
nssm set $ServiceName AppDirectory $InstallPath
nssm set $ServiceName DisplayName "ForkerDotNet Console"
nssm set $ServiceName Description "Web-based monitoring console for ForkerDotNet medical imaging replication service"
nssm set $ServiceName Start SERVICE_AUTO_START

# Set environment variables
nssm set $ServiceName AppEnvironmentExtra "FORKER_DB_PATH=$DatabasePath" "FORKER_MODE=production"

# Configure logging
$logPath = "C:\ProgramData\ForkerConsole\logs"
New-Item -ItemType Directory -Path $logPath -Force | Out-Null
nssm set $ServiceName AppStdout "$logPath\console.log"
nssm set $ServiceName AppStderr "$logPath\console-error.log"
nssm set $ServiceName AppStdoutCreationDisposition 4  # Append
nssm set $ServiceName AppStderrCreationDisposition 4  # Append

Write-Host "  ✓ Service installed" -ForegroundColor Green

# Configure firewall
Write-Host "[4/7] Configuring firewall..." -ForegroundColor Yellow
$firewallRule = Get-NetFirewallRule -DisplayName "ForkerDotNet Console" -ErrorAction SilentlyContinue
if ($firewallRule) {
    Remove-NetFirewallRule -DisplayName "ForkerDotNet Console"
}

New-NetFirewallRule `
    -DisplayName "ForkerDotNet Console" `
    -Direction Inbound `
    -LocalPort 5000 `
    -Protocol TCP `
    -Action Allow `
    -Profile Domain,Private | Out-Null

Write-Host "  ✓ Firewall rule created (port 5000)" -ForegroundColor Green

# Create shortcuts
Write-Host "[5/7] Creating shortcuts..." -ForegroundColor Yellow
$shell = New-Object -ComObject WScript.Shell

# Desktop shortcut
$shortcut = $shell.CreateShortcut("$env:PUBLIC\Desktop\ForkerDotNet Console.url")
$shortcut.TargetPath = "http://localhost:5000"
$shortcut.Save()

Write-Host "  ✓ Desktop shortcut created" -ForegroundColor Green

# Start service
Write-Host "[6/7] Starting service..." -ForegroundColor Yellow
Start-Service $ServiceName
Start-Sleep -Seconds 3

# Verify service
$service = Get-Service $ServiceName
if ($service.Status -eq "Running") {
    Write-Host "  ✓ Service running" -ForegroundColor Green
} else {
    Write-Host "  ✗ Service failed to start" -ForegroundColor Red
    Write-Host "  Check logs at: $logPath\console-error.log" -ForegroundColor Yellow
    exit 1
}

# Health check
Write-Host "[7/7] Verifying health endpoint..." -ForegroundColor Yellow
Start-Sleep -Seconds 2

try {
    $response = Invoke-RestMethod "http://localhost:5000/health" -TimeoutSec 5
    if ($response.status -eq "healthy") {
        Write-Host "  ✓ Health check passed" -ForegroundColor Green
    }
} catch {
    Write-Host "  ⚠ Health check failed (service may still be starting)" -ForegroundColor Yellow
    Write-Host "  Wait 10 seconds and check: http://localhost:5000/health" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Console URL:  http://localhost:5000" -ForegroundColor Cyan
Write-Host "Service name: $ServiceName" -ForegroundColor Cyan
Write-Host "Logs:         $logPath" -ForegroundColor Cyan
Write-Host ""
Write-Host "Management commands:" -ForegroundColor Yellow
Write-Host "  Start:   Start-Service $ServiceName" -ForegroundColor Gray
Write-Host "  Stop:    Stop-Service $ServiceName" -ForegroundColor Gray
Write-Host "  Status:  Get-Service $ServiceName" -ForegroundColor Gray
Write-Host "  Logs:    Get-Content '$logPath\console.log' -Tail 50 -Wait" -ForegroundColor Gray
```

### uninstall.ps1

```powershell
#Requires -RunAsAdministrator
param(
    [string]$ServiceName = "ForkerConsole"
)

$ErrorActionPreference = "Stop"

Write-Host "ForkerDotNet Console - Uninstallation" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Stop service
Write-Host "[1/4] Stopping service..." -ForegroundColor Yellow
$service = Get-Service $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq "Running") {
        Stop-Service $ServiceName -Force
        Write-Host "  ✓ Service stopped" -ForegroundColor Green
    }
} else {
    Write-Host "  ℹ Service not found" -ForegroundColor Gray
}

# Remove service
Write-Host "[2/4] Removing service..." -ForegroundColor Yellow
if (Get-Command nssm -ErrorAction SilentlyContinue) {
    nssm remove $ServiceName confirm
    Write-Host "  ✓ Service removed" -ForegroundColor Green
} else {
    Write-Host "  ⚠ NSSM not found, skipping service removal" -ForegroundColor Yellow
}

# Remove firewall rule
Write-Host "[3/4] Removing firewall rule..." -ForegroundColor Yellow
$rule = Get-NetFirewallRule -DisplayName "ForkerDotNet Console" -ErrorAction SilentlyContinue
if ($rule) {
    Remove-NetFirewallRule -DisplayName "ForkerDotNet Console"
    Write-Host "  ✓ Firewall rule removed" -ForegroundColor Green
} else {
    Write-Host "  ℹ Firewall rule not found" -ForegroundColor Gray
}

# Remove shortcuts
Write-Host "[4/4] Removing shortcuts..." -ForegroundColor Yellow
$shortcut = "$env:PUBLIC\Desktop\ForkerDotNet Console.url"
if (Test-Path $shortcut) {
    Remove-Item $shortcut -Force
    Write-Host "  ✓ Shortcut removed" -ForegroundColor Green
}

Write-Host ""
Write-Host "Uninstallation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Note: Installation files remain at:" -ForegroundColor Yellow
Write-Host "  C:\Program Files\ForkerConsole" -ForegroundColor Gray
Write-Host "  C:\ProgramData\ForkerConsole\logs" -ForegroundColor Gray
Write-Host ""
Write-Host "Delete manually if no longer needed." -ForegroundColor Gray
```

### start.ps1

```powershell
#Requires -RunAsAdministrator
Start-Service ForkerConsole
Write-Host "✓ ForkerConsole service started" -ForegroundColor Green
Write-Host "Access at: http://localhost:5000" -ForegroundColor Cyan
```

### stop.ps1

```powershell
#Requires -RunAsAdministrator
Stop-Service ForkerConsole -Force
Write-Host "✓ ForkerConsole service stopped" -ForegroundColor Green
```

---

## Comparison: Docker vs Native Windows

| Feature | Docker (Dev) | Native Windows (NHS) |
|---------|-------------|----------------------|
| **Platform** | WSL2 + Docker Desktop | Windows Server only |
| **Deployment** | `docker-compose up` | NSSM Windows service |
| **Size** | 15MB container | 12MB .exe + 1MB assets |
| **Startup** | ~5 seconds | ~2 seconds |
| **Memory** | 30-50MB | 25-40MB |
| **Updates** | Pull new image | Copy new .exe |
| **NHS Approval** | Complex (Docker licensing) | Simple (.exe + service) |
| **Logs** | `docker logs` | Windows Event Log + file |
| **Monitoring** | Docker stats | Task Manager / PerfMon |

---

## Production Checklist

### Before Deployment

- [ ] Build Windows executable (`console.exe`)
- [ ] Create deployment package ZIP
- [ ] Test on Windows Server 2019/2022 VM
- [ ] Verify database path is accessible
- [ ] Security scan (govulncheck, Windows Defender)
- [ ] Document database path for NHS team

### Deployment Day

- [ ] Transfer ZIP to NHS server
- [ ] Extract to `C:\Program Files\ForkerConsole`
- [ ] Run `install.ps1` as Administrator
- [ ] Verify service started successfully
- [ ] Test health endpoint: `http://localhost:5000/health`
- [ ] Open dashboard in Edge/Chrome
- [ ] Verify database connection (check logs)
- [ ] Create desktop shortcut for users

### Post-Deployment

- [ ] Monitor service for 24 hours
- [ ] Check log files for errors
- [ ] Verify service auto-starts after reboot
- [ ] Document for NHS operations team
- [ ] Schedule first update window

---

## Updating Console

### Development → NHS Server Update Process

**Step 1: Build new version**
```powershell
# On dev machine
cd C:\Dev\win_repos\forkerDotNet\src\Forker.Console
.\Build-Windows.ps1  # Cross-compile for Windows
```

**Step 2: Transfer to NHS server**
```powershell
# Copy only the .exe
scp console.exe nhs-server:"C:\Program Files\ForkerConsole\console.exe.new"
```

**Step 3: Hot-swap on NHS server**
```powershell
# On NHS server (as Administrator)
cd "C:\Program Files\ForkerConsole"

# Stop service
Stop-Service ForkerConsole

# Backup old version
Move-Item console.exe console.exe.backup -Force

# Install new version
Move-Item console.exe.new console.exe -Force

# Start service
Start-Service ForkerConsole

# Verify
Invoke-RestMethod http://localhost:5000/health
```

**Downtime:** ~5 seconds

---

## Troubleshooting

### Service won't start

```powershell
# Check event log
Get-EventLog -LogName Application -Source ForkerConsole -Newest 10

# Check error log
Get-Content "C:\ProgramData\ForkerConsole\logs\console-error.log"

# Test manually
cd "C:\Program Files\ForkerConsole"
.\console.exe  # Run in foreground to see errors
```

### Database connection fails

```powershell
# Verify database path
Test-Path "C:\ForkerDemo\forker.db"

# Check permissions
icacls "C:\ForkerDemo\forker.db"

# Grant read access if needed
icacls "C:\ForkerDemo\forker.db" /grant "NT AUTHORITY\NETWORK SERVICE:(R)"
```

### Port 5000 already in use

```powershell
# Find process using port
Get-NetTCPConnection -LocalPort 5000

# Change console port (edit NSSM service)
nssm set ForkerConsole AppEnvironmentExtra "FORKER_LISTEN_ADDR=localhost:5001"
Restart-Service ForkerConsole
```

---

## Contact

For deployment issues on NHS servers:
- Check logs: `C:\ProgramData\ForkerConsole\logs\console.log`
- Contact ForkerDotNet support team
- Include: OS version, PowerShell version, error logs
