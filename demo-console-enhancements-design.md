# ForkerDotNet Console Demo Mode: Complete Design Document

**Version:** 1.0
**Date:** 2025-10-15
**Status:** Design Complete - Ready for Implementation
**Estimated Effort:** 10-12 hours

---

## Executive Summary

Build a **Console-driven Demo Mode** that orchestrates existing PowerShell scripts while providing real-time monitoring, external polling simulation (Sectra import), and evidence collection. Use **2-3GB real medical imaging files** for authentic clinical demonstrations.

### Key Principles

1. **Zero Risk Tolerance** - Demo must be bulletproof for Chief Clinical Safety Officer presentation
2. **Reuse Proven Infrastructure** - Leverage mature PowerShell scripts (no reimplementation)
3. **Real Production Code** - No fakes, no simulations (real files, real service, real database)
4. **Triple-Locked Safety** - Corruption injection only in Demo mode with explicit confirmation
5. **Single Source of Truth** - Console UI orchestrates everything, no manual script execution

---

## Architecture Overview

### Console as Demo Orchestrator

```
┌─────────────────────────────────────────────────────────┐
│  Console UI (localhost:8082) - Demo Mode Tab           │
│  ┌───────────────────────────────────────────────────┐ │
│  │ Pre-Flight Checks (13 validations)     [✓] Ready │ │
│  │ [Run Pre-Flight Check]                            │ │
│  └───────────────────────────────────────────────────┘ │
│  ┌───────────────────────────────────────────────────┐ │
│  │ Scenario Launcher                                 │ │
│  │ [▶ Scenario 1: End-to-End (5 min)]               │ │
│  │ [▶ Scenario 2: Corruption (4 min)]               │ │
│  │ [▶ Scenario 3: Concurrent Access (5 min)]        │ │
│  │ [▶ Scenario 4: Crash Recovery (5 min)] [Admin]   │ │
│  │ [▶ Scenario 5: Stability Detection (4 min)]      │ │
│  └───────────────────────────────────────────────────┘ │
│  ┌───────────────────────────────────────────────────┐ │
│  │ External Poller (Simulates Sectra Import)        │ │
│  │ Status: ● Stopped                                 │ │
│  │ [Start Poller] Interval: 5s  Dest: DestinationA │ │
│  │ Files Accessed: 0  Locked: 0  Errors: 0         │ │
│  └───────────────────────────────────────────────────┘ │
│  ┌───────────────────────────────────────────────────┐ │
│  │ Scenario Progress: Scenario 2 (Step 6/10)        │ │
│  │ ✓ File generated (2.3GB)                          │ │
│  │ ✓ Source hash calculated                          │ │
│  │ ✓ Moved to Input                                  │ │
│  │ ✓ Copy to DestinationA complete                   │ │
│  │ ✓ Copy to DestinationB complete                   │ │
│  │ ✓ Corruption injected (DestinationA only)        │ │
│  │ ⏳ Waiting for hash verification... (3s)          │ │
│  │ [View Live State History] [View Logs]            │ │
│  └───────────────────────────────────────────────────┘ │
│  ┌───────────────────────────────────────────────────┐ │
│  │ Real-Time State Monitor (Auto-Refresh: 500ms)    │ │
│  │ Active Jobs: 1                                    │ │
│  │ Job: 484759.svs (2.3GB)                          │ │
│  │   State: Partial                                  │ │
│  │   TargetA: Verifying (hash check in progress)    │ │
│  │   TargetB: Verified ✓                            │ │
│  └───────────────────────────────────────────────────┘ │
│  [Export Evidence] [Cleanup & Reset]                  │
└─────────────────────────────────────────────────────────┘
```

---

## Component Design

### 1. Pre-Flight Validation System

**Purpose:** Validate environment before allowing scenario execution

**13 Critical Checks:**

