# ForkerDotNet Demonstration Guide

**Version**: 2.0
**Date**: 2025-10-06
**Status**: Observable PowerShell Demo System - Production Ready

---

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Quick Start](#quick-start)
4. [Demo Scenarios](#demo-scenarios)
5. [Database Monitoring](#database-monitoring)
6. [Troubleshooting](#troubleshooting)
7. [Evidence Collection](#evidence-collection)

---

## Overview

The ForkerDotNet demonstration system provides **real, observable demonstrations** using actual Windows tools and PowerShell scripts. These demonstrations prove system safety and reliability for clinical governance stakeholders.

### Key Principle: No Fakes

Unlike typical demos, ForkerDotNet demonstrations use:
- ✅ **Real file operations** (actual copy, hash, delete)
- ✅ **Real Windows File Explorer** (see files appear in real-time)
- ✅ **Real database** (SQLite with DataGrip monitoring)
- ✅ **Real PowerShell Get-FileHash** (cryptographic verification)
- ✅ **Real service crashes** (for recovery testing)

**No progress bars, no hardcoded hashes, no simulations.**

### What Gets Demonstrated

All demonstrations validate these **8 core clinical requirements**:

1. ✅ **Input Directory Monitoring** - File watching with stability detection
2. ✅ **Dual-Target Copy Operations** - Simultaneous replication to two destinations
3. ✅ **Input Directory Cleanup** - Only after BOTH targets verified
4. ✅ **Non-Locking Behavior** - External systems can access files during operations
5. ✅ **OS-Level File Copy** - Streaming operations, not in-memory
6. ✅ **Parallel Copying** - Minimize delay with concurrent target writes
7. ✅ **Clinical Risk Elimination** - SHA-256 verification + quarantine + crash recovery
8. ✅ **Complete Audit Trail** - SQLite state machine with all transitions logged

---

## Prerequisites

### Required Software

1. **.NET 8 Runtime** (8.0.x or later)
   ```powershell
   dotnet --version  # Should show 8.0.x
   ```
   Download: https://dotnet.microsoft.com/download/dotnet/8.0

2. **PowerShell 5.1+** (included with Windows 10/11)
   ```powershell
   $PSVersionTable.PSVersion  # Should show 5.1 or higher
   ```

3. **Administrator privileges** (for crash recovery demo only)

### Optional but Recommended

4. **DataGrip** or **DB Browser for SQLite** (for database monitoring)
   - DataGrip: https://www.jetbrains.com/datagrip/
   - DB Browser: https://sqlitebrowser.org/

### System Requirements

- **OS**: Windows 10/11 or Windows Server 2016+
- **Memory**: 2GB available RAM (service uses <100MB)
- **Disk Space**: 1GB free (for test files)
- **Network**: None required (demos run locally)

---

## Quick Start

### 1. Setup Demo Environment

```powershell
# Run as Administrator
cd c:\Dev\win_repos\forkerDotNet
.\scripts\Demo-Setup.ps1
```

**This creates:**
- `C:\ForkerDemo\` directory structure
- Input, DestinationA, DestinationB, Quarantine folders
- Logs directory

### 2. Start ForkerDotNet Service in Demo Mode

```powershell
# Terminal 1: Start service
$env:ASPNETCORE_ENVIRONMENT = "Demo"
cd src\Forker.Service
dotnet run
```

**Watch for:**
```
[INFO] Forker Service starting...
[INFO] Service configured with name: ForkerDotNetDemo
[INFO] Database initialized: C:\ForkerDemo\forker.db
[INFO] Monitoring directory: C:\ForkerDemo\Input
```

### 3. Run First Demo (Scenario 1: End-to-End)

```powershell
# Terminal 2: Run demo script
cd c:\Dev\win_repos\forkerDotNet
.\scripts\Run-Scenario1-EndToEnd.ps1
```

**This will:**
1. Create a 100MB test medical imaging file
2. Open 3 File Explorer windows (side-by-side view)
3. Move file to Input folder
4. **Watch files appear** in DestinationA and DestinationB
5. Verify SHA-256 hashes with PowerShell
6. Test non-locking behavior

**Duration:** ~3 minutes
**Evidence:** Visual proof in File Explorer + PowerShell hash verification

---

## Demo Scenarios

### Scenario 1: End-to-End File Replication ✅
**File:** `scripts\Run-Scenario1-EndToEnd.ps1`
**Duration:** 3-5 minutes
**Best for:** First-time demonstrations, governance approval

**What it demonstrates:**
- Complete workflow from file drop to verification
- Dual-target replication (both destinations)
- SHA-256 hash verification (PowerShell Get-FileHash)
- Non-locking file operations (external read access)
- SQLite state transitions (DISCOVERED → VERIFIED)

**Run:**
```powershell
.\scripts\Run-Scenario1-EndToEnd.ps1
```

**Optional parameters:**
```powershell
# Larger file for realistic demo
.\scripts\Run-Scenario1-EndToEnd.ps1 -TestFileSize 500
```

---

### Scenario 2: Corruption Detection ⚠️
**File:** `scripts\Run-Scenario2-Corruption.ps1`
**Duration:** 3-4 minutes
**Best for:** Proving data integrity guarantees

**What it demonstrates:**
- SHA-256 hash mismatch detection
- Automatic quarantine behavior
- Database QUARANTINED state
- Forensic audit trail (source hash vs destination hash)

**Run:**
```powershell
.\scripts\Run-Scenario2-Corruption.ps1
```

**What happens:**
1. Creates test file with known hash
2. Copies to both destinations
3. **Corrupts 1 byte** in DestinationA
4. ForkerDotNet detects hash mismatch
5. Job quarantined in database
6. QuarantineEntries table populated

---

### Scenario 3: Concurrent Access (Non-Locking) 🔓
**File:** `scripts\Run-Scenario3-ConcurrentAccess.ps1`
**Duration:** 5 minutes
**Best for:** Proving PACS/external systems can access files

**What it demonstrates:**
- External applications can open files during copy
- No "file in use" errors
- Streaming copy operations (not in-memory)

**Run:**
```powershell
.\scripts\Run-Scenario3-ConcurrentAccess.ps1
```

**What happens:**
1. Starts copying 200MB file
2. Opens destination file in Notepad (mid-copy)
3. Successfully reads file content
4. Proves no file locking

---

### Scenario 4: Crash Recovery 💥
**File:** `scripts\Run-Scenario4-CrashRecovery.ps1`
**Duration:** 5 minutes
**Requires:** Administrator privileges
**Best for:** Proving reliability/resilience

**What it demonstrates:**
- SQLite WAL-mode crash safety
- Automatic recovery on service restart
- No data loss or corruption
- Resume from last known good state

**Run:**
```powershell
# Must run as Administrator
.\scripts\Run-Scenario4-CrashRecovery.ps1
```

**What happens:**
1. Starts copying large file
2. **Kills ForkerDotNet service** mid-copy (simulates crash)
3. Restarts service automatically
4. Service recovers state from SQLite database
5. Completes copy operation
6. Verifies final hash integrity

---

### Scenario 5: File Stability Detection 📊
**File:** `scripts\Run-Scenario5-StabilityDetection.ps1`
**Duration:** 3-4 minutes
**Best for:** Proving scanner integration safety

**What it demonstrates:**
- Detection of growing files (incomplete scans)
- Wait for file stability before processing
- No partial file copies
- Network copy simulation

**Run:**
```powershell
.\scripts\Run-Scenario5-StabilityDetection.ps1
```

**What happens:**
1. Creates file that grows over time (simulates scanner)
2. ForkerDotNet detects file is still growing
3. Waits for stability (size unchanged for 5 seconds)
4. Only processes when file is complete

---

## Database Monitoring

### Connecting with DataGrip (Recommended)

1. **File → New → Data Source → SQLite**
2. **Path:** `C:\ForkerDemo\forker.db`
3. **Test Connection** → OK

### Connecting with DB Browser for SQLite

1. **Open DB Browser for SQLite**
2. **File → Open Database**
3. **Navigate to:** `C:\ForkerDemo\forker.db`
4. **Click Open**

### Key Tables to Monitor

#### 1. FileJobs (Overall Job State)
```sql
SELECT
    JobId,
    SourcePath,
    State,
    DiscoveredAt,
    CompletedAt
FROM FileJobs
ORDER BY DiscoveredAt DESC
LIMIT 20;
```

**State progression:**
- DISCOVERED → File detected in Input folder
- QUEUED → Ready for processing
- IN_PROGRESS → Copy operations in progress
- PARTIAL → Some targets copied, awaiting verification
- VERIFIED → All targets copied and verified ✅
- QUARANTINED → Hash mismatch detected ⚠️

#### 2. TargetOutcomes (Per-Target Status)
```sql
SELECT
    JobId,
    TargetId,
    State,
    CopyStartedAt,
    VerifiedAt,
    Hash
FROM TargetOutcomes
ORDER BY CopyStartedAt DESC
LIMIT 20;
```

**State progression:**
- PENDING → Awaiting copy
- COPYING → Copy in progress
- COPIED → Copy complete, awaiting verification
- VERIFYING → SHA-256 hash calculation in progress
- VERIFIED → Hash matches source ✅

#### 3. QuarantineEntries (Corruption Detection)
```sql
SELECT * FROM QuarantineEntries;
```

**Should be empty** unless Scenario 2 (Corruption) has been run.

**If populated, shows:**
- JobId of quarantined file
- Reason (hash mismatch)
- Source hash vs destination hash
- Timestamp of detection

---

## Troubleshooting

### Service Won't Start

**Check .NET installation:**
```powershell
dotnet --version
# Should show 8.0.x
```

**Check environment variable:**
```powershell
echo $env:ASPNETCORE_ENVIRONMENT
# Should show "Demo" for demo mode
```

**Check directory exists:**
```powershell
Test-Path C:\ForkerDemo
# Should return True
```

---

### Demo Script Fails

**"Demo environment not configured":**
```powershell
# Run setup again
.\scripts\Demo-Setup.ps1
```

**"ForkerDotNet service not running":**
```powershell
# Start service manually
$env:ASPNETCORE_ENVIRONMENT = "Demo"
cd src\Forker.Service
dotnet run
```

---

### Database Not Found

**Check database path:**
```powershell
Test-Path C:\ForkerDemo\forker.db
# Should return True after first service run
```

**If missing:**
```powershell
# Restart service - it will create database on startup
Stop-Process -Name "Forker.Service" -Force
$env:ASPNETCORE_ENVIRONMENT = "Demo"
cd src\Forker.Service
dotnet run
```

---

### File Explorer Windows Don't Open

**Check PowerShell version:**
```powershell
$PSVersionTable.PSVersion
# Should be 5.1 or higher
```

**Manual workaround:**
```powershell
# Open File Explorer manually
explorer C:\ForkerDemo\Input
explorer C:\ForkerDemo\DestinationA
explorer C:\ForkerDemo\DestinationB
```

---

## Evidence Collection

### For Governance Review

After running demonstrations, collect evidence package:

```powershell
.\scripts\Export-DemoEvidence.ps1 -ScenarioName "Scenario1-EndToEnd"
```

**Creates evidence folder containing:**
- SQLite database snapshot (forker-snapshot.db)
- Log files from demonstration
- README with verification steps
- Governance checklist

**Evidence folder location:**
```
C:\ForkerDemo\Evidence-{ScenarioName}-{Timestamp}\
```

### Manual Evidence Collection

**1. Database snapshot:**
```powershell
Copy-Item C:\ForkerDemo\forker.db C:\ForkerDemo\Evidence\forker-snapshot.db
```

**2. Log files:**
```powershell
Copy-Item C:\ForkerDemo\Logs\*.txt C:\ForkerDemo\Evidence\
```

**3. Screenshots:**
- File Explorer windows (before/after replication)
- DataGrip query results (FileJobs, TargetOutcomes tables)
- PowerShell Get-FileHash output

---

## Quick Command Reference

```powershell
# Setup
.\scripts\Demo-Setup.ps1

# Start service (Demo mode)
$env:ASPNETCORE_ENVIRONMENT="Demo"; dotnet run --project src\Forker.Service

# Run demos
.\scripts\Run-Scenario1-EndToEnd.ps1
.\scripts\Run-Scenario2-Corruption.ps1
.\scripts\Run-Scenario3-ConcurrentAccess.ps1
.\scripts\Run-Scenario4-CrashRecovery.ps1
.\scripts\Run-Scenario5-StabilityDetection.ps1

# Simple validation test
.\scripts\Test-Simple.ps1

# Database path (for DataGrip)
C:\ForkerDemo\forker.db

# Stop service
Stop-Process -Name "Forker.Service" -Force

# View logs
Get-Content C:\ForkerDemo\Logs\forker-*.txt -Wait
```

---

## For More Information

- **Configuration Guide:** [CONFIGURATION.md](CONFIGURATION.md) - Environment setup and config files
- **Quick Start Demo:** [DEMO-NOW.md](DEMO-NOW.md) - 5-minute quick validation
- **Architecture:** [README.md](README.md) - Technical overview
- **Development Plan:** [dev_plan.md](dev_plan.md) - Detailed 30-day implementation roadmap

---

## Version History

### Version 2.0 (2025-10-06)
- Complete rewrite focusing on PowerShell observable demos
- Removed Spectre.Console fake demos
- Added DataGrip integration instructions
- Standardized configuration with .NET environments

### Version 1.0 (2025-09-30)
- Initial release with Spectre.Console demos
- Phase 11 completion
