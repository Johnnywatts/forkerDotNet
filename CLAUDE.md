# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ForkerDotNet is a production-grade .NET 8 file copier service designed for large medical imaging files (SVS format, 500MB-20GB). This is a complete rewrite of the PowerShell-based forker service with enterprise security, reliability, and observability features.

**Current Status**: Repository setup phase - no .NET solution structure exists yet. This is a planning and design repository containing comprehensive documentation for the implementation.

## Key Architecture Principles

- **Domain-Driven Design**: Strong invariants and state machines for file job processing
- **Dual-Target Replication**: Simultaneous copy to multiple destinations with atomic operations
- **Medical Data Integrity**: SHA-256 verification mandatory for clinical files
- **Crash Recovery**: SQLite WAL-based persistence with automatic recovery
- **Non-Locking Operations**: External polling systems can access files during copy/verify
- **Security Hardened**: FIPS-compliant crypto, least-privilege service account

## Planned Project Structure

```
forkerDotNet/
├── src/
│   ├── Forker.Domain/          # Core domain logic, entities, invariants
│   ├── Forker.Infrastructure/  # SQLite, file system, hashing, metrics
│   └── Forker.Service/         # Worker Service host, API endpoints
├── tests/
│   ├── Forker.Domain.Tests/    # Unit tests with property-based testing
│   ├── Forker.Infrastructure.Tests/  # Integration tests
│   └── Forker.Resilience.Tests/      # Chaos engineering & fault injection
└── config/                     # Configuration files from PowerShell version
```

## Core Domain Model

### State Machines
- **JobState**: DISCOVERED → QUEUED → IN_PROGRESS → PARTIAL → VERIFIED
- **TargetCopyState**: PENDING → COPYING → COPIED → VERIFYING → VERIFIED
- **Failure branches**: FAILED, QUARANTINED (hash mismatch)

### Key Entities
- **FileJob**: Central entity tracking source file and overall state
- **TargetOutcome**: Per-target state tracking (TargetA, TargetB)
- **Events**: Append-only audit log for all state transitions

### Critical Invariants (I1-I20)
The system enforces 20 invariants covering state transitions, data integrity, concurrency control, and crash recovery. Key examples:
- I2: Job VERIFIED only if all targets VERIFIED & hashes match
- I4: Restart does not duplicate final writes
- I5: Hash mismatch → QUARANTINED (no silent retry)

## Technology Stack

- **.NET 8 LTS**: Framework-dependent deployment for security patching
- **SQLite with WAL**: Crash-safe persistence layer
- **SHA-256**: FIPS-compliant hashing for medical data integrity
- **Serilog + OpenTelemetry**: Structured logging and metrics
- **Windows Service**: Deployment via NSSM or built-in service host

## Configuration

The service uses two main configuration patterns inherited from the PowerShell version:
- **settings.json**: Linux-style paths for cross-platform testing
- **service-config.json**: Windows production paths with comprehensive retry policies

Key configuration areas:
- File watching patterns (*.svs, *.tiff, *.ndpi, *.scn medical formats)
- Dual-target replication setup
- Stability detection (file growth monitoring)
- Retry strategies with exponential backoff
- Performance monitoring and alerting thresholds

## Development Commands

**Note**: No .NET solution exists yet. The following commands will be available after Phase 1 implementation:

```bash
# After solution creation:
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Forker.Service

# Testing commands (planned):
dotnet test --collect:"XPlat Code Coverage"
dotnet test tests/Forker.Resilience.Tests  # Chaos engineering tests
```

## Implementation Phases

The project follows a 12-phase implementation plan over 30 days:

1. **Phase 1** (Day 0-1): Solution skeleton + health endpoints
2. **Phase 2** (Day 1-3): Domain core with state machines
3. **Phase 3** (Day 3-5): SQLite persistence layer
4. **Phase 4** (Day 5-7): File discovery and stability detection
5. **Phase 5** (Day 7-10): Single-target copy pipeline
6. **Phase 6** (Day 10-13): Multi-target verification
7. **Phase 7** (Day 13-15): Retry and backoff logic
8. **Phase 8** (Day 15-17): Adaptive concurrency control
9. **Phase 9** (Day 17-19): Observability maturity
10. **Phase 10** (Day 19-24): Resilience testing harness
11. **Phase 11** (Day 24-26): Performance tuning
12. **Phase 12** (Day 26-30): Production hardening

## Testing Strategy

- **Unit Tests**: 95%+ coverage with FsCheck property-based testing
- **Integration Tests**: SQLite state validation and concurrent operations
- **System Tests**: 24-hour soak testing with large files (20GB+)
- **Chaos Tests**: Fault injection and crash recovery validation
- **Security Tests**: Penetration testing and FIPS compliance

## Critical Requirements

- **Performance**: 1GB/min per target, <100MB memory usage
- **File Size**: Handle 500MB-20GB medical imaging files
- **Integrity**: Zero tolerance for hash mismatches
- **Availability**: Automatic recovery from crashes
- **Security**: NHS-grade security requirements
- **Observability**: Prometheus metrics and structured logging

## Key Documentation Files

- `README.md`: Project overview and architecture summary
- `dev_plan.md`: Detailed 30-day implementation roadmap with invariants
- `dotNetRebuild.md`: Complete technical architecture specification
- `next-steps-refactor-dotNet.md`: Phase-by-phase implementation guide
- `security-*.md`: NHS-grade security requirements and implementation details

## Development Notes

- All code must enforce the 20 documented invariants (I1-I20)
- Use streaming operations for memory efficiency with large files
- Implement comprehensive logging for audit trail requirements
- Follow domain-driven design principles with strong boundaries
- Prioritize reliability and correctness over raw performance
- Include crash recovery testing in all major features