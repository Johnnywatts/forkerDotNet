# ForkerDotNet Quick Start Demo Guide

**Time Required**: 10 minutes setup + 5 minutes per scenario

## Prerequisites

### Required Software
- ✅ Windows 10/11 or Windows Server 2019+
- ✅ .NET 8 Runtime ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- ✅ PowerShell 5.1+ (built into Windows)
- ✅ Administrator privileges (for service operations)

### Recommended Tools (Optional)
- [DB Browser for SQLite](https://sqlitebrowser.org/) - View real-time database state
- [Process Monitor (Sysinternals)](https://learn.microsoft.com/en-us/sysinternals/downloads/procmon) - Monitor file I/O
- Windows File Explorer - Visual file monitoring (built-in)

## Quick Setup (10 minutes)

### Step 1: Build ForkerDotNet

```powershell
# Navigate to repository root
cd C:\Dev\win_repos\forkerDotNet

# Build solution
dotnet build --configuration Release

# Run tests to verify (takes 2-3 minutes - includes resilience tests)
dotnet test --configuration Release
```

**Expected Result**: Build succeeds with 0 errors, all tests passing

**Note**: Full test suite includes Docker and resilience tests which take 2-3 minutes. Ensure Docker Desktop is running.

### Step 2: Setup Demo Environment

```powershell
# Run demo setup script (requires Admin)
cd demo\scripts
.\Demo-Setup.ps1
```

**Expected Result**: Demo directories created at `C:\ForkerDemo\`

### Step 3: Start ForkerDotNet Service

**Option A: Windows Service (Recommended)**
```powershell
# Install service (requires Admin)
.\Install-Service.ps1

# Start service
Start-Service ForkerDotNet
```

**Option B: Console Mode (Development)**
```powershell
# Run in console
cd ..\..\src\Forker.Service
dotnet run
```

### Step 4: Verify Installation

```powershell
# Check service status
Get-Service ForkerDotNet

# Check health endpoint (if installed)
curl http://localhost:5000/health
```

**Expected Result**: Service running, health endpoint returns 200 OK

## Demo Scenarios

### Scenario 1: End-to-End Workflow (5 min) ⭐ START HERE

**What it demonstrates:**
- Complete file replication workflow
- Dual-target copy (DestinationA + DestinationB)
- SHA-256 hash verification
- Non-locking file operations

**How to run:**
```powershell
cd demo\scripts
.\Run-Scenario1-EndToEnd.ps1
```

**What you'll see:**
1. 📂 File Explorer windows open showing Reservoir, DestinationA, DestinationB
2. 🗄️ SQLite Browser opens showing FileJobs table
3. 🔄 Test file copied to both destinations
4. ✅ PowerShell calculates SHA-256 hashes (all match)
5. 📊 Throughput metrics displayed

**Expected Duration**: 2-3 minutes for 100MB file

---

### Scenario 2: Corruption Detection (3 min)

**What it demonstrates:**
- SHA-256 hash mismatch detection
- Automatic quarantine of corrupt files
- Forensic preservation
- Clinical safety compliance

**How to run:**
```powershell
.\Run-Scenario2-Corruption.ps1
```

**What you'll see:**
1. 📂 File copied to destination
2. 💥 Corruption injected (1 byte modified)
3. 🔍 ForkerDotNet detects hash mismatch
4. 🚨 Corrupt file moved to Quarantine folder
5. 📝 SQLite shows QUARANTINED state

**Key Insight**: No corrupt data reaches clinical systems!

---

### Scenario 3: Concurrent Access (5 min)

**What it demonstrates:**
- External applications can access files during copy
- No "file in use" errors
- PACS/viewer compatibility
- Non-blocking operations

**How to run:**
```powershell
.\Run-Scenario3-ConcurrentAccess.ps1
```

**What you'll see:**
1. 📂 Large file (200MB+) starts copying
2. 📖 Notepad opens destination file mid-copy (works!)
3. 🔧 PowerShell reads file during copy (works!)
4. ✅ No locking errors
5. 🔐 Hash integrity maintained

**Key Insight**: Clinical systems can access files during ForkerDotNet operations!

---

### Scenario 4: Crash Recovery (5 min) ⚠️ Requires Admin

**What it demonstrates:**
- SQLite WAL crash-safe persistence
- Automatic recovery after service crash
- Resume from partial copy (no restart from beginning)
- Zero data loss

**How to run:**
```powershell
.\Run-Scenario4-CrashRecovery.ps1
```

**What you'll see:**
1. 📂 Large file (300MB+) starts copying
2. 💥 Service killed mid-copy (simulates power failure)
3. 🔄 Service restarted
4. 📊 Copy resumes from SQLite state (not from beginning!)
5. ✅ Hash integrity verified after recovery

**Key Insight**: No data loss even after service crash!

---

### Scenario 5: Stability Detection (3 min)

**What it demonstrates:**
- Detection of growing files (slow network copy)
- Wait for file stability before processing
- No partial file copies
- Intelligent file monitoring

**How to run:**
```powershell
.\Run-Scenario5-StabilityDetection.ps1
```

**What you'll see:**
1. 📂 File grows incrementally (simulates slow network copy)
2. ⏸️ ForkerDotNet detects growth and waits
3. ✅ Processing starts only after file stops growing
4. 🔐 Hash integrity verified

**Key Insight**: ForkerDotNet never copies incomplete files!

---

## Cleanup

After demonstrations, clean up test files:

```powershell
# Remove test files (keep database)
.\Cleanup-DemoEnvironment.ps1

# Remove test files AND reset database
.\Cleanup-DemoEnvironment.ps1 -ResetDatabase

# Keep evidence exports for governance review
.\Cleanup-DemoEnvironment.ps1 -KeepEvidence
```

## Evidence Collection

For governance approval, export evidence package:

```powershell
# Export evidence (includes SQLite snapshot, logs, hashes)
Export-DemoEvidence -ScenarioName "Scenario1" -OutputPath "C:\ForkerDemo"
```

**Evidence package includes:**
- SQLite database snapshot (FileJobs + TargetOutcomes tables)
- Service log files with audit trail
- README with governance checklist

## Troubleshooting

### "ForkerDotNet service not running"

**Solution:**
```powershell
# Check service status
Get-Service ForkerDotNet

# Start service
Start-Service ForkerDotNet

# Check logs
Get-Content C:\ProgramData\ForkerDotNet\logs\*.log -Tail 50
```

### "Demo directories not found"

**Solution:**
```powershell
# Run setup script
.\Demo-Setup.ps1
```

### "SQLite Browser not found"

**Solution:**
- Download from https://sqlitebrowser.org/
- Or use PowerShell to query database:
  ```powershell
  Install-Module -Name PSSQLite
  Invoke-SqliteQuery -Database "C:\ProgramData\ForkerDotNet\forker.db" -Query "SELECT * FROM FileJobs"
  ```

### "Test files too large / taking too long"

**Solution:**
```powershell
# Use smaller test files
.\Run-Scenario1-EndToEnd.ps1 -TestFileSize 50  # 50MB instead of 100MB
```

### "Access denied" errors

**Solution:**
- Run PowerShell as Administrator
- Ensure current user has full control of `C:\ForkerDemo` directory

## Next Steps

### For Clinical Demonstrations
1. ✅ Run all 5 scenarios in order
2. ✅ Export evidence packages
3. ✅ Review SQLite database with governance team
4. ✅ Document results in clinical safety case

### For Production Deployment
1. ✅ Test with real medical imaging files (SVS, DICOM)
2. ✅ Configure production paths in `service-config.json`
3. ✅ Install Windows Service with `Install-Service.ps1`
4. ✅ Configure monitoring and alerting
5. ✅ Run 24-hour soak test (see Phase 10 in dev_plan.md)

### For Development
1. ✅ Review source code in `src/` directory
2. ✅ Run unit tests: `dotnet test`
3. ✅ Review architecture in `README.md` and `dotNetRebuild.md`
4. ✅ See Phase 12 roadmap in `dev_plan.md`

## Support

- 📖 Documentation: `docs/` directory
- 🐛 Issues: Review `TASK_LIST.md` and `dev_plan.md`
- 🔍 Architecture: `README.md` and `dotNetRebuild.md`
- 🛡️ Security: `security-*.md` files

## Success Criteria

After running all scenarios, you should have:
- ✅ End-to-end replication verified (Scenario 1)
- ✅ Corruption detection tested (Scenario 2)
- ✅ Concurrent access confirmed (Scenario 3)
- ✅ Crash recovery validated (Scenario 4)
- ✅ Stability detection demonstrated (Scenario 5)
- ✅ Evidence packages exported
- ✅ SQLite audit trail reviewed
- ✅ Zero hash mismatches (except Scenario 2, which is intentional)

**Estimated Total Demo Time**: 25-30 minutes for all 5 scenarios

---

**Document Version**: 1.0
**Last Updated**: 2025-10-01
**Status**: Production Ready ✅
