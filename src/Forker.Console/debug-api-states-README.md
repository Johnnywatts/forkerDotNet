# API State Debugging Harness

## Purpose

This PowerShell script captures live API responses during file processing to help debug state mapping issues between the C# ForkerDotNet service and the Go console.

## The Problem We Solved

On 2025-10-10, we encountered an issue where the Transactions page Active pane displayed "undefined" for target copy states. The root cause was a field name mismatch in the data flow chain:

- **C# Service**: Returns `copyState` in JSON
- **Go API Client**: Had `State` field trying to unmarshal `copyState`
- **JavaScript**: Expected `target.copyState` but got `undefined`

This harness would have helped us identify this issue much faster by showing the actual JSON responses at each layer.

## How It Works

1. **Triggers Processing**: Copies test files from `DestinationA` to `Input` directory
2. **Polls Both APIs**: Every 500ms, fetches job data from:
   - C# Service API (`http://localhost:8081`)
   - Go Console API (`http://localhost:5000`)
3. **Captures Full JSON**: Logs complete API responses with timestamps
4. **Lists Directories**: Shows file counts in Input/DestinationA/DestinationB after each poll
5. **Writes Log File**: Saves everything to `api-debug-YYYYMMDD-HHMMSS.log`

## Usage

### Basic Usage
```powershell
# Run with default settings (500ms polls for 60 seconds)
.\debug-api-states.ps1
```

### Custom Settings
```powershell
# Poll every 250ms for 30 seconds
.\debug-api-states.ps1 -PollIntervalMs 250 -DurationSeconds 30

# Use different directories
.\debug-api-states.ps1 -SourceDir "C:\OtherData" -InputDir "C:\ForkerDemo\Input"
```

### Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `PollIntervalMs` | 500 | Milliseconds between polls |
| `DurationSeconds` | 60 | How long to run the test |
| `SourceDir` | C:\ForkerDemo\DestinationA | Where to copy test files from |
| `InputDir` | C:\ForkerDemo\Input | Where to copy files to (triggers processing) |
| `DestinationA` | C:\ForkerDemo\DestinationA | First target directory to monitor |
| `DestinationB` | C:\ForkerDemo\DestinationB | Second target directory to monitor |

## Output Example

```
[14:05:32.123] ===================================
[14:05:32.124] API State Debugging Harness Started
[14:05:32.125] ===================================
[14:05:32.126] STEP 1: Copying files from C:\ForkerDemo\DestinationA to C:\ForkerDemo\Input
[14:05:32.234]   Copied: 484759.svs
[14:05:32.345]   Copied: 484765.svs
[14:05:32.456] File copy complete - processing should start now
[14:05:32.457]
[14:05:32.458] STEP 2: Polling APIs every 500 ms for 60 seconds
[14:05:32.459]
[14:05:32.460] ===== POLL #1 =====
[14:05:32.461] --- C# API (port 8081) ---
[14:05:32.567] Jobs List Response:
[14:05:32.568] [
[14:05:32.569]   {
[14:05:32.570]     "jobId": "abc-123",
[14:05:32.571]     "state": "InProgress",
[14:05:32.572]     ...
[14:05:32.573]   }
[14:05:32.574] ]
[14:05:32.575] Active jobs: 1
[14:05:32.576] Job Details (C# API): abc-123
[14:05:32.678] {
[14:05:32.679]   "targets": [
[14:05:32.680]     {
[14:05:32.681]       "targetId": "TargetA",
[14:05:32.682]       "copyState": "Copying",  <-- ✅ Should show this
[14:05:32.683]       ...
[14:05:32.684]     }
[14:05:32.685]   ]
[14:05:32.686] }
[14:05:32.687]
[14:05:32.688] --- Go Console API (port 5000) ---
[14:05:32.689] Job Details (Go API): abc-123
[14:05:32.789] {
[14:05:32.790]   "targets": [
[14:05:32.791]     {
[14:05:32.792]       "targetId": "TargetA",
[14:05:32.793]       "state": "",  <-- ❌ Bug: should be "copyState": "Copying"
[14:05:32.794]       ...
[14:05:32.795]     }
[14:05:32.796]   ]
[14:05:32.797] }
[14:05:32.798]
[14:05:32.799] --- Directory Listings ---
[14:05:32.800] Input:        Files: 2 | 484759.svs, 484765.svs
[14:05:32.801] DestinationA: Files: 0 |
[14:05:32.802] DestinationB: Files: 0 |
```

## Analysis Tips

