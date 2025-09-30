# ForkerDotNet Windows Service Deployment Guide

## Overview

ForkerDotNet is designed to run as a Windows Service for production deployment. This guide covers installation, configuration, management, and troubleshooting.

## Prerequisites

- **Windows 10/11 or Windows Server 2016+**
- **.NET 8.0 Runtime** (or SDK for development)
- **Administrator privileges** for service installation
- **PowerShell 5.1+** for deployment scripts

## Quick Start (5 Minutes)

### 1. Publish the Service

From the repository root:

```powershell
dotnet publish src\Forker.Service -c Release -r win-x64 --self-contained false -o publish
```

This creates a framework-dependent deployment in the `publish\` directory.

### 2. Install as Windows Service

Run as Administrator:

```powershell
.\scripts\Install-ForkerService.ps1
```

The installation script will:
- ✅ Create the Windows Service named "ForkerDotNet"
- ✅ Configure automatic startup
- ✅ Set up crash recovery (automatic restart on failure)
- ✅ Configure service description

### 3. Start the Service

```powershell
Start-Service -Name ForkerDotNet
```

### 4. Verify Operation

```powershell
# Check service status
Get-Service -Name ForkerDotNet

# View recent logs
Get-EventLog -LogName Application -Source ForkerDotNet -Newest 20 | Format-Table -AutoSize
```

## Configuration

### Service Configuration File

The service reads configuration from `appsettings.json` in the same directory as the executable:

```
publish\
├── Forker.Service.exe
├── appsettings.json          ← Edit this file
├── forker.db                  ← SQLite database (auto-created)
└── logs\                      ← Serilog file logs (auto-created)
```

### Key Configuration Sections

#### 1. Directory Paths

Edit `appsettings.json` to configure your directory paths:

```json
{
  "Directories": {
    "Source": "C:\\ForkerDotNet\\Input",
    "TargetA": "C:\\ForkerDotNet\\Clinical",
    "TargetB": "C:\\ForkerDotNet\\Research",
    "Error": "C:\\ForkerDotNet\\Error",
    "Processing": "C:\\ForkerDotNet\\Processing"
  }
}
```

**Important:** The service will automatically create these directories on startup if they don't exist.

#### 2. File Monitoring

Configure which file types to monitor:

```json
{
  "Monitoring": {
    "IncludeSubdirectories": false,
    "FileFilters": ["*.svs", "*.tiff", "*.tif", "*.ndpi", "*.scn"],
    "ExcludeExtensions": [".tmp", ".temp", ".part", ".lock"],
    "MinimumFileAge": 5,
    "StabilityCheckInterval": 2,
    "MaxStabilityChecks": 10
  }
}
```

#### 3. Target Configuration

Configure dual-target replication:

```json
{
  "Target": {
    "Targets": {
      "TargetA": {
        "Id": "TargetA",
        "Path": "C:\\ForkerDotNet\\Clinical",
        "Enabled": true,
        "Description": "Clinical target for medical imaging files",
        "Priority": 1,
        "VerifyAfterCopy": true
      },
      "TargetB": {
        "Id": "TargetB",
        "Path": "C:\\ForkerDotNet\\Research",
        "Enabled": true,
        "Description": "Research target for medical imaging files",
        "Priority": 2,
        "VerifyAfterCopy": true
      }
    },
    "MaxConcurrentCopiesPerTarget": 2,
    "MaxRetryAttempts": 3,
    "RetryDelayMilliseconds": 5000,
    "CopyBufferSize": 1048576,
    "ParallelCopyEnabled": true
  }
}
```

#### 4. Logging Configuration

Configure Serilog logging levels and outputs:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/forker-.txt",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ]
  }
}
```

## Service Management

### Basic Commands

```powershell
# Check service status
Get-Service -Name ForkerDotNet

# Start service
Start-Service -Name ForkerDotNet

# Stop service
Stop-Service -Name ForkerDotNet

# Restart service
Restart-Service -Name ForkerDotNet

# View service details
Get-Service -Name ForkerDotNet | Format-List *
```

### View Logs

#### Application Event Log

The service logs to the Windows Application Event Log:

```powershell
# View recent logs
Get-EventLog -LogName Application -Source ForkerDotNet -Newest 20

# View error logs only
Get-EventLog -LogName Application -Source ForkerDotNet -EntryType Error -Newest 10

# Follow logs in real-time (PowerShell 7+)
Get-EventLog -LogName Application -Source ForkerDotNet -Newest 1 | Out-Default;
while ($true) {
    Start-Sleep -Seconds 2;
    Get-EventLog -LogName Application -Source ForkerDotNet -After (Get-Date).AddSeconds(-2)
}
```

#### File Logs

The service also writes to file logs in the `logs\` directory:

```powershell
# View today's log
Get-Content publish\logs\forker-20250930.txt -Tail 50 -Wait

