# Process Observability Refactor Plan

**Date**: 2025-10-10
**Purpose**: Add comprehensive state transition logging and increase database persistence frequency to enable real-time visibility of file processing states.

---

## Problem Statement

### Current Behavior
State transitions for target copy operations occur in-memory within a single method call:
- `Copying` → `Copied` → `Verifying` → `Verified`
- Only the **final state** (`Verified`) is persisted to the database
- API polling reads from database, so it only sees:
  - `Copying` (during file copy)
  - `Verified` (after verification completes)
- **Missing states**: `Copied`, `Verifying` are never visible via API

### Evidence
1. **Test Results**: Using `debug-api-states.ps1` with 100ms polling on slow spinning disk
   - Captured 194 instances of `"copyState": "Copying"`
   - Captured ZERO instances of `Copied`, `Verifying`, or target-level `Verified`
   - Jobs transitioned from `InProgress` → `Verified` directly

2. **Hash Timing Test**: Using `test-hash-timing.ps1`
   - 2.3GB file takes **1,285ms** to hash on fast SSD
   - Takes **1,547ms** to hash on slow spinning disk
   - That's **12-15 polling intervals** at 100ms
   - Yet we still can't capture the `Verifying` state

3. **Root Cause**: Found in `VerificationService.VerifyTargetOutcomeAsync()` (lines 128-136)
   ```csharp
   targetOutcome.StartVerification();           // Sets state to VERIFYING (in-memory)
   var result = await VerifyFileHashAsync(...); // Takes 1.2+ seconds
   targetOutcome.CompleteVerification();        // Sets state to VERIFIED (in-memory)
   ```
   Then in `VerificationOrchestrator.cs` line 356:
   ```csharp
   await _targetOutcomeRepository.UpdateAsync(targetOutcome, cancellationToken); // Saves FINAL state only
   ```

---

## Solution: Two Complementary Changes

### Change 1: State Change Audit Logging (Historical Record)
Create permanent audit trail of ALL state transitions

**Purpose**:
- ✅ Compliance audit trail for medical imaging files
- ✅ Performance analysis (time spent in each state)
- ✅ Debugging capability
- ✅ Root cause analysis for failures

### Change 2: Increase Database Persistence Frequency (Real-time Visibility)
Persist to database after EACH state transition, not just the final one

**Purpose**:
- ✅ API polling shows current state accurately
- ✅ UI displays "Verifying" status in real-time
- ✅ External monitoring systems see intermediate states
- ✅ Crash recovery knows exact state before crash

---

## Implementation Plan

### Phase 1: Database Schema Changes

#### 1.1 Create State Change Log Table
**File**: `src/Forker.Infrastructure/Persistence/Migrations/AddStateChangeLog.cs`

**Schema**:
```sql
CREATE TABLE StateChangeLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    JobId TEXT NOT NULL,
    EntityType TEXT NOT NULL,          -- 'Job' or 'Target'
    EntityId TEXT,                      -- NULL for Job, TargetId for Target
    OldState TEXT,                      -- NULL for initial state
    NewState TEXT NOT NULL,
    Timestamp TEXT NOT NULL,            -- ISO8601 format
    DurationMs INTEGER,                 -- Time since last state change (for this entity)
    AdditionalContext TEXT              -- JSON: {"bytesCopied": 12345, "errorMessage": "..."}
);

CREATE INDEX idx_statelog_jobid ON StateChangeLog(JobId);
CREATE INDEX idx_statelog_timestamp ON StateChangeLog(Timestamp);
CREATE INDEX idx_statelog_entity ON StateChangeLog(EntityType, EntityId);
```

**Why these indexes**:
- `JobId`: Query all transitions for a specific job
- `Timestamp`: Query transitions in time range
- `Entity`: Query all transitions for a specific target

#### 1.2 Add Configuration Settings
**File**: `src/Forker.Service/appsettings.json`

**Add section**:
```json
"StateChangeLogging": {
  "Enabled": true,
  "MaxRecords": 100000,
  "AutoCleanupEnabled": true,
  "RetentionDays": 90,
  "IncludeAdditionalContext": true,
  "LogToFile": false,
  "LogFilePath": "C:\\ProgramData\\ForkerDotNet\\Logs\\state-changes-.txt"
}
```

