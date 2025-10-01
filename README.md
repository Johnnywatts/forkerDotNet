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

**Status**: ✅ **Production Ready** (Phase 11.0 Complete)

- ✅ All 88 tests passing
- ✅ Windows Service deployment automation complete
- ✅ Real demo system with PowerShell scripts
- 📝 Phase 11.3+ in progress (see `TASK_LIST.md`)

See `dev_plan.md` for full 30-day implementation roadmap.

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

## Quick Start

### Option 1: Run Demonstrations (Recommended)

```powershell
# Setup demo environment
cd demo\scripts
.\Demo-Setup.ps1

# Run end-to-end demo (5 minutes)
.\Run-Scenario1-EndToEnd.ps1
```

**See [Quick-Start-Demo.md](docs/Quick-Start-Demo.md) for full demo guide** with 5 scenarios:
1. End-to-End Workflow (5 min)
2. Corruption Detection (3 min)
3. Concurrent Access (5 min)
4. Crash Recovery (5 min)
5. Stability Detection (3 min)

### Option 2: Production Deployment

```powershell
# Build solution
dotnet build --configuration Release

# Run tests
dotnet test

# Install Windows Service
cd demo\scripts
.\Install-Service.ps1

# Start service
Start-Service ForkerDotNet
```

**See [windows-service-deployment.md](docs/windows-service-deployment.md) for full deployment guide**

### Option 3: Development

1. Ensure .NET 8 SDK is installed
2. Run `dotnet restore` to restore dependencies
3. Run `dotnet build` to build solution
4. Run `dotnet test` to verify all tests pass (88/88 expected)
5. See `dev_plan.md` for architecture and development roadmap

This project emphasizes **proof** at every level - comprehensive test coverage, performance benchmarks, security validation, and reliability evidence suitable for medical data processing environments.