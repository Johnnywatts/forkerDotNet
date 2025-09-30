# ForkerDotNet Task List

**Project**: Production-grade .NET 8 file copier for medical imaging files (500MB-20GB)
**Current Phase**: Phase 11.1 - Real Demo System (URGENT)

**Legend**: ✅ Completed | 🔄 In Progress | 📝 To Do | ⛔ Blocked

---

## Phase 1 - Solution & Skeleton ✅ COMPLETED

- ✅ Install .NET 8 LTS SDK (8.0.414)
- ✅ Create Solution Structure (Forker.sln)
- ✅ Create Core Projects (Domain, Infrastructure, Service)
- ✅ Create Test Projects (Domain.Tests, Infrastructure.Tests, Resilience.Tests)
- ✅ Add Development Standards (.editorconfig, nullable types, warnings as errors)
- ✅ Implement Dependency Injection Wiring
- ✅ Add Serilog Configuration (console and file output)
- ✅ Implement Health Endpoint (/health/live)

## Phase 2 - Domain Core ✅ COMPLETED

- ✅ Implement enums (JobState, TargetCopyState with proper transitions)
- ✅ Implement value objects (FileJobId, TargetId, VersionToken with validation)
- ✅ Implement FileJob + TargetOutcome with guarded state transition methods
- ✅ Enforce invariants I1, I2, I8, I10 locally
- ✅ Add domain exceptions (InvalidStateTransitionException, InvariantViolationException)
- ✅ Unit tests (68/68 tests passing - exhaustive valid/invalid transitions)

## Phase 3 - Persistence Layer ✅ COMPLETED

- ✅ SQLite Database Schema (FileJobs and TargetOutcomes tables)
- ✅ Repository Interfaces (IJobRepository and ITargetOutcomeRepository)
- ✅ Repository Implementations (SqliteJobRepository and SqliteTargetOutcomeRepository)
- ✅ Optimistic Concurrency Control (VersionToken enforcement)
- ✅ Connection Factory (ISqliteConnectionFactory with crash-safe WAL mode)
- ✅ Database Initialization (automatic schema creation and migration)
- ✅ Foreign Key Constraints (cascade deletes and referential integrity)
- ✅ Integration Testing (88/88 tests passing - cross-layer operations)

## Phase 4 - File Discovery ✅ COMPLETED

- ✅ File Discovery Service (FileSystemWatcher-based implementation)
- ✅ File Stability Checking (multi-check verification for large files)
- ✅ Pattern Matching System (medical imaging formats: *.scn, *.svs, *.tiff, *.ndpi)
- ✅ Configuration Management (FileMonitoringConfiguration, DirectoryConfiguration)
- ✅ File Locking Prevention (safe file access for external polling systems)
- ✅ Dependency Injection (complete service registration and wiring)
- ✅ Comprehensive Testing (112+ tests with real 291MB medical files)

## Phase 5 - Dual-Target Copy Pipeline ✅ COMPLETED

- ✅ Core Copy Engine Interfaces (IHashingService, IFileCopyService, ICopyOrchestrator)
- ✅ Streaming SHA-256 Hashing Service (optimized for 500MB-20GB files)
- ✅ Atomic File Copy Service (temp file staging with atomic moves)
- ✅ Dual-Target Copy Orchestrator (parallel copying to TargetA and TargetB)
- ✅ Target Configuration System (multi-target setup with priority and concurrency)
- ✅ Domain Model Integration (FileJob/TargetOutcome state transitions)
- ✅ Dependency Injection Integration (full service registration)
- ✅ Comprehensive Testing (139/139 tests passing with real medical files)

## Phase 5.1 - Critical Race Condition Fixes ✅ COMPLETED

- ✅ Fix Timer Callback Anti-Pattern (atomic Interlocked.CompareExchange operations)
- ✅ Thread-Safe Event Handling (isolated parallel execution with exception handling)
- ✅ Fix Disposal Race Conditions (IAsyncDisposable pattern with timeout protection)
- ✅ Atomic State Management (proper state machine with Interlocked operations)
- ✅ NBomber Load Testing (25 ops/sec for 60 seconds race condition detection)
- ✅ Docker Multi-Process Framework (cross-process race condition testing)
- ✅ Comprehensive Test Coverage (5 concurrent stress test scenarios)

## Phase 6 - Multi-Target Verification ✅ COMPLETED