| # | Check Name | Type | Blocks Execution | Validation |
|---|------------|------|------------------|------------|
| 1 | Service Health | CRITICAL | Yes | GET /health returns 200 |
| 2 | Database Writable | CRITICAL | Yes | Test INSERT into forker.db |
| 3 | Input Directory | CRITICAL | Yes | Exists & writable |
| 4 | DestinationA | CRITICAL | Yes | Exists & writable |
| 5 | DestinationB | CRITICAL | Yes | Exists & writable |
| 6 | Quarantine Directory | CRITICAL | Yes | Exists & writable |
| 7 | Reservoir Directory | CRITICAL | Yes | Exists & writable |
| 8 | Disk Space (Input) | WARNING | No | >20GB available |
| 9 | Disk Space (DestA) | WARNING | No | >20GB available |
| 10 | Disk Space (DestB) | WARNING | No | >20GB available |
| 11 | Environment Variable | CRITICAL | Yes | ASPNETCORE_ENVIRONMENT=Demo |
| 12 | StateChangeLogging | WARNING | No | Enabled in config |
| 13 | No Active Jobs | WARNING | No | 0 jobs in Discovered/Queued/InProgress |

**Implementation:**

```go
type PreFlightCheck struct {
    Name     string `json:"name"`
    Status   string `json:"status"` // "pass", "fail", "warning"
    Message  string `json:"message"`
    Critical bool   `json:"critical"`
    Duration int64  `json:"duration_ms"`
}

func RunPreFlightChecks(ctx context.Context) []PreFlightCheck {
    // Execute all 13 checks
    // Return array with results
    // Calculate summary: canExecute = no critical failures
}
```

**API Endpoint:**
- `GET /api/demo/preflight` - Returns check results + summary

**UI Display:**
- Table format with ✓/✗/⚠ indicators
- Green (pass), Red (fail), Yellow (warning)
- "Ready" indicator if all critical checks pass
- Clear error messages with remediation steps

---

### 2. Scenario Orchestration Engine

**Purpose:** Execute existing PowerShell scripts from Console UI

**Design Philosophy:** Don't reimplement - orchestrate!

**Flow:**
```
Console UI → POST /api/demo/run-scenario → Go Backend
  → exec.Command("powershell.exe", "-File", "Run-ScenarioX.ps1")
  → Parse stdout/stderr in real-time
  → Stream progress via SSE to UI
  → Update progress indicators
  → Display completion status
```

**PowerShell Output Parsing:**

```go
// Detect step progression
if strings.HasPrefix(line, "STEP ") {
    // Extract: "STEP 3: Copying file to destinations"
    stepNum := extractNumber(line)
    message := extractMessage(line)
    emit({type: "step", step: stepNum, message: message})
}

// Detect status updates
if strings.Contains(line, "[OK]") {
    emit({type: "status", status: "success", message: extractMessage(line)})
}
if strings.Contains(line, "[ERROR]") {
    emit({type: "status", status: "error", message: extractMessage(line)})
}
if strings.Contains(line, "[WARN]") {
    emit({type: "status", status: "warning", message: extractMessage(line)})
}

// Detect progress indicators
if strings.Contains(line, "%") {
    emit({type: "progress", message: line})
}
```

**Benefits:**
- ✅ Zero reimplementation risk
- ✅ Reuse 400+ lines of proven PowerShell code per scenario
- ✅ Single codebase (scripts work standalone OR via Console)
- ✅ Maintenance simplified (update scripts, not two versions)

**API Endpoints:**
- `POST /api/demo/run-scenario` - Execute scenario (returns SSE stream)
- `POST /api/demo/cancel-scenario` - Cancel running scenario

**UI Components:**
- Scenario buttons (5 buttons, one per scenario)
- Progress panel with step-by-step updates
- Real-time status indicators
- Completion summary

---

### 3. External Poller (Sectra Import Simulation)

**Purpose:** Simulate external system (PACS/Sectra) continuously reading from DestinationA

**Why Critical:** Proves non-locking file operations (key safety requirement)

**Design:**

```go
type ExternalPoller struct {
    enabled  bool
    interval time.Duration  // e.g., 5 seconds
    path     string         // C:\ForkerDemo\DestinationA

    // Statistics
    filesAccessed    int64
    filesLocked      int64
    errors           int64
    lastAccessTime   time.Time
    lastAccessedFile string
}

func (p *ExternalPoller) pollOnce() {
    files := listFiles(p.path)

    for _, file := range files {
        // Skip temp files
        if strings.HasSuffix(file, ".forker-tmp") {
            continue
        }

        // Attempt non-locking read
        f, err := os.OpenFile(file, os.O_RDONLY, 0)

        if err != nil {
            // FILE LOCKED - CRITICAL FAILURE!
            p.filesLocked++
            p.errors++
            log.Printf("[POLLER] ✗ ERROR: File locked: %s", file)
        } else {
            // Success - no lock
            buf := make([]byte, 1024)
            f.Read(buf)
            f.Close()

            p.filesAccessed++
            log.Printf("[POLLER] ✓ Accessed %s", file)
        }
    }
}
```