# Search for errors
Select-String -Path publish\logs\*.txt -Pattern "ERR|FTL" | Select-Object -Last 20
```

### Monitoring Health Endpoint

The service exposes a health endpoint on port 8080:

```powershell
# Check health status
Invoke-WebRequest -Uri http://localhost:8080/health/live | Select-Object StatusCode, Content
```

Expected response:
```
StatusCode: 200
Content: Healthy
```

## Crash Recovery & Automatic Restart

The installation script configures automatic crash recovery:

| Failure Count | Action | Delay |
|--------------|--------|-------|
| 1st failure | Restart service | 5 seconds |
| 2nd failure | Restart service | 10 seconds |
| 3rd+ failures | Restart service | 30 seconds |
| Reset counter | After 24 hours | - |

### Verify Recovery Configuration

```powershell
sc.exe qfailure ForkerDotNet
```

### Modify Recovery Actions

```powershell
# Example: Restart after 15 seconds on all failures
sc.exe failure ForkerDotNet reset= 86400 actions= restart/15000/restart/15000/restart/15000
```

## Production Deployment Checklist

### Pre-Deployment

- [ ] **Test in development environment** - Run service interactively first
- [ ] **Verify .NET 8 Runtime** - `dotnet --version` should show 8.0.x
- [ ] **Check directory permissions** - Service account needs read/write access to all configured directories
- [ ] **Review configuration** - Validate all paths in `appsettings.json`
- [ ] **Database location** - Ensure `forker.db` directory is writable
- [ ] **Network paths** - If using UNC paths, test accessibility

### Installation

```powershell
# 1. Publish the service
dotnet publish src\Forker.Service -c Release -r win-x64 --self-contained false -o C:\ForkerDotNet\Service

# 2. Copy configuration
Copy-Item src\Forker.Service\appsettings.json C:\ForkerDotNet\Service\appsettings.json

# 3. Edit configuration for production paths
notepad C:\ForkerDotNet\Service\appsettings.json

# 4. Install as service
.\scripts\Install-ForkerService.ps1 -ServicePath "C:\ForkerDotNet\Service\Forker.Service.exe"

# 5. Start service
Start-Service -Name ForkerDotNet

# 6. Verify startup
Start-Sleep -Seconds 5
Get-Service -Name ForkerDotNet
Get-EventLog -LogName Application -Source ForkerDotNet -Newest 10
```

### Post-Deployment

- [ ] **Verify service is running** - `Get-Service -Name ForkerDotNet`
- [ ] **Check logs for errors** - `Get-EventLog -LogName Application -Source ForkerDotNet -Newest 20`
- [ ] **Test health endpoint** - `Invoke-WebRequest http://localhost:8080/health/live`
- [ ] **Drop test file** - Place a test file in the Input directory
- [ ] **Verify file processing** - Confirm file appears in Clinical and Research directories
- [ ] **Check Input cleanup** - Verify Input file is removed after successful processing
- [ ] **Monitor for 24 hours** - Ensure stable operation
- [ ] **Document deployment** - Record configuration and deployment date

## Troubleshooting

### Service Won't Start

**Symptom:** Service shows status "Starting..." then returns to "Stopped"

**Solutions:**

1. **Check Event Log for errors:**
   ```powershell
   Get-EventLog -LogName Application -Source ForkerDotNet -EntryType Error -Newest 5
   ```

2. **Run interactively to see errors:**
   ```powershell
   cd C:\ForkerDotNet\Service
   .\Forker.Service.exe
   ```

3. **Common Issues:**
   - **Missing .NET 8 Runtime:** Install from https://dotnet.microsoft.com/download/dotnet/8.0
   - **Configuration errors:** Validate JSON syntax in `appsettings.json`
   - **Permission issues:** Service account can't access configured directories
   - **Port conflict:** Port 8080 already in use (check with `netstat -ano | findstr :8080`)

### Service Starts but Doesn't Process Files

1. **Check file monitoring configuration:**
   ```powershell
   # Verify directories exist
   Test-Path C:\ForkerDotNet\Input
   Test-Path C:\ForkerDotNet\Clinical
   Test-Path C:\ForkerDotNet\Research
   ```

2. **Check file patterns:**
   - Ensure file extension matches `FileFilters` in configuration
   - Verify file is not excluded by `ExcludeExtensions`

3. **Check stability detection:**
   - Files must be stable (not growing) for `MinimumFileAge` seconds
   - Check logs for "File is still growing" messages

4. **Query SQLite database:**
   ```powershell
   # Install SQLite command-line tool or use DB Browser for SQLite
   # Check job status:
   sqlite3 C:\ForkerDotNet\Service\forker.db "SELECT * FROM FileJobs ORDER BY CreatedAt DESC LIMIT 10;"
   ```

### High Memory Usage

**Expected memory usage:** <100MB for normal operation

If memory usage is high:

1. **Check concurrent operations:**
   ```json
   "MaxConcurrentCopiesPerTarget": 1  // Reduce from 2
   ```

2. **Check for stuck jobs:**
   ```powershell
   # Query database for long-running jobs
   sqlite3 forker.db "SELECT SourcePath, State, CreatedAt FROM FileJobs WHERE State != 'Verified';"
   ```