**Configuration Options**:
- `Enabled`: Master switch for state change logging
- `MaxRecords`: Trigger cleanup when exceeded (prevents unbounded growth)
- `AutoCleanupEnabled`: Automatically delete old records
- `RetentionDays`: Keep logs for this many days
- `IncludeAdditionalContext`: Store JSON context (bytes copied, errors, etc.)
- `LogToFile`: Also write to structured log file (optional, for debugging)

---

### Phase 2: Domain Layer Changes

#### 2.1 Create State Change Logger Interface
**File**: `src/Forker.Domain/Services/IStateChangeLogger.cs`

```csharp
namespace Forker.Domain.Services;

/// <summary>
/// Service for logging all state transitions for audit trail and observability.
/// </summary>
public interface IStateChangeLogger
{
    /// <summary>
    /// Log a job state transition
    /// </summary>
    Task LogJobStateChangeAsync(
        string jobId,
        JobState? oldState,
        JobState newState,
        Dictionary<string, object>? additionalContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Log a target copy state transition
    /// </summary>
    Task LogTargetStateChangeAsync(
        string jobId,
        string targetId,
        TargetCopyState? oldState,
        TargetCopyState newState,
        Dictionary<string, object>? additionalContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query state changes for a specific job
    /// </summary>
    Task<List<StateChangeLogEntry>> GetJobStateHistoryAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Query state changes for a specific target
    /// </summary>
    Task<List<StateChangeLogEntry>> GetTargetStateHistoryAsync(
        string jobId,
        string targetId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a single state change log entry
/// </summary>
public record StateChangeLogEntry(
    long Id,
    string JobId,
    string EntityType,
    string? EntityId,
    string? OldState,
    string NewState,
    DateTime Timestamp,
    long? DurationMs,
    string? AdditionalContext);
```

#### 2.2 Modify Domain Entities to Accept Logger

**DO NOT** modify the domain entities themselves to depend on infrastructure.
Instead, we'll inject the logger at the service layer where state transitions occur.

**Rationale**: Keep domain layer pure and infrastructure-agnostic.

---

### Phase 3: Infrastructure Layer Changes

#### 3.1 Implement State Change Logger
**File**: `src/Forker.Infrastructure/Services/StateChangeLogger.cs`

```csharp
namespace Forker.Infrastructure.Services;

public sealed class StateChangeLogger : IStateChangeLogger
{
    private readonly IDbConnection _connection;
    private readonly ILogger<StateChangeLogger> _logger;
    private readonly StateChangeLoggingConfig _config;

    // Implementation details:
    // - Insert state change records into StateChangeLog table
    // - Include timestamp with millisecond precision
    // - Calculate duration since last state change for this entity
    // - Serialize additional context as JSON
    // - Handle database errors gracefully (don't fail job processing if logging fails)
}
```

**Key Implementation Points**:
1. **Non-blocking**: State change logging failures should NOT fail the job
2. **Performance**: Use parameterized queries, batch inserts if possible
3. **Cleanup**: Implement automatic cleanup of old records based on config
4. **Context Serialization**: Use System.Text.Json for additional context

#### 3.2 Repository Changes - Add Persistence Points

**Current Code** (VerificationOrchestrator.cs lines 348-356):
```csharp
// Line 348-349: Calls verification service
var verificationResult = await _verificationService.VerifyTargetOutcomeAsync(
    targetOutcome, expectedHash, cancellationToken);

// Line 356: Saves FINAL state only
await _targetOutcomeRepository.UpdateAsync(targetOutcome, cancellationToken);
```

**Updated Code** (VerificationService.cs lines 128-136):
```csharp
// Line 129: Set to Verifying
targetOutcome.StartVerification();
await _stateChangeLogger.LogTargetStateChangeAsync(
    targetOutcome.JobId,
    targetOutcome.TargetId,
    TargetCopyState.Copied,
    TargetCopyState.Verifying);
// *** ADD THIS: Persist intermediate state ***
await _targetOutcomeRepository.UpdateAsync(targetOutcome, cancellationToken);

// Line 131: Do hash calculation (1.2+ seconds)
var result = await VerifyFileHashAsync(targetOutcome.FinalPath, expectedHash, cancellationToken);

// Line 136: Set to Verified
targetOutcome.CompleteVerification();
await _stateChangeLogger.LogTargetStateChangeAsync(
    targetOutcome.JobId,
    targetOutcome.TargetId,
    TargetCopyState.Verifying,
    TargetCopyState.Verified,
    new Dictionary<string, object> {
        { "hash", result.ComputedHash },
        { "verificationDurationMs", result.Duration.TotalMilliseconds }
    });
// This save happens in orchestrator at line 356 (already exists)
```