**Features:**
- Start/Stop button controlled
- Configurable interval (default 5s)
- Real-time statistics display
- Alert if any file locked (red warning)
- Can run during any scenario (optional)

**API Endpoints:**
- `POST /api/demo/poller/start` - Start polling
- `POST /api/demo/poller/stop` - Stop polling
- `GET /api/demo/poller/stats` - Get current statistics

**UI Display:**
```
External Poller (Simulates Sectra Import)
Status: ● Running
Files Accessed: 47
Files Locked: 0 ← If >0, display in RED with animation
Errors: 0
Last Access: 484759.svs (2 seconds ago)

[Stop Poller]
```

---

### 4. Real-Time State Monitoring

**Purpose:** Show live job state transitions during demo execution

**Already 90% Complete!** From Phase 3:
- ✅ State history API working
- ✅ Console fetches state transitions
- ✅ Timeline display with timestamps
- ✅ Auto-refresh every 2 seconds

**Enhancements Needed:**

```javascript
// Accelerate refresh during demo
let refreshRate = 2000; // Normal: 2 seconds
let demoModeActive = false;

function enterDemoMode() {
    demoModeActive = true;
    refreshRate = 500; // Demo: 500ms for real-time feel
    restartAutoRefresh();
}

// Highlight current demo job
if (job.jobId === currentDemoJobId) {
    html += '<div class="demo-highlight" style="border: 3px solid #0066cc; background: #e6f2ff;">';
    // ... render job details
    html += '</div>';
}
```

**Benefits:**
- Leverages existing state history infrastructure
- No new backend code required
- Just accelerate refresh rate during demos
- Visually highlight job being demonstrated

---

### 5. Evidence Collection System

**Purpose:** Export governance-ready evidence package after demo

**Contents:**
1. **Database Snapshot** - forker-snapshot.db (complete state)
2. **Log Files** - All forker-*.txt logs from demo run
3. **State History JSON** - Complete transitions for recent jobs
4. **README.txt** - Verification steps and instructions
5. **GOVERNANCE-CHECKLIST.txt** - Clinical safety checklist for CCSO

**Implementation:**

```go
func (e *EvidenceCollector) Collect(scenarioName, outputPath string) error {
    timestamp := time.Now().Format("20060102-150405")
    evidenceDir := filepath.Join(outputPath,
        fmt.Sprintf("Evidence-%s-%s", scenarioName, timestamp))

    os.MkdirAll(evidenceDir, 0755)

    // 1. Copy database
    copyFile("C:\\ForkerDemo\\forker.db",
             filepath.Join(evidenceDir, "forker-snapshot.db"))

    // 2. Copy logs
    logs, _ := filepath.Glob("C:\\ForkerDemo\\Logs\\forker-*.txt")
    for _, log := range logs {
        copyFile(log, filepath.Join(evidenceDir, "Logs", filepath.Base(log)))
    }

    // 3. Export state history (last 10 jobs)
    stateHistory := fetchStateHistoryFromAPI(10)
    saveJSON(stateHistory, filepath.Join(evidenceDir, "state-history.json"))

    // 4. Generate README
    readme := generateReadmeText(scenarioName, timestamp)
    saveFile(readme, filepath.Join(evidenceDir, "README.txt"))

    // 5. Generate checklist
    checklist := generateChecklistText(scenarioName)
    saveFile(checklist, filepath.Join(evidenceDir, "GOVERNANCE-CHECKLIST.txt"))

    return nil
}
```

**README.txt Template:**
```
ForkerDotNet Demo Evidence Package
=====================================

Scenario: Scenario2-Corruption
Generated: 2025-10-15 15:30:00
Location: C:\ForkerDemo

Contents:
---------
1. forker-snapshot.db      - SQLite database snapshot
2. Logs\                   - Service log files
3. state-history.json      - Complete state transitions
4. GOVERNANCE-CHECKLIST.txt - Clinical safety checklist

Verification Steps:
------------------
1. Open forker-snapshot.db in DB Browser for SQLite
2. Query: SELECT * FROM FileJobs ORDER BY CreatedAt DESC LIMIT 10
3. Verify all jobs reached VERIFIED state (or QUARANTINED)
4. Query: SELECT * FROM StateChangeLog ORDER BY Timestamp DESC
5. Verify state transitions: DISCOVERED → QUEUED → INPROGRESS → VERIFIED

Key Safety Properties Demonstrated:
-----------------------------------
- SHA-256 hash verification for all files
- Dual-target replication with atomic operations
- State machine enforcement
- Crash recovery with SQLite WAL mode
- Non-locking file operations
- Quarantine on hash mismatch
```