After the script completes, analyze the log file:

### 1. Verify C# API Returns `copyState`
```powershell
Select-String -Path api-debug-*.log -Pattern "copyState" -Context 2
```

Should show:
```
"targetId": "TargetA",
"copyState": "Copying",  <-- ✅ Field present
"hash": null,
```

### 2. Compare C# vs Go Responses
Search for the same job ID in both API sections and compare:
```powershell
Select-String -Path api-debug-*.log -Pattern "Job Details.*abc-123" -Context 20
```

### 3. Look for State Values
```powershell
Select-String -Path api-debug-*.log -Pattern "Copying|Verifying|Verified"
```

**Expected Target Copy States** (from `TargetCopyState.cs`):
```
Pending          → Target copy not started yet
Copying          → Copy operation in progress
Copied           → Copy complete, waiting for verification
Verifying        → Hash verification in progress
Verified         → Successfully verified (terminal state)
FailedRetryable  → Failed but will retry
FailedPermanent  → Failed permanently (terminal state)
```

Should show state transitions over time:
- `"copyState": "Pending"` → Job queued, waiting to start
- `"copyState": "Copying"` → Files being copied (~20-30s for 3GB files)
- `"copyState": "Copied"` → Copy complete, before verification starts
- `"copyState": "Verifying"` → Hash verification in progress (~2-3s)
- `"copyState": "Verified"` → Complete ✓

**Note**: To capture all state transitions, run the harness for at least 60 seconds with large files (2-3GB). Small files may skip the "Copied" state if verification starts immediately.

**Check which states were captured**:
```powershell
# Count occurrences of each copyState value
grep -o '"copyState": "[^"]*"' api-debug-*.log | sort | uniq -c
```

### 4. Check for Field Mismatches
```powershell
# Look for empty state field (indicates bug)
Select-String -Path api-debug-*.log -Pattern '"state": ""'

# Look for correct copyState field
Select-String -Path api-debug-*.log -Pattern '"copyState":'
```

### 5. Track State Transitions
Watch how a single job's state changes over multiple polls:
```powershell
Select-String -Path api-debug-*.log -Pattern "abc-123" -Context 5 | more
```

## When to Use This Harness

Use this debugging tool when:

1. **UI displays "undefined"**: JavaScript can't read expected fields
2. **State badges missing**: Target states not showing in Active pane
3. **API integration issues**: Mismatch between C# service and Go console
4. **Field name changes**: After modifying DTOs/models in either language
5. **JSON serialization issues**: Investigating camelCase vs PascalCase problems

## Integration with Development Workflow

### Before Making API Changes
```powershell
# Capture baseline
.\debug-api-states.ps1 -DurationSeconds 30
Copy-Item api-debug-*.log baseline-before-changes.log
```

### After Making API Changes
```powershell
# Capture new behavior
.\debug-api-states.ps1 -DurationSeconds 30

# Compare with baseline
code --diff baseline-before-changes.log api-debug-*.log
```

## Prerequisites

- ForkerDotNet service running on port 8081
- Forker Console running on port 5000 (Docker)
- Test files in SourceDir (default: C:\ForkerDemo\DestinationA)
- PowerShell 5.1+ or PowerShell Core 7+

## Troubleshooting the Harness

### "Cannot copy files"
```powershell
# Check source directory exists
Test-Path C:\ForkerDemo\DestinationA
```

### "Cannot connect to API"
```powershell
# Test C# service
Invoke-RestMethod -Uri http://localhost:8081/api/monitoring/health

# Test Go console
Invoke-RestMethod -Uri http://localhost:5000/health
```

### "No active jobs found"
The files may be processing too quickly. Try:
```powershell
# Use faster poll interval
.\debug-api-states.ps1 -PollIntervalMs 100

# Or copy larger files that take longer to process
```

## Related Documentation

- **Issue Walkthrough**: See `PHASE3-API-MIGRATION.md` Troubleshooting section
- **API Specification**: See `MonitoringModels.cs` for C# DTOs
- **Client Models**: See `apiclient/models.go` for Go structs
- **Console Design**: See `console-design.md` for architecture

## Future Enhancements

Potential improvements to this harness:

1. **JSON Diff Tool**: Automatically highlight differences between C# and Go responses
2. **State Transition Graph**: Visualize state changes over time
3. **Performance Metrics**: Track API response times
4. **Alert on Mismatches**: Automatically detect field name issues
5. **Export to CSV**: Parse JSON into tabular format for Excel analysis