3. **Restart service:**
   ```powershell
   Restart-Service -Name ForkerDotNet
   ```

### Files Not Being Removed from Input

**Symptom:** Files copied to Clinical and Research, but remain in Input

**Cause:** Verification may have failed or service crashed before cleanup

**Solutions:**

1. **Check verification status:**
   ```powershell
   # Look for hash mismatch or verification errors in logs
   Select-String -Path C:\ForkerDotNet\Service\logs\*.txt -Pattern "verification|quarantine|hash"
   ```

2. **Check database state:**
   ```powershell
   sqlite3 forker.db "SELECT SourcePath, State FROM FileJobs WHERE State != 'Verified';"
   ```

3. **Manual cleanup:** If files are verified in database but still in Input, you can safely delete them

## Uninstallation

To remove the ForkerDotNet service:

```powershell
# Stop and uninstall service
.\scripts\Uninstall-ForkerService.ps1

# Optional: Remove service files
Remove-Item -Path C:\ForkerDotNet\Service -Recurse -Force

# Optional: Remove data directories (WARNING: Deletes all data!)
# Remove-Item -Path C:\ForkerDotNet -Recurse -Force
```

## Advanced Configuration

### Running as Specific User Account

By default, the service runs as `LocalSystem`. To run as a specific user:

```powershell
# After installation, configure service account
sc.exe config ForkerDotNet obj= "DOMAIN\ServiceAccount" password= "P@ssw0rd"
```

**Note:** The service account must have:
- Read/write access to all configured directories
- Read/write access to the service installation directory (for SQLite database and logs)

### Network Paths / UNC Shares

If using network paths, configure the service to run as a domain account with network access:

```json
{
  "Directories": {
    "Source": "\\\\FileServer\\PathologyScans\\Input",
    "TargetA": "\\\\FileServer\\Clinical\\Images",
    "TargetB": "\\\\FileServer\\Research\\Archives"
  }
}
```

### Performance Tuning

For high-throughput environments:

```json
{
  "Target": {
    "MaxConcurrentCopiesPerTarget": 4,  // Increase concurrent operations
    "CopyBufferSize": 2097152,          // 2MB buffer for large files
    "ParallelCopyEnabled": true
  },
  "Monitoring": {
    "MinimumFileAge": 2,                // Reduce stability wait time
    "StabilityCheckInterval": 1
  }
}
```

**Warning:** Higher concurrency increases memory usage. Monitor system resources.

## Security Considerations

### Least Privilege

1. **Create dedicated service account:**
   ```powershell
   New-LocalUser -Name "ForkerService" -Description "ForkerDotNet Service Account" -NoPassword
   ```

2. **Grant minimum required permissions:**
   - Read access to Input directory
   - Write access to Clinical, Research, Error, Processing directories
   - Read/write access to service installation directory (for database)

3. **Configure service to use account:**
   ```powershell
   sc.exe config ForkerDotNet obj= ".\ForkerService" password= ""
   ```

### FIPS Compliance

ForkerDotNet uses SHA-256 for file verification, which is FIPS 140-2 compliant.

To enable FIPS mode in Windows:
- **Do not enable system-wide FIPS mode** - This can cause .NET cryptography issues
- SHA-256 is already FIPS-compliant by default

### Audit Trail

All file operations are logged to:
1. **SQLite database** - Complete audit trail with timestamps
2. **Serilog file logs** - Structured logs with correlation IDs
3. **Windows Event Log** - High-level service events

## Support & Monitoring

### Monitoring Checklist

Monitor these metrics in production:

- [ ] **Service uptime** - `(Get-Service ForkerDotNet).Status`
- [ ] **Health endpoint** - `Invoke-WebRequest http://localhost:8080/health/live`
- [ ] **Input queue size** - Count files in Input directory
- [ ] **Processing rate** - Files processed per hour
- [ ] **Error rate** - Count of files in Error directory
- [ ] **Database size** - Monitor `forker.db` growth
- [ ] **Log file size** - Monitor logs directory growth
- [ ] **Memory usage** - Should stay <100MB
- [ ] **Disk space** - All configured directories

### Integration with Monitoring Systems

The health endpoint can be integrated with monitoring tools:

```powershell
# Example: Zabbix monitoring
Invoke-WebRequest -Uri http://localhost:8080/health/live -UseBasicParsing |
    Select-Object -ExpandProperty StatusCode

# Example: Prometheus (future enhancement - metrics endpoint coming)
# Invoke-WebRequest -Uri http://localhost:8080/metrics
```

## Additional Resources

- **GitHub Repository:** https://github.com/your-org/forkerDotNet
- **Issue Tracker:** https://github.com/your-org/forkerDotNet/issues
- **Architecture Documentation:** [dotNetRebuild.md](../dotNetRebuild.md)
- **Development Plan:** [dev_plan.md](../dev_plan.md)
- **Security Documentation:** [security-requirements.md](../security-requirements.md)

## Change Log

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2025-09-30 | Initial Windows Service deployment |