**API Endpoint:**
- `POST /api/demo/export-evidence` - Export evidence to user-defined path

**UI:**
- "Export Evidence" button
- Scenario name input
- Output path selector (default: Desktop)
- Success message with folder location

---

### 6. Cleanup & Reset System

**Purpose:** Prepare environment between scenarios

**Cleanup Process:**

```
1. Wait for all jobs to complete (timeout 2 minutes)
   - Poll API for jobs in Discovered/Queued/InProgress/Partial
   - Block until 0 active jobs OR timeout

2. Clean Input directory
   - Delete all files (*.svs, *.test, *.tiff, etc.)

3. Clean Reservoir directory
   - Delete all generated test files

4. Optionally clean Destinations
   - User choice: Keep previous files or clean
   - DestinationA, DestinationB

5. Clean Quarantine directory
   - Delete all quarantined files

6. Verify no active jobs
   - Final check: 0 jobs in active states
   - Block scenario execution if jobs still active
```

**Safety:**
- Never delete while jobs running (wait for completion)
- User confirmation for destination cleanup
- Log all deleted files for audit trail
- Report cleanup summary (X files deleted, Y directories cleaned)

**API Endpoint:**
- `POST /api/demo/cleanup` - Clean environment (optional: cleanDestinations flag)

**UI:**
- "Cleanup & Reset" button
- Checkbox: "Also clean destination directories"
- Progress indicator during cleanup
- Summary: "Cleanup complete - deleted 15 files, ready for next scenario"

---

## File Size Strategy (Real Medical Imaging Files)

### Recommended Sizes Per Scenario

| Scenario | File Size | Rationale | Demo Time |
|----------|-----------|-----------|-----------|
| 1. End-to-End | 2GB | Realistic clinical file, manageable demo time | ~5 min |
| 2. Corruption | 2.3GB | Same as user's test files, proves real-world handling | ~4 min |
| 3. Concurrent Access | 2GB | Need time for mid-copy access demonstration | ~5 min |
| 4. Crash Recovery | 3GB | Maximize crash recovery demo impact | ~5 min |
| 5. Stability Detection | 500MB → 2GB growing | Incremental growth simulation | ~4 min |

### Timing Estimates (2-3GB Files)

**Per-File Processing:**
- Stability detection: 5-10 seconds
- Copy to TargetA: 3-4 seconds (1GB/min throughput)
- Copy to TargetB: 3-4 seconds (parallel)
- Verify TargetA: 1-2 seconds (SHA-256)
- Verify TargetB: 1-2 seconds
- **Total: 8-12 seconds per file**

**Demo Scenarios:**
- Scenario 1: 1 file × 12s + setup/verify = ~5 min
- Scenario 2: 1 file × 12s + corruption inject + detect = ~4 min
- Scenario 3: 1 file × 12s + concurrent access demo = ~5 min
- Scenario 4: 1 file × 12s + crash + recovery = ~5 min
- Scenario 5: 1 growing file + stability wait = ~4 min

---

## Safety & Failure Handling

### 46 Identified Failure Modes

**Category Breakdown:**
- File System Failures: 11 scenarios
- Service State Failures: 9 scenarios
- Database Failures: 7 scenarios
- State Machine Failures: 8 scenarios
- Hash Verification Failures: 5 scenarios
- Timing & Concurrency: 6 scenarios

### Mitigation Strategy

**Prevention (80% of failures):**
- Pre-flight checks catch: directories, permissions, disk space, environment, config, service health, database access
- Block execution if critical checks fail

**Detection (15% of failures):**
- Timeout monitoring for stuck jobs
- Health endpoint polling for service crashes
- Error parsing from PowerShell output
- State machine validation via API

**Recovery (5% of failures):**
- Graceful error messages with remediation
- Automatic cleanup on failure
- Evidence collection even on failure
- Clear "what to do next" instructions

### Error Display Strategy