- ✅ Core Verification Pipeline (IVerificationService, IQuarantineService, IVerificationOrchestrator)
- ✅ Verification Workflow Implementation (hash validation, target state transitions)
- ✅ Quarantine System (hash mismatch handling with audit trail)
- ✅ Domain Model Integration (VerificationResult, JobVerificationResult)
- ✅ Repository Interfaces (IQuarantineRepository with placeholder implementation)
- ✅ Unit Tests (VerificationResult value object validation)
- ✅ Dependency Injection Wiring (full service registration)
- ✅ Invariant Enforcement (I2, I5, I11, I15, I16 implemented)

## Phase 7 - Retry & Backoff Logic ✅ COMPLETED

- ✅ Exponential backoff for failed operations (ExponentialBackoffRetryPolicy with jitter)
- ✅ Dead letter queue for permanently failed files (IDeadLetterService with audit trail)
- ✅ Retry policies and circuit breaker patterns (Invariant I6 & I13 enforcement)
- ✅ Retry orchestration and coordination (IRetryOrchestrator with concurrency control)
- ✅ Manual retry override capabilities (Administrative intervention support)

## Phase 8 - Adaptive Concurrency Control ✅ COMPLETED

- ✅ Dynamic concurrency adjustment based on system load (Real-time resource monitoring)
- ✅ Resource monitoring and throttling (CPU, memory, disk I/O tracking)
- ✅ Performance optimization for large file workflows (Operation-specific limits)
- ✅ Backpressure mechanisms for overload protection (Utilization-based adjustments)
- ✅ Cross-platform resource monitoring (BasicResourceMonitor without Windows dependencies)

## Phase 9 - Observability Maturity ✅ COMPLETED

- ✅ Structured logging with correlation IDs
- ✅ Prometheus metrics and health monitoring
- ✅ Distributed tracing for file processing workflows

## Phase 10 - Resilience Testing ✅ COMPLETED

- ✅ Thread Safety Race Condition Testing (CorrectStressTests.cs - 5/5 tests passing)
- ✅ Fix File System Race Condition Tests (ConcurrentStressTests.cs design flaws fixed - 5/5 tests passing)
- ✅ Fix Production Load Tests (SimplifiedNBomberTests.cs configuration issues fixed - 4/4 tests passing)
- ✅ Implement File System Timing Race Validation (FileSystemRaceTests.cs - 18/18 tests comprehensive coverage)
- ✅ Implement Medical Imaging Load Pattern Testing (FileSystemRaceTests.cs medical imaging workflows)
- ✅ Chaos engineering test harness (DockerMultiProcessTests.cs - 3/3 multi-process race condition tests)
- ✅ Fault injection and recovery validation (FileSystemRaceTests.cs error recovery scenarios)
- ✅ Production load simulation with comprehensive race condition coverage (NBomber + FileSystemRaceTests combined)

## Phase 10.1 - Test Design Flaw Remediation ✅ COMPLETED

- ✅ Analyze ConcurrentStressTests design flaws (timing-dependent assertions identified)
- ✅ Fix TimerOverlapPrevention test (race condition detection vs file processing counts)
- ✅ Fix EventHandlerSafety test (thread safety validation vs timing-dependent events)
- ✅ Fix HighVolumeFileProcessing test (system stability vs unrealistic throughput expectations)
- ✅ Fix NBomber warm-up/duration configuration (warm-up ≤ test duration fixes applied)
- ✅ Optimize NBomber loads for CI environments (10-15 ops/sec sustainable rates implemented)
- ✅ Implement stability-focused assertions (timing-tolerant race condition detection)

## Phase 10.2 - File System Race Condition Testing ✅ COMPLETED

- ✅ File Stability Detection Race Validation (growth detection, lock detection, age requirements)
- ✅ FileSystemWatcher Reliability Testing (event coalescing, initialization races, ordering)
- ✅ Pending File Management Timing Tests (timeout cleanup, concurrent modification handling)
- ✅ I/O Race Condition Validation (file accessibility during stability checks)
- ✅ Medical Imaging Workflow Pattern Testing (large file batch arrivals, external tool integration)

## Phase 10.3 - Production Load Pattern Validation ✅ COMPLETED

- ✅ Large File Processing Stability (500MB-20GB SVS files under concurrent load)
- ✅ Batch Arrival Pattern Testing (multiple simultaneous file discoveries)
- ✅ Resource Utilization Validation (memory, CPU, I/O under sustained medical imaging loads)
- ✅ External Integration Testing (file locking by imaging software during processing)
- ✅ Error Recovery Under Load (stability detection failures, I/O errors, resource exhaustion)

## Phase 11 - Observable System Testing ⚠️ COMPLETED WITH ISSUES

