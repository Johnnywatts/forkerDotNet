# DEPRECATED: Forker.Clinical.Demo

**Status**: ⚠️ DEPRECATED - Replaced by Real PowerShell Demo Scripts

**Date**: 2025-10-01

## Reason for Deprecation

This Spectre.Console-based demo project has been **replaced** by real, observable PowerShell demonstration scripts that use actual Windows tools and provide genuine verification.

### Issues with This Demo Project

1. **Fake Simulations**: Most demos (Demo #1-3, #5-6) used hardcoded values and fake hash verification
2. **No Real Verification**: Demos didn't actually test ForkerDotNet functionality
3. **Not Observable**: Governance teams couldn't observe real system behavior
4. **Misleading**: Appeared to show real behavior but was just Spectre.Console output

### Exception: Demo #4 (Corruption Prevention)

**Demo #4 was the ONLY real demo** with actual:
- SHA-256 hash calculation
- Real file corruption detection
- Genuine verification behavior

However, even this has been superseded by better PowerShell demos.

## Replacement: Real PowerShell Demo System

**New Location**: `demo/scripts/`

### New Demo Scripts (All Real)

1. **Run-Scenario1-EndToEnd.ps1** (5 min)
   - Real file replication with dual targets
   - PowerShell Get-FileHash verification
   - SQLite Browser state machine monitoring
   - File Explorer visual confirmation

2. **Run-Scenario2-Corruption.ps1** (3 min)
   - Real corruption injection
   - Real SHA-256 mismatch detection
   - Real quarantine behavior
   - Observable in SQLite database

3. **Run-Scenario3-ConcurrentAccess.ps1** (5 min)
   - Real external file access during copy
   - Notepad opens file mid-copy (proves no locking)
   - PowerShell reads file during operation
   - Process Monitor syscall traces (optional)

4. **Run-Scenario4-CrashRecovery.ps1** (5 min)
   - Real service crash (kills process)
   - Real SQLite WAL recovery
   - Real resume from partial copy
   - Observable recovery behavior

5. **Run-Scenario5-StabilityDetection.ps1** (3 min)
   - Real growing file simulation
   - Real stability detection wait
   - Real processing after stability confirmed
   - Observable in File Explorer and SQLite

### Why PowerShell Demos Are Better

✅ **Real Verification**: Uses PowerShell Get-FileHash (not fake hardcoded values)
✅ **Observable**: Uses File Explorer, SQLite Browser, Process Monitor
✅ **Auditable**: Creates evidence packages for governance review
✅ **Trustworthy**: Stakeholders can see real file operations
✅ **Simple**: No fake Spectre.Console output, just Windows tools
✅ **Comprehensive**: Covers all clinical safety scenarios

## Quick Start with New Demos

```powershell
# Setup demo environment
cd demo\scripts
.\Demo-Setup.ps1

# Run end-to-end demo (5 minutes)
.\Run-Scenario1-EndToEnd.ps1

# See full guide
# docs/Quick-Start-Demo.md
```

## Migration Path

If you were using this Forker.Clinical.Demo project:

| Old Demo | New PowerShell Script | Notes |
|----------|----------------------|-------|
| Demo #1: Live Workflow | Run-Scenario1-EndToEnd.ps1 | Real hash verification |
| Demo #2: Destination Locking | Run-Scenario3-ConcurrentAccess.ps1 | Real external access testing |
| Demo #3: File Stability | Run-Scenario5-StabilityDetection.ps1 | Real growing file simulation |
| Demo #4: Corruption Prevention | Run-Scenario2-Corruption.ps1 | Enhanced with quarantine behavior |
| Demo #5: Failure Recovery | Run-Scenario4-CrashRecovery.ps1 | Real crash and recovery |
| Demo #6: Real-Time Monitoring | (All scripts) | SQLite Browser + File Explorer |
| Demo #7-9: Documentation | docs/ | Still valid documentation |

## What Happens to This Project?

**Options**:

1. **Keep as Reference** (Recommended)
   - Preserve for historical record
   - Mark as deprecated in solution
   - Don't run or maintain

2. **Remove Completely**
   - Delete from solution file
   - Remove from tests directory
   - Clean git history if desired

**Current Decision**: Keep as reference with this deprecation notice.

## Documentation

For the new real demo system, see:

- [Quick-Start-Demo.md](../../docs/Quick-Start-Demo.md) - 10-minute setup + 5 scenarios
- [Demo-Tools-Setup.md](../../docs/Demo-Tools-Setup.md) - Windows tools guide
- [windows-service-deployment.md](../../docs/windows-service-deployment.md) - Production deployment

## Key Lesson Learned

**Fake demos are worse than no demos.** They create a false sense of security and undermine trust when discovered. Real, observable demonstrations with genuine verification are essential for clinical governance approval.

The new PowerShell demo system demonstrates:
- Real file operations (not simulated)
- Real hash verification (PowerShell Get-FileHash)
- Real state transitions (SQLite Browser)
- Real concurrent access (Notepad test)
- Real crash recovery (kill process)

This is what clinical governance teams need to see.

---

**Status**: ⚠️ DEPRECATED
**Superseded By**: `demo/scripts/Run-Scenario*.ps1`
**Date**: 2025-10-01
**Phase**: 11.3 (Real Demo System)
