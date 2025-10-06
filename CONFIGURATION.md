# ForkerDotNet Configuration Guide

This guide explains how to configure and run ForkerDotNet in different environments using .NET's standard configuration pattern.

## Overview

ForkerDotNet uses **environment-specific configuration files** to support multiple deployment scenarios without code changes. This is the standard .NET Core/ASP.NET pattern.

## Configuration Files

### Base Configuration (Production)
**File:** [`src/Forker.Service/appsettings.json`](src/Forker.Service/appsettings.json)

This is the **default configuration** used when no environment is specified. It contains production settings:

- **Database:** `C:\ProgramData\ForkerDotNet\forker.db`
- **Directories:** `C:\ProgramData\ForkerDotNet\*`
- **Service Name:** `ForkerDotNet`
- **Logging:** Production-level logging

### Demo Environment
**File:** [`src/Forker.Service/appsettings.Demo.json`](src/Forker.Service/appsettings.Demo.json)

Used for demonstrations and testing. Overrides base configuration with:

- **Database:** `C:\ForkerDemo\forker.db`
- **Directories:** `C:\ForkerDemo\*`
- **Service Name:** `ForkerDotNetDemo`
- **File Filters:** Includes `*.test` files for testing

### SlowDrive Test Environment
**File:** [`src/Forker.Service/appsettings.SlowDrive.json`](src/Forker.Service/appsettings.SlowDrive.json)

Used for performance testing on slow drives. Overrides base configuration with:

- **Database:** `E:\ForkerDotNetTestVolume\forker.db`
- **Directories:** `E:\ForkerDotNetTestVolume\*`
- **Service Name:** `ForkerDotNetSlowDrive`

---

## How Environment Selection Works

.NET automatically loads configuration files in this order:

1. Loads `appsettings.json` (base configuration)
2. **If** `ASPNETCORE_ENVIRONMENT` is set, loads `appsettings.{Environment}.json`
3. Environment-specific settings **override** base settings

### Example

```powershell
# No environment variable = Production (appsettings.json only)
dotnet run
# Uses: C:\ProgramData\ForkerDotNet

# With ASPNETCORE_ENVIRONMENT = Demo
$env:ASPNETCORE_ENVIRONMENT = "Demo"
dotnet run
# Loads: appsettings.json + appsettings.Demo.json
# Uses: C:\ForkerDemo (Demo overrides Production)

# With ASPNETCORE_ENVIRONMENT = SlowDrive
$env:ASPNETCORE_ENVIRONMENT = "SlowDrive"
dotnet run
# Loads: appsettings.json + appsettings.SlowDrive.json
# Uses: E:\ForkerDotNetTestVolume
```

---

## Running ForkerDotNet in Different Environments

### Production (Default)

**Setup:**
```powershell
.\scripts\Production-Setup.ps1
```

**Run as console application:**
```powershell
cd src\Forker.Service
dotnet run
```

**Install as Windows Service:**
```powershell
.\scripts\Install-ForkerService.ps1
Start-Service ForkerDotNet
```

**Database location:** `C:\ProgramData\ForkerDotNet\forker.db`

---

### Demo Environment

**Setup:**
```powershell
.\scripts\Demo-Setup.ps1
```

**Run demo service:**
```powershell
$env:ASPNETCORE_ENVIRONMENT = "Demo"
cd src\Forker.Service
dotnet run
```

**Run demo scenarios:**
```powershell
# In a new terminal
.\scripts\Run-Scenario1-EndToEnd.ps1
```

**Database location:** `C:\ForkerDemo\forker.db`

**Note:** Demo scripts automatically set `ASPNETCORE_ENVIRONMENT="Demo"` internally.

---

### SlowDrive Test Environment

**Setup:**
```powershell
# Ensure E:\ForkerDotNetTestVolume exists
New-Item -Path "E:\ForkerDotNetTestVolume" -ItemType Directory -Force
```

**Run:**
```powershell
$env:ASPNETCORE_ENVIRONMENT = "SlowDrive"
cd src\Forker.Service
dotnet run
```

**Database location:** `E:\ForkerDotNetTestVolume\forker.db`

---

## Database Locations Summary

| Environment | Database Path | Used For |
|------------|---------------|----------|
| **Production** (default) | `C:\ProgramData\ForkerDotNet\forker.db` | Real clinical file processing |
| **Demo** | `C:\ForkerDemo\forker.db` | Demonstrations and testing |
| **SlowDrive** | `E:\ForkerDotNetTestVolume\forker.db` | Performance testing |

