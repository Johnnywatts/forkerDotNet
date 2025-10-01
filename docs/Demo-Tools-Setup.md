# ForkerDotNet Demo Tools Setup Guide

This guide covers installing and configuring the recommended Windows tools for ForkerDotNet demonstrations.

## Required Tools

### 1. Windows File Explorer (Built-in) ✅

**Purpose**: Visual monitoring of file replication in real-time

**Setup**: None required - built into Windows

**Usage in Demos**:
- Scenario 1-5: Auto-opened in grid layout showing Reservoir, Input, DestinationA, DestinationB
- Watch files appear and grow during replication
- Verify dual-target copies complete

---

### 2. PowerShell 5.1+ (Built-in) ✅

**Purpose**: Running demo scripts and hash verification

**Setup**: None required - built into Windows 10/11

**Verify Installation**:
```powershell
$PSVersionTable.PSVersion
# Should show 5.1 or higher
```

**Usage in Demos**:
- All scenarios: Run demo scripts
- Hash verification: `Get-FileHash -Algorithm SHA256`
- Service control: `Get-Service`, `Start-Service`, `Stop-Service`

---

## Recommended Tools (Optional but Highly Recommended)

### 3. DB Browser for SQLite

**Purpose**: Real-time monitoring of ForkerDotNet state machine transitions

**Download**: https://sqlitebrowser.org/dl/

**Installation**:
1. Download "DB Browser for SQLite - Standard installer for 64-bit Windows"
2. Run installer (defaults are fine)
3. Launch "DB Browser for SQLite" from Start Menu

**Usage in Demos**:
- Opens automatically in Scenario 1-5
- Navigate to: `C:\ProgramData\ForkerDotNet\forker.db`
- Tables to watch:
  - **FileJobs**: Main state machine (DISCOVERED → QUEUED → IN_PROGRESS → VERIFIED)
  - **TargetOutcomes**: Per-target copy state (PENDING → COPYING → COPIED → VERIFYING → VERIFIED)
  - **Events**: Append-only audit log

**Key Features**:
- Auto-refresh: View > Refresh (F5)
- SQL query: Execute SQL tab
- Export: File > Export

**Example Queries**:
```sql
-- View all jobs
SELECT SourcePath, State, CreatedAt, UpdatedAt FROM FileJobs;

-- View target outcomes for a job
SELECT JobId, TargetType, State, HashValue FROM TargetOutcomes;

-- View recent events
SELECT EventType, Timestamp, Details FROM Events ORDER BY Timestamp DESC LIMIT 20;
```

---

### 4. Process Monitor (Sysinternals)

**Purpose**: Monitor file I/O operations, verify non-locking behavior

**Download**: https://learn.microsoft.com/en-us/sysinternals/downloads/procmon

**Installation**:
1. Download Procmon.zip
2. Extract to `C:\Tools\Sysinternals\` (or any location)
3. Run `Procmon64.exe` (requires Admin)

**Setup for Demos**:
1. Launch Procmon
2. Filter > Process Name > is > Forker.Service.exe > Add > OK
3. Filter > Path > contains > C:\ForkerDemo > Add > OK
4. Filter > Operation > is > ReadFile > Add > OK
5. Filter > Operation > is > WriteFile > Add > OK
6. Options > Capture Events (Ctrl+E to toggle)

**Usage in Demos**:
- Scenario 3 (Concurrent Access): Monitor file operations while external apps access files
- Shows ForkerDotNet uses FileShare.Read (non-exclusive locks)
- Proves external applications can access files during copy

**Key Columns**:
- **Time**: Timestamp of operation
- **Process Name**: Forker.Service.exe
- **Operation**: ReadFile, WriteFile, CreateFile
- **Path**: File being accessed
- **Result**: SUCCESS or error code

---

### 5. Windows Performance Monitor (Built-in) ✅

**Purpose**: Monitor system resources during copy operations

**Setup**: Built into Windows

**Usage in Demos**:
1. Run `perfmon` from Start Menu
2. Add counters:
   - Process > Private Bytes > Forker.Service
   - Process > % Processor Time > Forker.Service
   - PhysicalDisk > Disk Bytes/sec
   - Memory > Available MBytes

**Key Metrics**:
- **Memory Usage**: Should stay <100MB (verify no memory leaks)
- **CPU Usage**: Should be low (verify efficient I/O)
- **Disk Throughput**: Should match expected copy speed

---

### 6. HashCheck Shell Extension (Optional)

**Purpose**: Right-click context menu for hash verification

**Download**: https://github.com/gurnec/HashCheck/releases

**Installation**:
1. Download `HashCheckInstall.exe`
2. Run installer
3. Right-click any file > Properties > Checksums tab

**Usage in Demos**:
- Quick verification: Right-click destination file > Properties > Checksums
- Compare with source file hash
- Alternative to PowerShell Get-FileHash

---

## Alternative: PowerShell-Only Demos (No GUI Tools)

If you cannot install GUI tools, all demos work with PowerShell only:

### SQLite Queries (PowerShell Module)

```powershell
# Install PSSQLite module
Install-Module -Name PSSQLite -Scope CurrentUser