**CRITICAL DISCOVERY**: Phase 11 demos are mostly fake simulations (see commit fbcdd29)
- ✅ Demo #4 (Corruption Prevention) - ONLY REAL DEMO with actual SHA-256 verification
- ❌ Demo #1 (Live Workflow) - Fake hash verification (hardcoded strings), fake stability checks
- ❌ Demo #2 (Destination Locking) - Completely broken (doesn't copy files, proves nothing)
- ❌ Demo #3 (File Stability) - Fake stability checks (just delays, no real checking)
- ❌ Demo #5 (Failure Recovery) - Fake simulations, no real failure injection
- ❌ Demo #6 (Real-Time Monitoring) - Fake progress bars, no real processing
- ✅ Demos #7-9 (Documentation) - Appropriate informational content

**Action Required**: Replace fake demos with real observable demonstrations using Windows tools (see Phase 11.1)

---

## Phase 11.1 - Real Demo System 🔄 IN PROGRESS (URGENT)

**Objective**: Replace fake Spectre.Console demos with observable demonstrations using real Windows system tools

### PowerShell Demo Scripts
- 📝 Create Setup-DemoEnvironment.ps1 (launches File Explorer grid, SQLite Browser, Process Monitor)
- 📝 Create Run-Scenario1-EndToEnd.ps1 (5 min: File drop → dual copy → hash verification)
- 📝 Create Run-Scenario2-Corruption.ps1 (3 min: Inject corruption → detect with SHA-256 → quarantine)
- 📝 Create Run-Scenario3-ConcurrentAccess.ps1 (5 min: External read during copy → no blocking)
- 📝 Create Run-Scenario4-CrashRecovery.ps1 (5 min: Kill service mid-copy → restart → resume)
- 📝 Create Run-Scenario5-StabilityDetection.ps1 (3 min: Growing file → wait for stability → process)
- 📝 Create Cleanup-DemoEnvironment.ps1

### WPF Resilience Test Controller (Optional)
- 📝 Create WPF project structure
- 📝 Implement service control UI (start/stop/restart ForkerDotNet service)
- 📝 Implement file injection UI (drop 1/10/50 files, simulate growing file, inject corrupted file)
- 📝 Implement stress test UI (lock files, disk full, crash service, concurrent reads)
- 📝 Implement verification UI (PowerShell Get-FileHash, SQLite state query, temp file scan)
- 📝 Implement tool launcher (File Explorer grid, Process Monitor, SQLite Browser, perfmon)
- 📝 Implement evidence export (screenshots, test logs, governance package ZIP)

### Documentation Updates
- 📝 Update demo-user-guide.md with PowerShell scripts approach
- 📝 Update demo-user-guide.md with Windows tools setup (Process Monitor, SQLite Browser, etc)
- 📝 Create Demo-Evidence-Package-Template.md (governance approval checklist)
- 📝 Create Quick-Start-Demo.md (5 minute setup guide)
- 📝 Add tool download links (Sysinternals, SQLite Browser, HashCheck)

### Cleanup Fake Demos
- 📝 Review Forker.Clinical.Demo/Program.cs Demo #4 (keep - only real one)
- 📝 Delete or deprecate Forker.Clinical.Demo project
- 📝 Remove fake demo references from solution file
- 📝 Update documentation to remove fake demo instructions

### Testing & Validation
- 📝 Test Scenario 1 with real ForkerDotNet service running
- 📝 Test Scenario 2 with PowerShell Get-FileHash verification
- 📝 Test Scenario 3 with Process Monitor syscall trace
- 📝 Test Scenario 4 with SQLite Browser state verification
- 📝 Test Scenario 5 with File Explorer + Process Monitor
- 📝 Practice complete demo flow (under 30 minutes total)
- 📝 Create evidence package with screenshots and test outputs

---

## Phase 12 - Performance Tuning 📝 TO DO

### Clinical Pathway Prioritization
- 📝 Configure Clinical folder as TargetA (primary) with priority queuing
- 📝 Implement parallel copy optimization for Clinical + Research targets
- 📝 Validate minimal delay on primary pathway (<5 seconds for 1GB files)

### Buffer Size Experiments
- 📝 Measure throughput with 500MB-20GB SVS/SCN files (64KB vs 256KB vs 1MB buffers)
- 📝 Evaluate CPU usage vs I/O throughput trade-offs
- 📝 Test optimal buffer size for dual-target parallel copying

### Streaming Copy Optimization
- 📝 Optional async prefetching of source stream
- 📝 Validate <100MB memory usage during concurrent operations
- 📝 Optimize SHA-256 hashing concurrency for verification pipeline

### Concurrent Operations Tuning
- 📝 Evaluate memory pressure under high-volume pathology workflows
- 📝 Optimize resource monitoring thresholds for production loads
- 📝 Test backpressure mechanisms with sustained medical imaging batches

