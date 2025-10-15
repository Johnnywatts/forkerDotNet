# ForkerDotNet Console Demo Mode - Task List

**Status:** Ready for Implementation
**Estimated Total Effort:** 10-12 hours
**Priority:** High (Required for CCSO presentation)
**Target Completion:** Before CCSO demo

---

## Implementation Phases

### Phase 1: Proof of Concept (4 hours) 🎯 NEXT

**Goal:** Console can launch Scenario 1 and display progress

#### Task 4.1: Pre-Flight Validation Dashboard (2 hours) ⏳ TODO
**Priority:** CRITICAL
**Dependencies:** None

**Files to Create:**
- [ ] `src/Forker.Console/internal/demo/preflight.go` - Validation checks
- [ ] `src/Forker.Console/internal/server/handlers_demo.go` - Demo page handlers
- [ ] `src/Forker.Console/web/templates/demo.html` - Demo UI template

**Subtasks:**
- [ ] Implement 13 pre-flight check functions
  - [ ] checkServiceHealth() - GET /health endpoint
  - [ ] checkDatabaseWritable() - Test INSERT operation
  - [ ] checkDirectory() - 5 directories (Input, DestA, DestB, Quarantine, Reservoir)
  - [ ] checkDiskSpace() - 3 disk space checks (>20GB each)
  - [ ] checkEnvVar() - ASPNETCORE_ENVIRONMENT=Demo
  - [ ] checkConfigSetting() - StateChangeLogging.Enabled
  - [ ] checkNoActiveJobs() - Query API for active jobs
- [ ] Create PreFlightCheck struct
- [ ] Implement RunPreFlightChecks() function
- [ ] Add API endpoint: GET /api/demo/preflight
- [ ] Create demo.html template with check results table
- [ ] Add "Run Pre-Flight Check" button
- [ ] Display results with ✓/✗/⚠ indicators
- [ ] Calculate summary (canExecute flag)
- [ ] Test with missing directory (should fail)
- [ ] Test with service stopped (should fail)
- [ ] Test with all passing (should show "Ready")

**Success Criteria:**
- All 13 checks run and return results
- Critical failures block scenario execution
- Warnings allow execution with user confirmation
- Clear error messages with remediation steps
- Response time <5 seconds for all checks

**Testing:**
```bash
# Test pre-flight endpoint
curl http://localhost:8082/api/demo/preflight

# Expected: JSON with 13 checks, summary.canExecute = true/false
```

---

#### Task 4.2: Scenario Orchestration (Scenario 1 Only) (2 hours) ⏳ TODO
**Priority:** CRITICAL
**Dependencies:** Task 4.1 (soft dependency)

**Files to Create:**
- [ ] `src/Forker.Console/internal/demo/executor.go` - PowerShell execution
- [ ] `src/Forker.Console/internal/demo/parser.go` - Output parsing

**Subtasks:**
- [ ] Create ScenarioExecutor struct
- [ ] Implement NewScenarioExecutor(scenarioNum) constructor
- [ ] Implement Execute() method with exec.Command
- [ ] Configure PowerShell execution:
  - [ ] Command: `powershell.exe -ExecutionPolicy Bypass -File scripts\Run-Scenario1-EndToEnd.ps1`
  - [ ] Capture stdout and stderr pipes
  - [ ] Stream output to channel
- [ ] Implement parseOutput() goroutine
  - [ ] Detect "STEP N:" lines → emit step event
  - [ ] Detect "[OK]" → emit success status
  - [ ] Detect "[ERROR]" → emit error status
  - [ ] Detect "[WARN]" → emit warning status
  - [ ] Detect "%" → emit progress event
- [ ] Create ScenarioOutput struct
- [ ] Add API endpoint: POST /api/demo/run-scenario (SSE stream)
- [ ] Update demo.html with Scenario 1 button
- [ ] Add JavaScript runScenario() function
- [ ] Implement SSE EventSource reader
- [ ] Update progress panel with real-time updates
- [ ] Test with Scenario 1 (2GB file)

**Success Criteria:**
- Click "Run Scenario 1" button
- PowerShell script executes
- Console UI shows real-time progress
- Steps appear as PowerShell progresses
- Completion message when done
- No errors or hangs

