# ForkerDotNet - Enterprise File Copier Service

## Overview

ForkerDotNet is a production-grade .NET 8 file copier service designed for large medical imaging files (SVS format, 500MB-20GB). This is a complete rewrite of the PowerShell-based forker service with enterprise security, reliability, and observability features.

## Key Features

- **Medical Data Integrity**: SHA-256 verification for clinical files
- **Non-Locking Operations**: External polling systems can access files during copy/verify
- **Dual-Target Replication**: Simultaneous copy to multiple destinations
- **Crash Recovery**: SQLite WAL-based persistence with automatic recovery
- **Security Hardened**: FIPS-compliant crypto, least-privilege service account
- **Comprehensive Testing**: Unit, integration, system, and chaos testing with proof artifacts

## Architecture

- **.NET 8 LTS**: Framework-dependent deployment for security patching
- **Domain-Driven Design**: Strong invariants and state machines
- **SQLite Persistence**: WAL mode for crash safety
- **Streaming Operations**: Memory-efficient processing of large files
- **Observability**: Prometheus metrics, structured logging, health endpoints

## Project Structure

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
├── config/                     # Configuration files from PowerShell version
├── docs/                       # Documentation and proof artifacts
└── dev_plan.md                 # Detailed development plan
```

## Security Features

- **Service Account**: gMSA or virtual service account (not LocalSystem)
- **Path Security**: Canonicalization, no reparse point following
- **Crypto**: FIPS-compliant SHA-256 via IncrementalHash
- **Network**: localhost-only endpoints, optional Windows Auth
- **Supply Chain**: SBOM generation, dependency scanning

## Testing Strategy

### Unit Tests (95%+ Coverage)
- Property-based testing with FsCheck
- Mutation testing with Stryker.NET
- Performance benchmarking with BenchmarkDotNet

### Integration Tests
- SQLite database state validation
- Concurrent operation testing
- File system state auditing

### System Tests
- 24-hour soak testing
- Large file processing (20GB+)
- Memory leak detection
- Crash recovery validation

### Security Tests
- Penetration testing
- SBOM and vulnerability scanning
- FIPS compliance validation

## Development Status

**Current Phase**: Repository setup and solution structure
**Next Phase**: Domain core implementation with state machines

See `dev_plan.md` for detailed 30-day implementation roadmap.

## Requirements from PowerShell Analysis

- SVS files: 500MB-20GB medical imaging files
- No file locking during verification (external polling requirement)
- Dual-target replication with atomic operations
- SHA256 verification mandatory for medical data integrity
- Performance: 1GB/min per target, <100MB memory usage
- Windows Service deployment with automatic recovery

## Documentation

- `dev_plan.md` - Comprehensive development plan with 12 phases
- `dotNetRebuild.md` - Complete architecture specification
- `security-*.md` - NHS-grade security requirements and implementation
- `next-steps-refactor-dotNet.md` - Implementation roadmap

## Getting Started

1. Ensure .NET 8 SDK is installed
2. Open VS Code workspace in this directory
3. Run `dotnet restore` to restore dependencies
4. Follow the phase-by-phase implementation in `dev_plan.md`

This project emphasizes **proof** at every level - comprehensive test coverage, performance benchmarks, security validation, and reliability evidence suitable for medical data processing environments.