```
┌────────────────────────────────────────┐
│ ✗ Demo Execution Error                │
├────────────────────────────────────────┤
│ Category: Service State                │
│ Severity: ERROR                        │
│                                        │
│ The ForkerDotNet service stopped       │
│ responding during scenario execution.  │
│                                        │
│ What to do:                           │
│ 1. Check if service is still running  │
│ 2. Review logs in C:\ForkerDemo\Logs  │
│ 3. Restart service and try again      │
│                                        │
│ [View Logs] [Restart Service] [Abort] │
└────────────────────────────────────────┘
```

---

## Corruption Safety (Triple-Locked)

### Why This Matters

Corruption injection is **intentional for demo** but must be **impossible in production**.

### Three Layers of Protection

**Layer 1: Environment Check**
```powershell
# PowerShell script checks
if ($env:ASPNETCORE_ENVIRONMENT -ne "Demo") {
    throw "Corruption injection only allowed in Demo mode"
}
```

**Layer 2: Configuration Flag**
```json
// appsettings.Demo.json
"Testing": {
    "EnableTestMode": true,
    "AllowCorruptionInjection": true  // Must be explicit
}
```

**Layer 3: Explicit Confirmation**
```powershell
# PowerShell function
function New-CorruptedFile {
    param([string]$SourcePath, [string]$DestinationPath)

    # Triple check
    if ($env:ASPNETCORE_ENVIRONMENT -ne "Demo") { throw }
    if (-not $AllowCorruptionInjection) { throw }
    if (-not $ExplicitConfirmation) { throw }

    # Corrupt file (XOR byte at midpoint)
    $bytes = [System.IO.File]::ReadAllBytes($SourcePath)
    $midpoint = [Math]::Floor($bytes.Length / 2)
    $bytes[$midpoint] = $bytes[$midpoint] -bxor 0xFF
    [System.IO.File]::WriteAllBytes($DestinationPath, $bytes)
}
```

### Result

- ❌ Cannot run in Production (environment check)
- ❌ Cannot run if config flag disabled
- ❌ Cannot run without explicit confirmation
- ✅ Logged explicitly for audit trail
- ✅ Only corrupts destination (source intact)
- ✅ Only corrupts DestinationA (DestinationB clean for comparison)

---

## PowerShell Scripts as Integration Tests

### Dual Purpose Design

**Scenario Scripts Can Be:**
1. **Executed from Console UI** - For CCSO demos
2. **Executed standalone** - For integration testing / CI/CD

### Integration Test Harness

**New Script:** `scripts/Run-All-Scenarios-Integration-Test.ps1`

```powershell
# Run all 5 scenarios sequentially
# Track pass/fail for each
# Generate summary report
# Exit code 0 = all passed, 1 = any failed

$results = @()

$results += Test-Scenario -ScenarioNum 1 -Description "End-to-End"
$results += Test-Scenario -ScenarioNum 2 -Description "Corruption"
$results += Test-Scenario -ScenarioNum 3 -Description "Concurrent Access"
$results += Test-Scenario -ScenarioNum 4 -Description "Crash Recovery"
$results += Test-Scenario -ScenarioNum 5 -Description "Stability Detection"

# Report: X passed, Y failed
# Total duration: Z seconds
```

**Usage:**
```powershell
# CI/CD Integration
.\scripts\Run-All-Scenarios-Integration-Test.ps1

# Stop on first failure
.\scripts\Run-All-Scenarios-Integration-Test.ps1 -StopOnFailure
```

**Benefits:**
- ✅ Scenarios validate entire system (true integration tests)
- ✅ Can run nightly in CI/CD
- ✅ Regression testing for production readiness
- ✅ Same code path as demos (no separate test implementation)

---

## Implementation Approach

### Phase 1: Proof of Concept (4 hours)

**Goal:** Console can launch Scenario 1 and display progress

**Tasks:**
- Implement Pre-Flight Validation Dashboard (Task 4.1)
- Implement Scenario Orchestration for Scenario 1 only (Task 4.2 partial)
- Test with 2GB file
- Verify PowerShell output parsing works
- Confirm SSE streaming to UI

**Success Criteria:**
- Click "Run Scenario 1" → PowerShell executes → Progress updates in real-time → Completion message

### Phase 2: Critical Features (4 hours)

**Goal:** Add external poller and complete all scenarios