# Query FileJobs
Invoke-SqliteQuery -Database "C:\ProgramData\ForkerDotNet\forker.db" -Query "SELECT * FROM FileJobs"

# Query TargetOutcomes
Invoke-SqliteQuery -Database "C:\ProgramData\ForkerDotNet\forker.db" -Query "SELECT * FROM TargetOutcomes"
```

### File Monitoring (PowerShell)

```powershell
# Watch directory for changes
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = "C:\ForkerDemo\DestinationA"
$watcher.EnableRaisingEvents = $true

Register-ObjectEvent -InputObject $watcher -EventName Created -Action {
    Write-Host "File created: $($Event.SourceEventArgs.FullPath)"
}
```

### Service Monitoring (PowerShell)

```powershell
# Watch service status
while ($true) {
    $service = Get-Service ForkerDotNet
    Write-Host "$((Get-Date).ToString('HH:mm:ss')) - Service: $($service.Status)"
    Start-Sleep -Seconds 2
}
```

---

## Demo Environment Layout

Recommended screen layout for demonstrations:

```
+------------------------+------------------------+
|                        |                        |
|   File Explorer Grid   |   SQLite Browser       |
|   (3 windows)          |   (FileJobs table)     |
|                        |                        |
+------------------------+------------------------+
|                        |                        |
|   PowerShell           |   Process Monitor      |
|   (Demo script)        |   (File I/O)           |
|                        |                        |
+------------------------+------------------------+
```

**Tips**:
- Use Windows Snap (Win+Arrow keys) to position windows
- Use multiple monitors if available
- Demo scripts auto-arrange File Explorer windows

---

## Troubleshooting

### "DB Browser for SQLite: Database file is encrypted or is not a database"

**Cause**: Database file is locked by ForkerDotNet service

**Solution**:
- Use "Read Only" mode in DB Browser
- Or stop ForkerDotNet service temporarily

### "Process Monitor: Cannot start driver"

**Cause**: Requires Administrator privileges

**Solution**:
- Right-click Procmon64.exe > Run as Administrator
- Or run PowerShell as Administrator first

### "PowerShell: Execution policy prevents running scripts"

**Cause**: Script execution is disabled

**Solution**:
```powershell
# Allow scripts (Admin PowerShell)
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope LocalMachine

# Or bypass for single session
powershell.exe -ExecutionPolicy Bypass -File .\Demo-Setup.ps1
```

### "File Explorer windows not auto-arranging"

**Cause**: Windows API limitations

**Solution**:
- Manually position File Explorer windows using Windows Snap
- Use Win+Arrow keys for quick arrangement
- Demo scripts still open correct folders

---

## Evidence Collection Tools

### Screenshot Tools

**Windows Snipping Tool** (Built-in):
- Win+Shift+S: Capture screenshot
- Save to Evidence folder for governance review

**Windows Game Bar** (Built-in):
- Win+G: Open Game Bar
- Win+Alt+R: Record video of demo

### Log Analysis Tools

**Visual Studio Code**:
- Open log files from `C:\ProgramData\ForkerDotNet\logs`
- Search for errors, warnings, state transitions

**LogParser** (Optional):
- Advanced log analysis for large log files
- Download: https://www.microsoft.com/en-us/download/details.aspx?id=24659

---

## Demo Checklist

Before running demonstrations:

- [ ] .NET 8 Runtime installed
- [ ] PowerShell 5.1+ available
- [ ] DB Browser for SQLite installed
- [ ] Process Monitor downloaded (optional)
- [ ] Demo environment created (`.\Demo-Setup.ps1`)
- [ ] ForkerDotNet service running (`Get-Service ForkerDotNet`)
- [ ] File Explorer windows can be opened
- [ ] PowerShell execution policy allows scripts

---

## Next Steps

- ✅ Tools installed → Run [Quick-Start-Demo.md](Quick-Start-Demo.md)
- ✅ Want production deployment → See [windows-service-deployment.md](windows-service-deployment.md)
- ✅ Need help → Review troubleshooting section above

---

**Document Version**: 1.0
**Last Updated**: 2025-10-01
**Status**: Production Ready ✅