**Testing:**
```bash
# Manual test: Click button in UI
# Watch for:
# - "STEP 1: Creating test file"
# - "[OK] File created (2GB)"
# - Progress indicators updating
# - "Scenario execution finished"
```

---

### Phase 2: Critical Features (4 hours)

**Goal:** Add external poller and complete all scenarios

#### Task 4.3: External Poller (Sectra Import Simulation) (1.5 hours) ⏳ TODO
**Priority:** HIGH
**Dependencies:** None (can work in parallel with Task 4.2)

**Files to Create:**
- [ ] `src/Forker.Console/internal/demo/poller.go` - Polling implementation

**Subtasks:**
- [ ] Create ExternalPoller struct
  - [ ] Fields: enabled, interval, path, statistics
  - [ ] Mutex for thread-safe stats
- [ ] Implement NewExternalPoller() constructor
- [ ] Implement Start() method
  - [ ] Create context with cancellation
  - [ ] Launch poll() goroutine
  - [ ] Ticker for interval-based polling
- [ ] Implement Stop() method
- [ ] Implement pollOnce() method
  - [ ] List files in DestinationA
  - [ ] Skip .forker-tmp files
  - [ ] Attempt os.OpenFile() with O_RDONLY
  - [ ] Track success/failure statistics
  - [ ] Log access attempts
- [ ] Implement GetStats() method
- [ ] Add API endpoints:
  - [ ] POST /api/demo/poller/start
  - [ ] POST /api/demo/poller/stop
  - [ ] GET /api/demo/poller/stats
- [ ] Add UI panel to demo.html
  - [ ] Status indicator (● Running / ● Stopped)
  - [ ] Start/Stop buttons
  - [ ] Interval selector (default 5s)
  - [ ] Statistics display
- [ ] Add JavaScript polling for stats updates (every 2s)
- [ ] Highlight errors in RED if filesLocked > 0

**Success Criteria:**
- Poller starts when button clicked
- Reads files every 5 seconds
- Shows "Files Accessed: X" incrementing
- Shows "Files Locked: 0" (always 0 = success!)
- No errors reading files during ForkerDotNet operations
- Poller stops cleanly when button clicked

**Testing:**
```bash
# Manual test:
# 1. Drop 2GB file in Input
# 2. Start poller
# 3. Watch "Files Accessed" increment
# 4. Verify "Files Locked: 0" (no locks!)
# 5. Stop poller
```

---

#### Task 4.2 (Continued): Complete All Scenarios (1.5 hours) ⏳ TODO
**Priority:** HIGH
**Dependencies:** Task 4.2 (Scenario 1) complete

**Subtasks:**
- [ ] Add Scenario 2 button (Corruption Detection)
- [ ] Add Scenario 3 button (Concurrent Access)
- [ ] Add Scenario 4 button (Crash Recovery) - mark [Admin Required]
- [ ] Add Scenario 5 button (Stability Detection)
- [ ] Test each scenario individually
  - [ ] Scenario 2: Verify corruption detection shown
  - [ ] Scenario 3: Verify non-locking demo
  - [ ] Scenario 4: Verify crash recovery (requires Admin)
  - [ ] Scenario 5: Verify stability detection
- [ ] Verify parsing works for all scenarios
- [ ] Handle scenario-specific output variations

**Success Criteria:**
- All 5 scenarios launch from Console
- Each shows progress correctly
- Completion messages for all
- Scenario 4 warns if not Admin
- No PowerShell errors

**Testing:**
```bash
# Run each scenario once
# Verify completion
# Check for parsing errors in logs
```

---

#### Task 4.4: Real-Time State Monitoring Enhancement (1 hour) ⏳ TODO
**Priority:** MEDIUM
**Dependencies:** Existing state history from Phase 3

**Files to Modify:**
- [ ] `src/Forker.Console/internal/server/handlers_api.go` - Refresh rate logic
- [ ] Existing JavaScript in handlers_api.go - Demo mode flag

**Subtasks:**
- [ ] Add demo mode detection in Go backend
  - [ ] Check ASPNETCORE_ENVIRONMENT == Demo