**Tasks:**
- Implement External Poller (Task 4.3)
- Complete Scenario Orchestration for scenarios 2-5 (Task 4.2 complete)
- Enhance Real-Time State Monitoring (Task 4.4)

**Success Criteria:**
- All 5 scenarios launch from Console
- External poller proves non-locking
- State transitions visible in real-time

### Phase 3: Evidence & Cleanup (3 hours)

**Goal:** Evidence export and cleanup automation

**Tasks:**
- Implement Evidence Export (Task 4.5)
- Implement Cleanup & Reset (Task 4.6)
- End-to-end testing

**Success Criteria:**
- Evidence package exports successfully
- Cleanup prepares environment for next scenario
- Run full suite 5 times without errors

### Phase 4: Polish & Practice (2 hours)

**Goal:** Production-ready for CCSO presentation

**Tasks:**
- UI polish (styling, error messages, loading indicators)
- Create presenter notes
- Practice full demo flow
- Document troubleshooting steps

**Success Criteria:**
- Demo runs flawlessly 5 times in a row
- Clear error messages for all failure modes
- Presenter confident in flow

**Total: 13 hours (includes testing and polish)**

---

## Technology Stack

### Backend (Go)
- **Language:** Go 1.23+
- **HTTP Server:** stdlib `net/http`
- **Process Execution:** `os/exec` for PowerShell
- **File Operations:** `os`, `io/ioutil`, `path/filepath`
- **JSON Handling:** `encoding/json`

### Frontend (JavaScript)
- **No Framework** - Pure vanilla JavaScript
- **SSE:** EventSource API for real-time updates
- **Fetch API:** For REST calls
- **LocalStorage:** For UI preferences

### Integration
- **PowerShell 5.1+** - Existing demo scripts
- **C# Service** - ForkerDotNet on port 8081
- **SQLite** - Database at C:\ForkerDemo\forker.db

---

## API Specification

### Demo Endpoints

| Method | Endpoint | Purpose | Response |
|--------|----------|---------|----------|
| GET | /api/demo/preflight | Run pre-flight checks | JSON: {checks: [], summary: {}} |
| POST | /api/demo/run-scenario | Execute scenario (SSE stream) | SSE: data: {type, message, status} |
| POST | /api/demo/cancel-scenario | Cancel running scenario | JSON: {status: "cancelled"} |
| POST | /api/demo/poller/start | Start external poller | JSON: {status: "started"} |
| POST | /api/demo/poller/stop | Stop external poller | JSON: {status: "stopped"} |
| GET | /api/demo/poller/stats | Get poller statistics | JSON: {filesAccessed, filesLocked, errors} |
| POST | /api/demo/export-evidence | Export evidence package | JSON: {status, path} |
| POST | /api/demo/cleanup | Cleanup environment | JSON: {status: "success"} |

### Request/Response Examples

**Run Scenario:**
```json
POST /api/demo/run-scenario
Content-Type: application/json

{
  "scenario_num": 2
}

Response (SSE stream):
data: {"type":"step","step":1,"message":"Creating test file"}

data: {"type":"status","status":"success","message":"File created (2.3GB)"}

data: {"type":"progress","message":"Target A: 45%"}

data: {"type":"complete","message":"Scenario execution finished"}
```

**Export Evidence:**
```json
POST /api/demo/export-evidence
Content-Type: application/json

{
  "scenario_name": "Scenario2-Corruption",
  "output_path": "C:\\Users\\John\\Desktop"
}

Response:
{
  "status": "success",
  "path": "C:\\Users\\John\\Desktop\\Evidence-Scenario2-Corruption-20251015-153000"
}
```

---

## UI Design Specifications

### Layout

**Three-Column Layout:**
```
┌─────────────────────────────────────────────┐
│ Header (Tabs: Folders | Transactions | Demo)│
├─────────────────────────────────────────────┤
│                                             │
│  [Left Panel: 40%]   [Right Panel: 60%]    │
│                                             │
│  • Pre-Flight Checks  • Scenario Progress   │
│  • Scenario Buttons   • Real-Time Monitor   │
│  • External Poller    • State History       │
│  • Controls           • Logs                │
│                                             │
└─────────────────────────────────────────────┘
```

### Color Scheme

- **Success:** #28a745 (green)
- **Error:** #dc3545 (red)
- **Warning:** #ffc107 (yellow)
- **Info:** #17a2b8 (cyan)
- **Primary:** #0066cc (blue)
- **Highlight:** #e6f2ff (light blue background)

