# Production-Strength Demo Guide

This guide walks through a **production-realistic** demonstration of ForkerDotNet running as a Windows Service with full visual monitoring.

## Overview

- **Target Audience**: Stakeholders, operations teams, production readiness validation
- **Duration**: 30 minutes
- **Requirements**: Windows 10/11/Server, Administrator privileges
- **What's Different**: Runs as actual Windows Service (not console), production configuration

## Prerequisites

### Software Requirements
- .NET 8 SDK installed
- DB Browser for SQLite (download from https://sqlitebrowser.org/)
- Administrator PowerShell access

### Directory Structure
The demo uses `C:\ForkerDemo\` with production-like layout:
```
C:\ForkerDemo\
├── source\          # Drop files here (simulates medical imaging scanner output)
├── targetA\         # Primary replication target
├── targetB\         # Secondary replication target
├── quarantine\      # Hash mismatch isolation
├── logs\            # Structured logs (Serilog)
└── forker.db        # SQLite persistence (WAL mode)
```

## Step 1: Build Production Release

Open PowerShell (no admin needed yet):

```powershell
cd C:\Dev\win_repos\forkerDotNet

# Clean build
dotnet clean --configuration Release
dotnet build --configuration Release

# Verify all tests pass (249 tests)
dotnet test --configuration Release
```

**Expected Output**:
- Build succeeded
- 249 tests passed (0 failed)

## Step 2: Setup Demo Environment

```powershell
cd demo\scripts
.\Demo-Setup.ps1
```

**What This Does**:
- Creates `C:\ForkerDemo\` directory structure
- Cleans any previous test data
- Copies production configuration
- Initializes SQLite database

**Expected Output**:
```
✓ Created C:\ForkerDemo\source
✓ Created C:\ForkerDemo\targetA
✓ Created C:\ForkerDemo\targetB
✓ Created C:\ForkerDemo\quarantine
✓ Created C:\ForkerDemo\logs
✓ Demo environment ready
```

## Step 3: Install as Windows Service (REQUIRES ADMIN)

Open **Administrator PowerShell**:

```powershell
cd C:\Dev\win_repos\forkerDotNet\scripts
.\Install-ForkerService.ps1 -Environment Demo
```

**What This Does**:
- Reads `appsettings.Demo.json` to extract service metadata (name, display name, description)
- Registers Windows Service using config-driven properties
- Configures automatic startup and crash recovery
- Prompts to start the service

**Expected Output**:
```
ℹ Reading configuration from: appsettings.Demo.json

Service Configuration (from appsettings.Demo.json):
  Name: ForkerDotNetDemo
  Display Name: ForkerDotNet Demo Service
  Description: Demo service for ForkerDotNet clinical file processing demonstrations
  Environment: Demo

✓ Service installed successfully
✓ Service started successfully
```

**Verify Service Running**:
```powershell
Get-Service ForkerDotNetDemo
```

Expected status: **Running**

**How It Works**: The script reads ALL service properties from the config file - no parameters needed! Change environment = change config file.

## Step 4: Open Visual Monitoring Tools

Before running scenarios, open these windows side-by-side:

### 4.1 File Explorer (3 windows)
- Window 1: `C:\ForkerDemo\source` (View → Details, add Date Modified column)
- Window 2: `C:\ForkerDemo\targetA` (View → Details)
- Window 3: `C:\ForkerDemo\targetB` (View → Details)

### 4.2 DB Browser for SQLite
1. Open DB Browser for SQLite
2. File → Open Database → `C:\ForkerDemo\forker.db`
3. Browse Data tab → Select `FileJobs` table
4. Keep window visible for real-time state changes

### 4.3 Log Monitoring (Optional)
```powershell
# PowerShell window
Get-Content C:\ForkerDemo\logs\forker-*.log -Wait -Tail 20
```

## Step 5: Run Scenario 1 - End-to-End Success

**Scenario**: Copy 5 medical imaging files, verify dual-target replication, confirm hash verification.

### 5.1 Start Scenario
```powershell
cd C:\Dev\win_repos\forkerDotNet\demo\scripts
.\Run-Scenario1-EndToEnd.ps1
```

### 5.2 What to Watch

**File Explorer (source)**:
- 5 files appear (slide001.svs through slide005.svs)
- Sizes: 10MB, 50MB, 100MB, 250MB, 500MB

**File Explorer (targetA & targetB)**:
- Files copy simultaneously to both targets
- Watch file sizes grow during copy operation

**DB Browser (FileJobs table)**:
- Click "Refresh" button periodically
- Watch `State` column: DISCOVERED → QUEUED → IN_PROGRESS → PARTIAL → VERIFIED
- `VerificationCompletedUtc` timestamps populate when done

**DB Browser (TargetOutcomes table)**:
- Switch to TargetOutcomes table
- Watch `CopyState` per target: PENDING → COPYING → COPIED → VERIFYING → VERIFIED
- `BytesCopied` increments during copy
- `VerifiedHashSHA256` populates after verification

### 5.3 Expected Timeline
- **0-30s**: File discovery, queuing, copy start
- **30-120s**: Parallel copy to both targets (depends on disk speed)
- **120-180s**: Hash verification (SHA-256 on all files)
- **180s**: All jobs VERIFIED, TargetOutcomes VERIFIED

### 5.4 Success Criteria
✓ All 5 files in targetA and targetB (exact sizes)
✓ All FileJobs in VERIFIED state
✓ All TargetOutcomes in VERIFIED state
✓ No files in quarantine folder
✓ Logs show no errors

## Step 6: Run Scenario 2 - Corruption Detection

**Scenario**: Introduce hash mismatch, verify quarantine behavior.

### 6.1 Start Scenario
```powershell
.\Run-Scenario2-Corruption.ps1
```

### 6.2 What to Watch

**File Explorer (source)**:
- `corrupted-slide.svs` appears

**File Explorer (targetA or targetB)**:
- File copies normally initially

**DB Browser (FileJobs)**:
- Job reaches PARTIAL (copy complete)
- **IMPORTANT**: Watch for state change to **QUARANTINED** (not VERIFIED!)

**File Explorer (quarantine)**:
- Corrupted file moves to `C:\ForkerDemo\quarantine\`
- Original filename preserved with timestamp

**Logs**:
```
[ERR] Hash mismatch detected for JobId=X
[WRN] File quarantined: corrupted-slide.svs
```

### 6.3 Success Criteria
✓ Job state: **QUARANTINED** (not VERIFIED!)
✓ Corrupted file moved to quarantine folder
✓ Targets cleaned up (file removed if copied)
✓ Alert logged with hash details

## Step 7: Run Scenario 3 - Automatic Cleanup

**Scenario**: Service auto-deletes source files after successful verification.

```powershell
.\Run-Scenario3-AutoCleanup.ps1
```

### 7.1 What to Watch

**File Explorer (source)**:
- Files appear
- **After VERIFIED state**: Files disappear from source (auto-deleted)

**File Explorer (targets)**:
- Files remain in targetA and targetB (never deleted)

**DB Browser**:
- `SourceDeletedUtc` timestamp populates
- Job state remains VERIFIED

### 7.2 Success Criteria
✓ Source files deleted after verification
✓ Target files preserved
✓ Database records `SourceDeletedUtc` timestamp
✓ No errors in logs

## Step 8: Stop and Uninstall Service

When demo is complete:

### 8.1 Stop Service
```powershell
# Administrator PowerShell
Stop-Service ForkerDotNetDemo
```

### 8.2 Uninstall Service (Optional)
```powershell
# Administrator PowerShell
cd C:\Dev\win_repos\forkerDotNet\scripts
.\Install-ForkerService.ps1 -Environment Demo -Uninstall
```

**Note**: The script reads `appsettings.Demo.json` to get the service name, ensuring the correct service is uninstalled.

### 8.3 Preserve or Clean Demo Data

**Preserve** (for analysis):
```powershell
# Database and logs remain in C:\ForkerDemo\
```

**Clean** (start fresh):
```powershell
.\Demo-Setup.ps1  # Re-runs cleanup
```

## Troubleshooting

### Service Won't Start

**Check logs**:
```powershell
Get-Content C:\ForkerDemo\logs\forker-*.log -Tail 50
```

**Common Issues**:
- Port 5000 already in use → Check `netstat -ano | findstr :5000`
- Permissions on C:\ForkerDemo → Ensure service account has write access
- Configuration errors → Verify `appsettings.json` paths

### Files Not Copying

**Check service status**:
```powershell
Get-Service ForkerDotNet
```

**Check database**:
- Open `forker.db` in DB Browser
- Check `FileJobs` table for entries
- Look for State = FAILED with ErrorMessage

**Check file watcher**:
- Verify `C:\ForkerDemo\source` exists
- Verify files match pattern `*.svs` (or configured extensions)

### Hash Verification Slow

**Expected performance**:
- 1GB/min throughput per target
- 500MB file ≈ 30-60 seconds total (copy + verify both targets)

**If slower**:
- Check disk performance (HDD vs SSD)
- Check antivirus exclusions for `C:\ForkerDemo\`
- Check system load (CPU, memory)

## Next Steps

After completing this demo:

1. **Review Architecture**: See [README.md](../README.md) for domain model details
2. **Production Deployment**: See [deployment-guide.md](deployment-guide.md) for NHS/clinical environments
3. **Security Hardening**: See [security-hardening.md](security-hardening.md) for FIPS compliance
4. **Performance Tuning**: See [performance-tuning.md](performance-tuning.md) for large file optimization

## Key Observations for Stakeholders

After running this demo, you should be confident that ForkerDotNet:

✓ **Reliability**: Runs as proper Windows Service with automatic restart
✓ **Data Integrity**: SHA-256 verification catches corruption (Scenario 2)
✓ **Dual Replication**: Atomic operations ensure both targets verified
✓ **Crash Recovery**: SQLite WAL mode preserves state across restarts
✓ **Observability**: Real-time state visible in database and logs
✓ **Production Ready**: Handles large files (500MB+) with proper cleanup

## Performance Expectations

Based on Scenario 1 (910MB total across 5 files):

| Metric | Target | Typical |
|--------|--------|---------|
| Throughput | 1GB/min per target | 74-150 MB/min (disk dependent) |
| Memory Usage | <100MB | ~50-80MB |
| Hash Verification | 500MB/min | Varies by CPU |
| Crash Recovery | <5 seconds | 1-2 seconds |

**Note**: Console-mode tests yesterday achieved 74.6 MB/min on standard HDD. SSD environments should see 2-3x improvement.

---

**Document Version**: 1.0
**Last Updated**: 2025-10-03
**Tested On**: Windows 11, .NET 8.0.404