- [ ] Add getRefreshRate() function
  - [ ] Return 500ms if demo mode
  - [ ] Return 2000ms if normal mode
- [ ] Update JavaScript with demo mode flag
  - [ ] Add enterDemoMode() function
  - [ ] Set refreshRate = 500ms
  - [ ] Add currentDemoJobId tracking
- [ ] Highlight current demo job in UI
  - [ ] Add CSS class "demo-highlight"
  - [ ] Blue border and light blue background
- [ ] Auto-enter demo mode when scenario starts
- [ ] Auto-exit demo mode when scenario completes

**Success Criteria:**
- State transitions update every 500ms during demo
- Current demo job highlighted visually
- Returns to 2s refresh after demo
- No performance issues with 500ms refresh

**Testing:**
```bash
# Start Scenario 1
# Watch Transactions pane
# Verify job appears with blue highlight
# Verify states update quickly (< 1s visible delay)
```

---

### Phase 3: Evidence & Cleanup (3 hours)

**Goal:** Evidence export and cleanup automation

#### Task 4.5: Evidence Export System (1 hour) ⏳ TODO
**Priority:** HIGH
**Dependencies:** None (can work in parallel)

**Files to Create:**
- [ ] `src/Forker.Console/internal/demo/evidence.go` - Collection logic

**Subtasks:**
- [ ] Create EvidenceCollector struct
- [ ] Implement Collect() method
  - [ ] Create evidence directory with timestamp
  - [ ] Copy database snapshot (forker.db → forker-snapshot.db)
  - [ ] Copy log files from C:\ForkerDemo\Logs
  - [ ] Fetch state history from API (last 10 jobs)
  - [ ] Export state history as JSON
  - [ ] Generate README.txt with verification steps
  - [ ] Generate GOVERNANCE-CHECKLIST.txt
- [ ] Implement generateReadmeText()
- [ ] Implement generateChecklistText()
- [ ] Add copyFile() helper function
- [ ] Add API endpoint: POST /api/demo/export-evidence
- [ ] Add UI panel to demo.html
  - [ ] Scenario name input
  - [ ] Output path input (default: Desktop)
  - [ ] "Export Evidence" button
- [ ] Add JavaScript exportEvidence() function
- [ ] Show success message with folder path

**Success Criteria:**
- Evidence exports to user-specified path
- Contains all 5 components (DB, logs, JSON, README, checklist)
- README has clear verification steps
- Checklist ready for CCSO signature
- Export completes in <10 seconds

**Testing:**
```bash
# After Scenario 2 completes:
# 1. Click "Export Evidence"
# 2. Specify output path
# 3. Verify folder created with timestamp
# 4. Open README.txt - verify readable
# 5. Open forker-snapshot.db in DB Browser - verify data
# 6. Open state-history.json - verify transitions
```

---

#### Task 4.6: Cleanup & Reset System (1 hour) ⏳ TODO
**Priority:** HIGH
**Dependencies:** None

**Files to Create:**
- [ ] `src/Forker.Console/internal/demo/cleanup.go` - Cleanup logic

**Subtasks:**
- [ ] Create CleanupManager struct
- [ ] Implement CleanupBeforeScenario() method
  - [ ] waitForIdleState() - wait for 0 active jobs (2min timeout)
  - [ ] cleanDirectory(Input) - delete all files
  - [ ] cleanDirectory(Reservoir) - delete test files
  - [ ] cleanDirectory(Quarantine) - delete quarantined files
  - [ ] Optional: cleanDirectory(DestinationA/B) based on flag
  - [ ] checkActiveJobs() - verify 0 active after cleanup
- [ ] Implement waitForIdleState() with timeout
- [ ] Implement checkActiveJobs() via API query
- [ ] Implement cleanDirectory() helper
- [ ] Add API endpoint: POST /api/demo/cleanup
- [ ] Add UI panel to demo.html
  - [ ] "Cleanup & Reset" button
  - [ ] Checkbox: "Also clean destination directories"
  - [ ] Progress indicator
- [ ] Add JavaScript cleanup() function
- [ ] Show summary: "Deleted X files, ready for next scenario"