### Throughput Validation
- 📝 Measure dual-target throughput with real medical imaging files
- 📝 Validate parallel copy performance meets clinical pathway requirements (1GB/min per target)
- 📝 Decide on final default buffer + hashing concurrency settings

### Medical Imaging Format Optimization
- 📝 Test performance with typical pathology file sizes (500MB-5GB)
- 📝 Validate extreme case handling (20GB+ whole slide images)
- 📝 Optimize file stability detection for large file growth patterns

---

## Phase 13 - Pre-Production Hardening 📝 TO DO

### Configuration Validation
- 📝 Validate Clinical and Research folder paths exist and are writable
- 📝 Check for duplicate target IDs in dual-target configuration
- 📝 Enforce medical imaging file pattern validation (*.svs, *.scn, *.ndpi, *.tiff)
- 📝 Validate Input directory accessibility and monitoring permissions

### Security Hardening
- 📝 Path canonicalization to prevent directory traversal attacks
- 📝 Permission checks for least-privilege service account operation
- 📝 FIPS-compliant SHA-256 verification enforcement
- 📝 Secure configuration file handling (encryption for sensitive paths)

### Crash Recovery Validation
- 📝 Manual failover rehearsal (simulate service crash during high load)
- 📝 Warm startup recovery time measurement and optimization
- 📝 SQLite WAL recovery testing with interrupted operations
- 📝 Validate no duplicate writes after restart (Invariant I4 enforcement)

### Production Deployment Testing
- 📝 Test Windows Service deployment via NSSM or built-in service host
- 📝 Validate automatic startup and crash recovery in production environment
- 📝 Simulate pathology scanner continuous file drops (24-hour soak test)
- 📝 Test NPIC ingestion workflow integration (zero interference validation)

---

## Phase 14 - Clinical Deployment Validation 📝 TO DO

### Requirement 1: Input Directory Monitoring
- 📝 Validate continuous file monitoring with FileSystemWatcher reliability
- 📝 Test with actual pathology scanner file drop patterns
- 📝 Confirm medical imaging format detection (SVS, NDPI, SCN, TIFF)
- 📝 Validate file stability detection prevents incomplete file processing

### Requirement 2: Dual-Target Copy Operations
- 📝 Validate Primary Clinical folder copy (guaranteed bit-perfect OS file copy)
- 📝 Validate Research folder copy (secondary pathway with same integrity)
- 📝 Confirm both copies complete before Input cleanup (coordination requirement)
- 📝 Test SHA-256 verification ensures bit-perfect copy integrity

### Requirement 3: Input Directory Cleanup
- 📝 Validate Input cleanup only after BOTH Clinical and Research copies verified
- 📝 Test cleanup atomicity (no partial cleanup on failure)
- 📝 Confirm failed files remain in Input for retry processing
- 📝 Validate quarantine handling for hash mismatch scenarios

### Requirement 4: NPIC Workflow Non-Interference
- 📝 Test NPIC ingestion can read from Clinical folder during ForkerDotNet operations
- 📝 Validate no file locking prevents external system access
- 📝 Confirm Clinical folder files immediately available after copy completion
- 📝 Test continuous NPIC ingestion while ForkerDotNet processes new files

### Requirement 6: Minimize Clinical Pathway Interference
- 📝 Validate streaming copy operations use OS-level file copy mechanisms
- 📝 Confirm atomic temp file staging with rename operations
- 📝 Test bit-perfect copy integrity with SHA-256 verification
- 📝 Validate no data corruption risk during copy operations

### Requirement 7: Minimize Primary Pathway Delay
- 📝 Measure Clinical folder copy completion time (<5 seconds for 1GB files)
- 📝 Validate parallel copy to Research doesn't delay Clinical pathway
- 📝 Test Clinical target priority in dual-target orchestration
- 📝 Confirm 1GB/min throughput meets clinical urgency requirements

### Requirement 8: Clinical Risk Elimination Design
- 📝 Validate 20 invariants (I1-I20) enforce safe state transitions
- 📝 Test crash recovery prevents partial operations (SQLite WAL validation)
- 📝 Confirm quarantine system prevents corrupted data propagation (Invariant I5)
- 📝 Validate append-only audit trail supports clinical compliance
- 📝 Test failure modes: network interruption, service crash, disk full scenarios

### End-to-End Clinical Workflow Validation
- 📝 Simulate full pathology workflow: Scanner → Input → Clinical → NPIC ingestion
- 📝 Test Research folder parallel replication with Clinical pathway priority
- 📝 Validate 24-hour continuous operation with real medical imaging file loads
- 📝 Confirm zero data loss, zero corruption, zero NPIC interference
- 📝 Capture evidence: logs, metrics, performance data for governance approval