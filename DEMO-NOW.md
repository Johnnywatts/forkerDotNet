# ForkerDotNet - Quick Demo (Current Setup)

**Your Current Configuration**: E:\ForkerDotNetTestVolume

**Time Required**: 5-10 minutes

---

## Current Status ✅

- ✅ **Build**: Clean (0 errors, 25 warnings)
- ✅ **Tests**: 249/249 passing (Domain: 143, Infrastructure: 106)
- ✅ **Critical Bug Fixed**: Repository state reconstruction working
- ✅ **Configuration**: E: drive (slow external drive for testing)
- ✅ **Scenario 2**: Corruption detection PROVEN working (see commit e284512)

---

## Quick Demo - 3 Steps

### Step 1: Start ForkerDotNet (Console Mode)

```powershell
# Kill any running instances
Stop-Process -Name dotnet -Force -ErrorAction SilentlyContinue

# Start fresh
cd c:\Dev\win_repos\forkerDotNet\src\Forker.Service
dotnet run --configuration Release
```

**Expected Output**:
```
[INFO] Forker Service starting...
[INFO] ForkerDotNet is now running - Ready to process files
[INFO] Health endpoint listening on http://localhost:8080/health/live
```

---

### Step 2: Run Simple Test (Verify Working)

**Open a NEW PowerShell window** (leave service running):

```powershell
cd c:\Dev\win_repos\forkerDotNet\demo\scripts
.\Test-Simple.ps1
```

**What you'll see**:
1. Creates 50MB test file
2. Copies to both DestinationA and DestinationB
3. Verifies SHA-256 hashes match
4. Shows throughput metrics (expect ~2.5 GB/min on E: drive)

**Expected Result**:
```
[SUCCESS] All hashes match - file integrity verified
Throughput: XXX MB/min per target
```

---

### Step 3: Run Corruption Detection Demo

**Still in the same PowerShell window**:

```powershell
# This test requires VerificationDelaySeconds: 30 in appsettings.json
# Current config has it set to 0, so first update it:

# Open appsettings.json in notepad
notepad c:\Dev\win_repos\forkerDotNet\src\Forker.Service\appsettings.json

# Change line 85:
#   FROM: "VerificationDelaySeconds": 0
#   TO:   "VerificationDelaySeconds": 30

# Save and close notepad

# Restart ForkerDotNet (Ctrl+C in the service window, then run again)
# Then run the corruption test:
.\Test-Scenario2-Corruption.ps1
```

**What you'll see**:
1. Creates 100MB test file with known hash
2. Moves to Input folder (triggers ForkerDotNet)
3. Waits for copy to complete (~12 seconds)
4. **CORRUPTS** DestinationA file by flipping bytes
5. Waits for verification (30 second delay)
6. ForkerDotNet detects hash mismatch
7. Job quarantined in database

**Expected Result**:
```
================================================================================
  SUCCESS: Corruption Detected and Quarantined
================================================================================

Database Status:
  Quarantine ID: <guid>
  Reason: Hash verification failed - data integrity compromised
  Affected Targets: 1
  Status: Active
```

---

## What Each Test Proves

### Test-Simple.ps1 ✅
- **Proves**: End-to-end replication working
- **Validates**: Dual-target copy, hash verification, file cleanup
- **Performance**: ~2.5 GB/min per target on slow drive

### Test-Scenario2-Corruption.ps1 ✅
- **Proves**: Corruption detection working (hash mismatch detected)
- **Validates**: Quarantine system, database audit trail, forensic preservation
- **Safety**: No corrupt data reaches clinical systems

---

## Your Current Environment

**Directories** (E: drive):
```
E:\ForkerDotNetTestVolume\
├── Input\                  # Drop files here
├── DestinationA\          # First target
├── DestinationB\          # Second target
├── Quarantine\            # Corrupt files (database, not filesystem)
├── Processing\            # Temp directory
├── Logs\                  # Service logs
└── forker.db              # SQLite database
```

**Configuration File**: `src/Forker.Service/appsettings.json`

**Database Browser**: `E:\ForkerDotNetTestVolume\forker.db`
- Open with DB Browser for SQLite (https://sqlitebrowser.org/)
- Tables: FileJobs, TargetOutcomes, QuarantineEntries

---

## Troubleshooting

### "Service not copying files"

Check service logs:
```powershell
Get-Content E:\ForkerDotNetTestVolume\Logs\forker-*.txt -Tail 50
```

### "Test script fails"

Make sure:
1. Service is running (see Step 1)
2. Directories exist on E: drive
3. No files already in Input folder (clean it first)

### "Corruption test fails with 'Quarantine failed'"

Check these in order:
1. VerificationDelaySeconds set to 30 in appsettings.json?
2. Service restarted after changing config?
3. Wait full 60 seconds for test to complete
4. Check logs: `Get-Content E:\ForkerDotNetTestVolume\Logs\forker-*.txt -Tail 100`

---

## Next Steps

### To run the FULL demo suite (C:\ForkerDemo)

The full Quick-Start-Demo.md uses `C:\ForkerDemo` with visual File Explorer windows and SQLite Browser integration. To use it:

1. Update appsettings.json paths from `E:\ForkerDotNetTestVolume` to `C:\ForkerDemo`
2. Run Demo-Setup.ps1 to create C:\ForkerDemo directories
3. Follow docs/Quick-Start-Demo.md

### For production deployment

See docs/Quick-Start-Demo.md "For Production Deployment" section.

---

## Proven Working (as of commit e284512)

✅ **Core System**: All 249 tests passing
✅ **End-to-End**: File replication with hash verification
✅ **Corruption Detection**: Hash mismatch detection and quarantine
✅ **Database**: SQLite persistence with proper state reconstruction
✅ **Performance**: 2.5+ GB/min per target on slow drive
✅ **Crash Recovery**: SQLite WAL-based recovery (not tested in demo yet)

---

**Last Updated**: 2025-10-02
**Current Branch**: master
**Configuration**: E:\ForkerDotNetTestVolume (slow drive for testing)