**Success Criteria:**
- Cleanup waits for jobs to complete
- All specified directories cleaned
- No files left in Input/Reservoir/Quarantine
- User can opt to keep destination files
- Reports summary of actions taken
- Completes in <30 seconds (if no active jobs)

**Testing:**
```bash
# After Scenario 1 completes:
# 1. Drop 3 files in Input manually
# 2. Click "Cleanup & Reset"
# 3. Verify Input directory empty
# 4. Verify Reservoir empty
# 5. Verify "Ready for next scenario" message
```

---

#### Task 4.7: End-to-End Testing (1 hour) ⏳ TODO
**Priority:** CRITICAL
**Dependencies:** All previous tasks

**Subtasks:**
- [ ] Test full demo flow:
  - [ ] Pre-flight checks → all pass
  - [ ] Run Scenario 1 → success
  - [ ] Export evidence → success
  - [ ] Cleanup → success
  - [ ] Run Scenario 2 → success
  - [ ] Export evidence → success
  - [ ] Cleanup → success
  - [ ] Repeat for Scenarios 3, 4, 5
- [ ] Test failure scenarios:
  - [ ] Pre-flight with service stopped → blocks execution
  - [ ] Pre-flight with insufficient disk space → warns
  - [ ] Cancel scenario mid-execution → cleans up
- [ ] Test external poller during scenarios
- [ ] Verify state history updates in real-time
- [ ] Check evidence packages are complete
- [ ] Verify cleanup between scenarios

**Success Criteria:**
- Run all 5 scenarios consecutively without errors
- Each scenario completes successfully
- Evidence exports for all scenarios
- Cleanup prepares environment correctly
- No leftover files or stuck processes
- Run full suite 3 times → 3/3 success

**Testing:**
```bash
# Full suite test:
# 1. Pre-flight → pass
# 2. Scenario 1 → pass
# 3. Export → verify
# 4. Cleanup → verify
# 5. Scenario 2 → pass
# 6. Export → verify
# 7. Cleanup → verify
# ... continue for all 5 scenarios
```

---

### Phase 4: Polish & Practice (2 hours)

**Goal:** Production-ready for CCSO presentation

#### Task 4.8: UI Polish (1 hour) ⏳ TODO
**Priority:** MEDIUM
**Dependencies:** All core functionality complete

**Subtasks:**
- [ ] Add CSS styling to demo.html
  - [ ] Professional color scheme
  - [ ] Clear status indicators
  - [ ] Loading spinners
  - [ ] Error highlights
- [ ] Improve error messages
  - [ ] Clear, non-technical language
  - [ ] Specific remediation steps
  - [ ] Category labels (File System, Service, Database)
- [ ] Add loading indicators
  - [ ] Spinner during pre-flight checks
  - [ ] Progress bar during scenario execution
  - [ ] "Exporting..." during evidence collection
- [ ] Add keyboard shortcuts
  - [ ] Ctrl+R: Run pre-flight
  - [ ] Escape: Cancel scenario
- [ ] Add confirmation dialogs
  - [ ] "Cancel scenario?" if running
  - [ ] "Clean destinations?" if checked
- [ ] Responsive design for different screen sizes
- [ ] Test UI in different browsers (Edge, Chrome)

**Success Criteria:**
- Professional appearance
- Clear visual hierarchy
- No confusing elements
- Errors easy to understand
- Works in Edge and Chrome
- No console errors

---

#### Task 4.9: Documentation & Practice (1 hour) ⏳ TODO
**Priority:** HIGH
**Dependencies:** All functionality complete

**Subtasks:**
- [ ] Create presenter notes document
  - [ ] What to say for each scenario
  - [ ] Key points to emphasize
  - [ ] Questions CCSO might ask
  - [ ] Answers to common questions
- [ ] Document troubleshooting steps
  - [ ] What if service crashes during demo?
  - [ ] What if pre-flight check fails?
  - [ ] What if file doesn't copy?
- [ ] Practice full demo 5 times
  - [ ] Time each scenario
  - [ ] Identify rough spots
  - [ ] Smooth transitions between scenarios
- [ ] Create backup plan
  - [ ] Pre-recorded video if live demo fails
  - [ ] Screenshots as fallback
  - [ ] Evidence packages from previous runs