#### 3.3 Similar Changes for Copy Operations

**Files to Modify**:
- `src/Forker.Infrastructure/Services/FileCopyService.cs`
- `src/Forker.Infrastructure/Services/DualTargetCopyOrchestrator.cs`

**Add persistence after**:
- `CompleteCopy()` - Persist `Copied` state before verification starts

---

### Phase 4: Service Registration

**File**: `src/Forker.Service/Program.cs`

```csharp
// Register state change logger
builder.Services.Configure<StateChangeLoggingConfig>(
    builder.Configuration.GetSection("StateChangeLogging"));
builder.Services.AddSingleton<IStateChangeLogger, StateChangeLogger>();
```

---

### Phase 5: API Endpoints for State History

**File**: `src/Forker.Service/Controllers/MonitoringController.cs`

Add new endpoints:
```csharp
// GET /api/monitoring/jobs/{id}/state-history
[HttpGet("jobs/{id}/state-history")]
public async Task<ActionResult<List<StateChangeLogEntry>>> GetJobStateHistory(string id)
{
    var history = await _stateChangeLogger.GetJobStateHistoryAsync(id);
    return Ok(history);
}

// GET /api/monitoring/jobs/{jobId}/targets/{targetId}/state-history
[HttpGet("jobs/{jobId}/targets/{targetId}/state-history")]
public async Task<ActionResult<List<StateChangeLogEntry>>> GetTargetStateHistory(
    string jobId, string targetId)
{
    var history = await _stateChangeLogger.GetTargetStateHistoryAsync(jobId, targetId);
    return Ok(history);
}
```

---

## Testing Plan

### Test 1: Verify State Change Logging Works
**File**: Create `tests/Forker.Infrastructure.Tests/StateChangeLoggerTests.cs`

**Tests**:
1. `LogJobStateChange_StoresCorrectData`
2. `LogTargetStateChange_StoresCorrectData`
3. `GetJobStateHistory_ReturnsAllTransitions`
4. `GetTargetStateHistory_ReturnsAllTransitions`
5. `LogStateChange_WithContext_SerializesCorrectly`
6. `LogStateChange_FailureDoesNotThrow`

### Test 2: Verify Intermediate States Are Visible
**Script**: Update `debug-api-states.ps1`

**Expected Results**:
- Capture `"copyState": "Copying"` during file copy
- Capture `"copyState": "Copied"` after copy completes
- Capture `"copyState": "Verifying"` during hash verification
- Capture `"copyState": "Verified"` after verification completes

**Success Criteria**: All 4 states captured during slow drive test

### Test 3: Verify State History API
**Tool**: Create `test-state-history.ps1`

**Test**:
1. Process a file through the system
2. Call `/api/monitoring/jobs/{id}/state-history`
3. Verify it shows complete transition history:
   - Discovered → InProgress
   - InProgress → Partial (if applicable)
   - Partial → Verified

4. Call `/api/monitoring/jobs/{jobId}/targets/{targetId}/state-history`
5. Verify it shows complete target transition history:
   - Pending → Copying
   - Copying → Copied
   - Copied → Verifying
   - Verifying → Verified

### Test 4: Performance Impact
**Tool**: Create `test-performance-impact.ps1`

**Test**:
1. Process 10 files with state logging ENABLED
2. Process 10 files with state logging DISABLED
3. Compare average processing time
4. Verify impact is < 5%

### Test 5: Database Growth
**Test**:
1. Process 100 files
2. Check StateChangeLog table size
3. Verify cleanup mechanism works
4. Verify old records are deleted after retention period

---

## Rollback Plan

### If State Change Logging Has Issues
**Option 1**: Disable via configuration
```json
"StateChangeLogging": {
  "Enabled": false
}
```
Service continues to work without logging.