---

## Connecting to Database (DataGrip / DB Browser)

### DataGrip (Recommended)
1. File → New → Data Source → SQLite
2. Path: Select the database file for your environment (see table above)
3. Test Connection
4. Click OK

### DB Browser for SQLite
1. Open DB Browser
2. File → Open Database
3. Navigate to database path (see table above)
4. Select `forker.db`

### Key Tables to Monitor

**FileJobs** - Overall job state:
```sql
SELECT JobId, SourcePath, State, DiscoveredAt, CompletedAt
FROM FileJobs
ORDER BY DiscoveredAt DESC;
```

**TargetOutcomes** - Per-target copy/verify status:
```sql
SELECT JobId, TargetId, State, CopyStartedAt, VerifiedAt
FROM TargetOutcomes
ORDER BY CopyStartedAt DESC;
```

**QuarantineEntries** - Corruption detection:
```sql
SELECT * FROM QuarantineEntries;
```

---

## Configuration Settings Reference

### Key Settings in appsettings.json

| Section | Setting | Description |
|---------|---------|-------------|
| **Database** | ConnectionString | SQLite database file path |
| | EnableWalMode | Write-Ahead Logging for crash safety (true) |
| **Directories** | Source | Input directory for file monitoring |
| | TargetA/B | Dual-target replication destinations |
| | Error | Quarantine directory for corrupt files |
| | Processing | Temporary directory for in-progress copies |
| **Monitoring** | FileFilters | File patterns to monitor (*.svs, *.tiff, etc.) |
| | MinimumFileAge | Seconds before file is eligible (stability detection) |
| | StabilityCheckInterval | Seconds between stability checks |
| **Target** | MaxConcurrentCopiesPerTarget | Parallelism per destination (2) |
| | VerifyAfterCopy | Enable SHA-256 verification (true) |
| | CopyBufferSize | Buffer size for copy operations (1MB) |

---

## Adding a New Environment

To add a new environment (e.g., "Staging"):

1. **Create config file:**
   ```
   src/Forker.Service/appsettings.Staging.json
   ```

2. **Add environment-specific settings:**
   ```json
   {
     "ServiceName": "ForkerDotNetStaging",
     "Database": {
       "ConnectionString": "Data Source=C:\\Staging\\forker.db"
     },
     "Directories": {
       "Source": "C:\\Staging\\Input",
       "TargetA": "C:\\Staging\\DestinationA",
       "TargetB": "C:\\Staging\\DestinationB",
       ...
     }
   }
   ```

3. **Run with environment:**
   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = "Staging"
   dotnet run
   ```

---

## Troubleshooting

### Wrong database being used
**Check:**
```powershell
# Verify environment variable
echo $env:ASPNETCORE_ENVIRONMENT

# Expected output:
# - Nothing (blank) = Production
# - "Demo" = Demo environment
# - "SlowDrive" = SlowDrive environment
```

### Configuration not loading
**Check:**
```powershell
# Verify config file exists
Test-Path src\Forker.Service\appsettings.Demo.json

# Check service logs
Get-Content C:\ForkerDemo\Logs\forker-*.txt -Tail 50
```

### Database not found
**Check paths match configuration:**
```powershell
# For Demo:
Test-Path C:\ForkerDemo\forker.db

# For Production:
Test-Path C:\ProgramData\ForkerDotNet\forker.db
```

---

## Best Practices

1. **Keep base config (appsettings.json) as Production**
   - Default behavior should be production-ready
   - Overrides are for special cases (demo, test, staging)

2. **Never commit sensitive credentials**
   - Use environment variables for passwords/API keys
   - Use User Secrets for development

3. **Document environment-specific behavior**
   - Update this file when adding new environments
   - Note any environment-specific quirks

4. **Test configuration loading**
   - Verify correct database path on startup
   - Check log file location matches expectation

---

## Quick Reference

### Run in Production (Default)
```powershell
dotnet run --project src\Forker.Service
```

### Run in Demo
```powershell
$env:ASPNETCORE_ENVIRONMENT="Demo"; dotnet run --project src\Forker.Service
```

### Run in SlowDrive
```powershell
$env:ASPNETCORE_ENVIRONMENT="SlowDrive"; dotnet run --project src\Forker.Service
```

### Check Current Configuration
```powershell
# Database path is logged on startup:
Get-Content C:\ForkerDemo\Logs\forker-*.txt | Select-String "Database"
```