- [ ] Update README.md with demo mode instructions
- [ ] Create quick reference card (laminated cheat sheet)

**Success Criteria:**
- Presenter comfortable with flow
- Can answer common questions
- Backup plan documented
- Demo completes in 25 minutes (5 scenarios × 5 min)
- No awkward pauses or confusion

---

## Optional Enhancements (Future)

### Task 5.1: Stress Test Mode (2 hours) ⏳ FUTURE
**Priority:** LOW
**Dependencies:** All core functionality

**Goal:** Test system with 100+ concurrent files

**Subtasks:**
- [ ] Create StressTest struct
- [ ] Implement stress test execution
  - [ ] Generate 100 files in Reservoir
  - [ ] Drop to Input simultaneously or in batches
  - [ ] Monitor service RAM/CPU
  - [ ] Track throughput and error rate
- [ ] Add UI panel for stress test
- [ ] Add results visualization (charts?)
- [ ] Document max capacity findings

---

### Task 5.2: Soak Test Mode (2 hours) ⏳ FUTURE
**Priority:** LOW
**Dependencies:** All core functionality

**Goal:** 24-48 hour continuous operation test

**Subtasks:**
- [ ] Create SoakTest struct
- [ ] Implement background file generation
  - [ ] New file every 5 minutes
  - [ ] Run for 24-48 hours
- [ ] Monitor resource usage over time
- [ ] Generate charts (RAM, CPU, disk over time)
- [ ] Alert if memory leak detected
- [ ] Add UI panel for soak test
- [ ] Document findings

---

## Integration Test Harness (Bonus)

### Task 6.1: PowerShell Integration Test Suite (2 hours) ⏳ BONUS
**Priority:** LOW
**Dependencies:** All scenarios working

**Goal:** Automated test suite for CI/CD

**Files to Create:**
- [ ] `scripts/Run-All-Scenarios-Integration-Test.ps1`

**Subtasks:**
- [ ] Create test harness script
- [ ] Implement Test-Scenario function
- [ ] Run all 5 scenarios sequentially
- [ ] Track pass/fail for each
- [ ] Generate summary report
- [ ] Export results to JSON
- [ ] Add -StopOnFailure parameter
- [ ] Add -TestFileSize parameter
- [ ] Document usage for CI/CD
- [ ] Test in GitHub Actions (if applicable)

**Success Criteria:**
- Runs all 5 scenarios unattended
- Reports pass/fail for each
- Exit code 0 = all passed, 1 = any failed
- Can run nightly in CI/CD
- Evidence collected for each run

---

## Task Dependencies

```
Phase 1: Proof of Concept
├─ Task 4.1: Pre-Flight Validation (2h)
└─ Task 4.2: Scenario Orchestration (Scenario 1) (2h)
     └─ (depends on 4.1 soft)

Phase 2: Critical Features
├─ Task 4.3: External Poller (1.5h)
│    └─ (no dependencies)
├─ Task 4.2 (cont): All Scenarios (1.5h)
│    └─ depends on: Task 4.2 (Scenario 1)
└─ Task 4.4: Real-Time Monitoring (1h)
     └─ depends on: Phase 3 state history (already done)

Phase 3: Evidence & Cleanup
├─ Task 4.5: Evidence Export (1h)
│    └─ (no dependencies)
├─ Task 4.6: Cleanup & Reset (1h)
│    └─ (no dependencies)
└─ Task 4.7: End-to-End Testing (1h)
     └─ depends on: Tasks 4.1-4.6

Phase 4: Polish
├─ Task 4.8: UI Polish (1h)
│    └─ depends on: All core functionality
└─ Task 4.9: Documentation & Practice (1h)
     └─ depends on: All functionality complete
```

---

## Progress Tracking

### Overall Status

- **Phase 1:** ⏳ TODO (0% complete)
- **Phase 2:** ⏳ TODO (0% complete)
- **Phase 3:** ⏳ TODO (0% complete)
- **Phase 4:** ⏳ TODO (0% complete)

**Total Progress:** 0/9 core tasks complete (0%)

### Key Milestones