**Option 2**: Revert commits
```bash
git revert <commit-hash>
git push origin master
```

### If Increased Persistence Causes Performance Issues
**Quick Fix**: Add configuration flag
```json
"Persistence": {
  "SaveIntermediateStates": false
}
```
Falls back to original behavior (only save final state).

---

## Implementation Task List

**Status Icons**:
- ⏳ Not Started  
- 🔄 In Progress
- ✅ Completed
- ❌ Blocked/Failed

### Step 1: Database Schema ✅
1. ✅ Create migration for StateChangeLog table
2. ✅ Test migration up/down
3. ✅ Verify indexes created

### Step 2: Configuration ✅
1. ✅ Add StateChangeLoggingConfig class
2. ✅ Add configuration section to appsettings.json
3. ✅ Test configuration loading

### Step 3: Domain Interface ✅
1. ✅ Create IStateChangeLogger interface
2. ✅ Create StateChangeLogEntry record
3. ✅ No changes to domain entities yet

### Step 4: Infrastructure Implementation ✅
1. ✅ Implement StateChangeLogger service
2. ✅ Write unit tests (22/22 passing - 100%)
3. ✅ Register service in DI container

### Step 5: Add Logging Calls ✅
1. ✅ Modify VerificationService to log state changes
2. ✅ Add repository UpdateAsync after StartVerification()
3. ⏳ Test with slow drive files (ready to test)

### Step 6: Extend to Copy Operations ⏳
1. ⏳ Modify FileCopyService to log state changes
2. ⏳ Add repository UpdateAsync after CompleteCopy()
3. ⏳ Test with slow drive files

### Step 7: API Endpoints ✅
1. ✅ Add state history endpoints (`GET /api/monitoring/jobs/{id}/state-history`)
2. ✅ Test endpoints return correct data (verified with job 006ccd6e - shows Verifying states with 2.5s durations)
3. ⏳ Update Go Console to display state history

### Step 8: Commit Work ✅
1. ✅ Verify all tests passing
2. ✅ Create git commit with descriptive message
3. ✅ Document changes in commit message

### Step 9: Integration Testing ⏳
1. ⏳ Run full test suite
2. ⏳ Use debug-api-states.ps1 to verify all states captured
3. ⏳ Performance testing
4. ⏳ Cleanup testing

---

## Success Criteria

### Functional Requirements
- ✅ All state transitions logged to StateChangeLog table
- ✅ API polling captures all intermediate states (Copying, Copied, Verifying, Verified)
- ✅ State history API returns complete transition timeline
- ✅ Additional context (hashes, errors) stored correctly

### Non-Functional Requirements
- ✅ Performance impact < 5% on file processing time
- ✅ State logging failures do not crash the service
- ✅ Database growth controlled via automatic cleanup
- ✅ All existing tests continue to pass
- ✅ New tests provide >90% coverage of new code

### Observability Improvements
- ✅ Can determine exact time spent in each state
- ✅ Can track state transition patterns over time
- ✅ Can identify performance bottlenecks
- ✅ Can debug verification failures with complete audit trail

---

## Files to Create/Modify

### New Files
- `src/Forker.Domain/Services/IStateChangeLogger.cs`
- `src/Forker.Infrastructure/Services/StateChangeLogger.cs`
- `src/Forker.Infrastructure/Configuration/StateChangeLoggingConfig.cs`
- `src/Forker.Infrastructure/Persistence/Migrations/AddStateChangeLog.cs`
- `tests/Forker.Infrastructure.Tests/StateChangeLoggerTests.cs`
- `src/Forker.Console/test-state-history.ps1`
- `src/Forker.Console/test-performance-impact.ps1`

### Modified Files
- `src/Forker.Service/appsettings.json` - Add StateChangeLogging config
- `src/Forker.Service/Program.cs` - Register StateChangeLogger
- `src/Forker.Infrastructure/Services/VerificationService.cs` - Add logging and persistence
- `src/Forker.Infrastructure/Services/FileCopyService.cs` - Add logging and persistence
- `src/Forker.Infrastructure/Services/DualTargetCopyOrchestrator.cs` - Add persistence calls
- `src/Forker.Service/Controllers/MonitoringController.cs` - Add state history endpoints
- `src/Forker.Console/debug-api-states.ps1` - Update to show all captured states

