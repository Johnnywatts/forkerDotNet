1# ForkerDotNet Development Plan

## Requirements Summary from PowerShell Analysis

**Core Requirements Extracted:**
- **File Types**: SVS files (500MB-20GB medical imaging files)
- **Critical Constraint**: No file locking during verification (external polling systems access files every 30-60 seconds)
- **Destinations**: Dual-target replication (TargetA, TargetB)
- **Integrity**: SHA256 verification mandatory for medical data
- **Performance**: 1GB/min per target, <100MB memory usage regardless of file size
- **Atomicity**: No partial files visible to external systems
- **Service**: Windows Service with NSSM deployment

## .NET Architecture Design with Security Amendments

Based on the security documents and PowerShell analysis, here's the comprehensive architecture:

### **Technology Stack (Security-Hardened)**
- **.NET 8 LTS** (framework-dependent deployment for OS patching)
- **SQLite with WAL** (crash-safe persistence)
- **SHA-256 default** (FIPS-compliant, medical data integrity)
- **Serilog + OpenTelemetry** (structured logging + metrics)
- **Windows Service Host** (least-privilege service account)
- **HTTP endpoints on localhost only** (health/metrics)

### **Security Implementation**
- **Service Account**: gMSA or virtual service account (not LocalSystem)
- **File Operations**: No reparse point following, path canonicalization
- **Crypto**: IncrementalHash.CreateHash(SHA256) for FIPS compliance
- **Temp Files**: Dedicated .forker/tmp directories with restrictive ACLs
- **Endpoints**: localhost binding, optional Windows Auth for remote access

### **Testing Strategy with PROOF Requirements**

#### **1. Unit Test Proof Artifacts**
- **Coverage Reports**: 95%+ line/branch coverage with detailed HTML reports
- **Property-Based Tests**: FsCheck validation of invariants (1000+ random inputs)
- **Mutation Testing**: Stryker.NET to validate test quality
- **Performance Benchmarks**: BenchmarkDotNet with memory allocation tracking

#### **2. Integration Test Proof Artifacts**
- **Database State Validation**: SQLite database dumps before/after operations
- **File System State Audits**: Directory trees with checksums
- **Concurrent Operation Logs**: Thread-safe operation tracking
- **Recovery Scenario Videos**: Automated crash-and-recover demonstrations

#### **3. System Test Proof Artifacts**
- **24-Hour Soak Reports**: Continuous operation with metrics collection
- **Chaos Engineering Results**: Fault injection with recovery validation
- **Memory Leak Detection**: Application Verifier and PerfView analysis
- **Large File Performance**: 20GB file processing with memory/CPU profiling

#### **4. Security Test Proof Artifacts**
- **Penetration Test Reports**: Path traversal, privilege escalation attempts
- **Code Security Scans**: SonarQube, Semgrep, CodeQL reports
- **SBOM Generation**: CycloneDX with vulnerability scanning
- **FIPS Compliance Validation**: Algorithm usage verification

### **Implementation Phases with Proof Gates**

Each phase will include:
1. **Implementation** with comprehensive logging
2. **Unit Tests** with coverage reports
3. **Integration Tests** with state validation
4. **Performance Benchmarks** with evidence
5. **Security Validation** with scan reports

## Phase 1 – Solution & Skeleton (Day 0–1)
- Create solution: Forker.sln
- Projects:
  - src/Forker.Domain (pure domain logic, invariants, value types)
  - src/Forker.Infrastructure (SQLite repo, file system, hashing, metrics, logging adapters)
  - src/Forker.Service (Worker Service host, DI wiring, configuration, health/metrics endpoints)
  - tests/Forker.Domain.Tests
  - tests/Forker.Infrastructure.Tests
  - tests/Forker.Resilience.Tests (property + chaos harness later)
- Add EditorConfig + nullable + warnings as errors
- Add basic Dependency Injection wiring
- Add Serilog (console + rolling file) placeholder

Deliverable: Build + run service (no behavior yet) with /health/live endpoint returning OK.

## Phase 2 – Domain Core (Day 1–3)
- Implement enums: JobState, TargetCopyState
- Implement value objects: FileJobId, TargetId, VersionToken
- Implement FileJob + TargetOutcome with guarded state transition methods
- Enforce invariants I1, I2 (partial enforcement), I8, I10 locally
- Add domain exceptions (InvalidStateTransitionException, InvariantViolationException)
- Unit tests: exhaustive valid/invalid transitions (FsCheck property for monotonic progression)

Deliverable: Domain test suite green; mutation test (optional) to validate transition guards.

## Phase 3 – Persistence Layer (Day 3–5)
- Create SQLite DDL migrations (Jobs, Targets, Events)
- Implement IJobRepository (CRUD + state updates + recovery queries)
- Implement event append (Events table) with ordering guarantee (Seq)
- Add pragmatic indices per design
- Add lightweight migration runner (apply if missing)
- Property test: inserting random sequences of events preserves ordering (I18)

Deliverable: Repository integration tests using real SQLite (Temp folder) + concurrency test (parallel updates) to ensure no duplicate finalization attempts.

## Phase 4 – Discovery & Stability (Day 5–7)
- Implement IFileStabilityStrategy (size sampling)
- Implement FileDiscoveryService:
  - Periodic scan of WatchedRoots
  - Detect new files not yet tracked
  - Apply stability strategy before enqueuing
- Persist DISCOVERED then QUEUED states atomically when stable
- Tests: simulate growth/shrink to assert I14 (unstable not enqueued)