### Status Indicators

- ✓ Success (green checkmark)
- ✗ Failure (red X)
- ⚠ Warning (yellow triangle)
- ⏳ In Progress (spinning loader or timer)
- ● Running (green dot)
- ● Stopped (gray dot)

### Accessibility

- Keyboard navigation support
- ARIA labels for screen readers
- Clear focus indicators
- High contrast mode support

---

## Testing Strategy

### Unit Tests (Go)

```go
// Test pre-flight checks
func TestPreFlightCheckServiceHealth(t *testing.T) {
    check := checkServiceHealth("http://localhost:8081/api/monitoring/health")
    assert.Equal(t, "pass", check.Status)
}

// Test PowerShell output parsing
func TestParsePowerShellStepLine(t *testing.T) {
    line := "STEP 3: Copying file to destinations"
    output := parseLine(line)
    assert.Equal(t, "step", output.Type)
    assert.Equal(t, 3, output.Step)
}
```

### Integration Tests (PowerShell)

```powershell
# Run all scenarios as integration test
.\scripts\Run-All-Scenarios-Integration-Test.ps1

# Expected: 5/5 passed, exit code 0
```

### Manual Testing (CCSO Demo Rehearsal)

**Checklist:**
- [ ] Pre-flight checks all pass
- [ ] Scenario 1 completes successfully
- [ ] Scenario 2 shows corruption detection
- [ ] External poller shows no locks
- [ ] State transitions visible in real-time
- [ ] Evidence exports successfully
- [ ] Cleanup prepares for next run
- [ ] Run full suite 5 times → 5/5 success

---

## Success Criteria

### Pre-Demo Checklist
- [ ] All 13 pre-flight checks passing
- [ ] Console UI accessible at localhost:8082/demo
- [ ] ForkerDotNet service running in Demo mode
- [ ] Disk space >60GB available
- [ ] File Explorer grid working
- [ ] External poller tested

### During Demo
- [ ] Scenario launches from single button click
- [ ] Real-time progress updates every 500ms
- [ ] State transitions visible
- [ ] External poller shows no locks
- [ ] PowerShell output parsed correctly
- [ ] Any errors handled gracefully

### Post-Demo
- [ ] Evidence package exports successfully
- [ ] Contains: DB snapshot, logs, state history, README, checklist
- [ ] Cleanup completes without errors
- [ ] System ready for next scenario

### Zero-Failure Safeguards
1. ✅ Pre-flight blocks execution if critical checks fail
2. ✅ PowerShell scripts proven (mature, field-tested)
3. ✅ Corruption triple-locked
4. ✅ Timeout detection for stuck jobs
5. ✅ Error categorization with remediation
6. ✅ Graceful degradation
7. ✅ Evidence auto-collected
8. ✅ Cleanup automated

---

## Maintenance & Future Enhancements

### Ongoing Maintenance

**PowerShell Scripts:**
- Update as scenarios evolve
- Add new scenarios as needed
- Console automatically discovers new scripts

**Go Backend:**
- Update parsing logic if PowerShell output format changes
- Add new API endpoints for new features
- Monitor performance with large files

### Potential Enhancements

**Phase 5 (Future):**
- Stress Test Mode (100+ concurrent files)
- Soak Test Mode (24-48 hour continuous operation)
- Scenario recording/playback
- Automated screenshot capture
- Video export of demo runs
- Remote demo capability (screen sharing integration)

---

## Glossary

**CCSO** - Chief Clinical Safety Officer
**Demo Mode** - ForkerDotNet running with ASPNETCORE_ENVIRONMENT=Demo
**Evidence Package** - Collection of DB snapshot, logs, state history for governance review
**External Poller** - Simulated PACS/Sectra system reading destination files
**Pre-Flight Checks** - 13 validation checks before scenario execution
**Reservoir** - Directory where test files are generated before moving to Input
**Scenario** - One of 5 predefined demo workflows (End-to-End, Corruption, etc.)
**SSE** - Server-Sent Events (one-way streaming from server to client)
**State History** - Complete timeline of job state transitions with timestamps

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-10-15 | Claude Code | Initial design based on user requirements and repository analysis |

---

**Next Steps:** See [demo-console-enhancements-task-list.md](demo-console-enhancements-task-list.md) for implementation tasks.