- [ ] **Milestone 1:** Scenario 1 runs from Console (Task 4.1, 4.2 complete)
- [ ] **Milestone 2:** All scenarios + poller working (Phase 2 complete)
- [ ] **Milestone 3:** Evidence & cleanup automated (Phase 3 complete)
- [ ] **Milestone 4:** Production-ready (Phase 4 complete)
- [ ] **Milestone 5:** CCSO demo successful (Ultimate goal!)

---

## Time Estimates Summary

| Phase | Tasks | Estimated | Priority |
|-------|-------|-----------|----------|
| Phase 1 | Tasks 4.1-4.2 | 4 hours | CRITICAL |
| Phase 2 | Tasks 4.2-4.4 | 4 hours | HIGH |
| Phase 3 | Tasks 4.5-4.7 | 3 hours | HIGH |
| Phase 4 | Tasks 4.8-4.9 | 2 hours | MEDIUM |
| **Total Core** | **9 tasks** | **13 hours** | **Must Do** |
| Optional | Tasks 5.1-5.2 | 4 hours | LOW |
| Bonus | Task 6.1 | 2 hours | BONUS |

**Critical Path:** Tasks 4.1 → 4.2 → 4.2(cont) → 4.7 → 4.9 = **8 hours minimum**

---

## Risk Assessment

### High Risk Items

| Risk | Impact | Mitigation | Owner |
|------|--------|------------|-------|
| PowerShell parsing fails | Scenarios don't work | Test extensively with all 5 scenarios | Dev |
| External poller detects locks | Demo shows failure | Test non-locking thoroughly, prove ForkerDotNet safe | Dev |
| CCSO demo crashes | Project failure | Practice 5+ times, create backup plan | Team |
| Corruption injection in prod | Data loss | Triple-locked safety (env + config + explicit) | Dev |
| Large files timeout | Demo incomplete | Test with 2-3GB files, set appropriate timeouts | Dev |

### Medium Risk Items

| Risk | Impact | Mitigation | Owner |
|------|--------|------------|-------|
| UI confusing to CCSO | Unclear demo | User testing with non-technical person | Team |
| Evidence export fails | No governance proof | Test evidence collection thoroughly | Dev |
| Cleanup incomplete | Next scenario fails | Verify cleanup logic, add retries | Dev |
| Browser compatibility | UI broken in Edge | Test in Edge and Chrome | Dev |

---

## Notes

### Design Decisions

1. **Reuse PowerShell Scripts** - Don't reimplement proven code
2. **Real 2-3GB Files** - Authentic medical imaging file sizes
3. **Triple-Locked Corruption** - Safety first for demo injection
4. **Console Orchestration** - Single UI for all demos
5. **Evidence Auto-Collection** - No manual steps for governance

### Key Constraints

- Must work on Windows (PowerShell 5.1+)
- Must use existing PowerShell scripts (no rewrites)
- Must handle 2-3GB files (realistic clinical sizes)
- Must be bulletproof (zero tolerance for demo failures)
- Must complete in 25 minutes (5 scenarios × 5 min)

### Success Criteria

- ✅ All 5 scenarios run from Console UI
- ✅ External poller proves non-locking
- ✅ Evidence exports for governance review
- ✅ Cleanup automated between scenarios
- ✅ Demo runs flawlessly 5 times in a row
- ✅ CCSO confident in system safety

---

## Next Steps

1. **Start with Task 4.1** - Pre-Flight Validation Dashboard
2. **Create demo.html template** - Basic UI structure
3. **Test pre-flight checks** - Ensure all 13 checks work
4. **Move to Task 4.2** - Get Scenario 1 working end-to-end
5. **Iterate rapidly** - Get something working quickly, polish later

**First Goal:** Click button → Scenario 1 runs → Progress updates → Completion message

---

## References

- [demo-console-enhancements-design.md](demo-console-enhancements-design.md) - Complete design document
- [console-dev-task-list.md](console-dev-task-list.md) - Phase 1-3 task list (already complete)
- [demo-user-guide.md](demo-user-guide.md) - Existing PowerShell demo guide
- [scripts/Run-Scenario*.ps1](scripts/) - Existing scenario scripts to orchestrate

---

**Last Updated:** 2025-10-15
**Next Review:** After Phase 1 complete