---

## Open Questions

### Question 1: Separate Table vs. Same Table?
**Option A**: Separate `JobStateChangeLog` and `TargetStateChangeLog` tables
**Option B**: Single `StateChangeLog` table with EntityType discriminator

**Recommendation**: Option B (single table) - Simpler queries, easier to see complete job timeline

### Question 2: Synchronous vs. Asynchronous Logging?
**Option A**: Synchronous - Log immediately, block until written
**Option B**: Asynchronous - Queue logs, write in background

**Recommendation**: Option A (synchronous) for Phase 1
- Simpler implementation
- Ensures logs are written before next state transition
- Performance impact likely minimal (< 5ms per insert)
- Can optimize later if needed

### Question 3: What Additional Context to Store?
**For Copy Operations**:
- BytesCopied
- CopyDurationMs
- SourceHash (when available)

**For Verification Operations**:
- ComputedHash
- ExpectedHash
- VerificationDurationMs
- FileSize

**For Failures**:
- ErrorMessage
- ErrorType
- AttemptNumber
- CanRetry

### Question 4: Cleanup Strategy?
**Option A**: Time-based (delete records older than N days)
**Option B**: Count-based (keep only last N records per job)
**Option C**: Size-based (delete when table exceeds N MB)

**Recommendation**: Option A (time-based) with configurable retention
- Predictable behavior
- Complies with data retention policies
- Easy to understand and configure

---

## Risk Assessment

### Risk 1: Database Performance
**Impact**: Medium
**Likelihood**: Low
**Mitigation**:
- Indexes on frequently-queried columns
- Automatic cleanup of old records
- Async logging option if needed
- Can disable via configuration

### Risk 2: Database Size Growth
**Impact**: Medium
**Likelihood**: Medium
**Mitigation**:
- MaxRecords configuration limit
- Automatic cleanup based on retention days
- Monitoring/alerting on table size
- Can truncate table if needed

### Risk 3: Breaking Existing Functionality
**Impact**: High
**Likelihood**: Low
**Mitigation**:
- Comprehensive test suite
- State logging failures don't crash service
- Can disable state logging via config
- Easy rollback via git revert

### Risk 4: Additional Persistence Slows Processing
**Impact**: Medium
**Likelihood**: Low
**Mitigation**:
- SQLite with WAL mode is fast (< 5ms per write)
- Performance testing before deployment
- Can make intermediate persistence optional
- Rollback plan available

---

## Timeline Estimate

### Phase 1: Database Schema (1-2 hours)
- Create migration
- Test up/down
- Verify indexes

### Phase 2: Configuration (30 minutes)
- Add config class
- Add to appsettings.json
- Test loading

### Phase 3: Domain Interface (30 minutes)
- Create interface
- Create record types
- Document

### Phase 4: Infrastructure Implementation (3-4 hours)
- Implement StateChangeLogger
- Write unit tests
- Test database operations

### Phase 5: Add Logging to Verification (2-3 hours)
- Modify VerificationService
- Add persistence points
- Test with slow drive

### Phase 6: Add Logging to Copy (2-3 hours)
- Modify FileCopyService
- Add persistence points
- Test with slow drive

### Phase 7: API Endpoints (1-2 hours)
- Add endpoints
- Test
- Document

### Phase 8: Integration Testing (2-3 hours)
- Full test suite
- debug-api-states.ps1 verification
- Performance testing
- Cleanup testing

**Total Estimate**: 12-18 hours of development time

---

## Notes

### Design Philosophy
- **Fail-safe**: State logging failures should never crash the service
- **Configurable**: All aspects controlled via appsettings.json
- **Observable**: Complete visibility into state transitions
- **Performant**: Minimal impact on processing time
- **Compliant**: Audit trail for medical imaging requirements

### Future Enhancements
- State change event streaming (SignalR for real-time UI updates)
- State transition metrics (Prometheus/OpenTelemetry)
- Anomaly detection (detect jobs stuck in states)
- State transition visualization (timeline graphs in UI)
- Export state history to external systems

---

**Last Updated**: 2025-10-10
**Status**: Planning Phase
**Next Step**: Review plan with user, get approval to proceed