Deliverable: Service logs discovery of stable test files; jobs appear in DB QUEUED.

## Phase 5 – Copy Pipeline (Single Target First) (Day 7–10)
- Implement IHasher (SHA256 streaming)
- Implement IFileSystemAbstraction (open/read/write/atomic move/fsync/list temp cleanup)
- Implement CopyWorker (single target): stream copy + hash + temp file semantics
- Add channel-based dispatcher (bounded capacity) & worker loop
- Persist target transitions (PENDING → COPYING → COPIED)
- Introduce Job IN_PROGRESS state when first target starts
- Tests: interrupted copy (kill process mid-stream) ensures restart requeues (I4)

Deliverable: Single-target copy end-to-end with hash recorded.

## Phase 6 – Multi-Target & Verification (Day 10–13)
- Add second target configuration support (RequiredTargets JSON)
- Parallel copy respecting per-target semaphores
- Implement verification service (rehash target vs SourceHash)
- Transition logic for PARTIAL and VERIFIED (I2, I11)
- Hash mismatch quarantine path (I5, I16)
- Metrics: copy_duration_seconds, bytes_copied_total, hash_mismatch_total

Deliverable: Two targets verify; job reaches VERIFIED when both consistent.

## Phase 7 – Retry & Backoff (Day 13–15)
- Implement IRetryPolicy (exponential + jitter; enforce non-decreasing I13)
- Distinguish retryable vs permanent failures
- Track Attempts; enforce maxAttempts → FAILED_PERMANENT (I6)
- Metrics: retries_total{reason}, copy_failures_total{reason}

Deliverable: Induced transient failures recover automatically; permanent failure path logged & surfaced.

## Phase 8 – Adaptive Concurrency (Day 15–17)
- Implement AdaptiveConcurrencyController:
  - Record per-copy latency
  - Periodically adjust global limit within [1, maxGlobal]
  - Simple rule: if p95 latency > threshold reduce; if well below increase gradually
- Expose metric adaptive_concurrency_level
- Tests: simulated latency spikes enforce I12

Deliverable: Concurrency auto-tunes under synthetic slowdowns.

## Phase 9 – Observability Maturity (Day 17–19)
- Finalize Prometheus metrics endpoint (/metrics)
- Structured logging enrichment: correlation (jobId, targetId)
- Implement /jobs/{id} read model (compose job + targets + recent events)
- Add /stats/summary (counts by state + recent throughput)
- Metrics monotonic counters test (I17)

Deliverable: Dashboard-ready metrics + targeted log search.

## Phase 10 – Resilience & Chaos Harness (Day 19–24)
- Implement FaultingFileSystem + IFaultProfile
- Scenario DSL for test composition
- Property tests to enforce invariants I1–I20 across random sequences
- Crash injection: orchestrate process kill & restart script harness
- Integrity audit script: rehash all final files, compare to DB

Deliverable: ResilienceReport with zero hash mismatches and full invariant coverage.

## Phase 11 – Performance & Tuning (Day 24–26)
- Buffer size experiments (64KB vs 256KB vs 1MB) measure throughput & CPU
- Optional async prefetching of source stream
- Evaluate memory pressure under concurrency
- Decide on final default buffer + hashing concurrency

Deliverable: Performance summary document + chosen defaults.

## Phase 12 – Pre-Production Hardening (Day 26–30)
- Config validation (fail fast on invalid paths, duplicate target IDs)
- Security hardening (path canonicalization, permission checks)
- Manual failover rehearsal (simulate service crash during high load)
- Warm startup recovery time measurement (I4, I20)

Deliverable: Green checklist for launch readiness.

## Invariants (I1-I20)

| Id | Invariant | Test Strategy |
|----|-----------|---------------|
| I1 | Target VERIFYING requires prior COPIED | Unit transition guard; property random transitions |
| I2 | Job VERIFIED only if all targets VERIFIED & hashes match | Integration partial completion; property subsets |
| I3 | No concurrent finalization same path | Concurrency stress (parallel finalize) |
| I4 | Restart no duplicate final writes | Crash mid-copy, restart, assert file count |
| I5 | Hash mismatch => QUARANTINED | Inject corruption, expect quarantine |
| I6 | MaxAttempts -> FAILED_PERMANENT | Force persistent fault, observe state |
| I7 | Event log reconstructs job | Replay events vs repo state |
| I8 | Monotonic job progression | Property sequence generation |
| I9 | Temp never finalized | Scan outputs during stress |
| I10 | SourceHash immutable | Unit double-set different value |
| I11 | Partial not VERIFIED | Slow second target test |
| I12 | Adaptive limit <= max | Simulated latency spikes |
| I13 | Retry backoff non-decreasing | Policy unit |
| I14 | Unstable not enqueued | Grow/shrink file test |
| I15 | Rehash matches original | Verification test |
| I16 | Quarantined requires manual action | Soak, observe static state |
| I17 | Metrics monotonic counters | Metrics audit test |
| I18 | Event ordering ascending | Insert out-of-order; retrieval sorted |
| I19 | Independent target progress | Fail one target; other verifies |
| I20 | Recovery requeues incomplete only | Mixed state crash test |

## Success Criteria
- All invariants I1–I20 demonstrably tested (automated) pre-cutover
- 24h soak: zero hash mismatches, no orphan temp files
- Crash mid-copy: automatic recovery without operator intervention
- Verified jobs >= 99.5% within expected SLA window (define post metrics collection)