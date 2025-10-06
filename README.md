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
│       ├── appsettings.json               # Production config (default)
│       ├── appsettings.Demo.json          # Demo environment config
│       └── appsettings.SlowDrive.json     # Test environment config
├── tests/
│   ├── Forker.Domain.Tests/    # Unit tests with property-based testing
│   ├── Forker.Infrastructure.Tests/  # Integration tests
│   └── Forker.Resilience.Tests/      # Chaos engineering & fault injection
├── scripts/                    # PowerShell demo and setup scripts
│   ├── Demo-Setup.ps1
│   ├── Production-Setup.ps1
│   └── Run-Scenario*.ps1      # 5 demo scenarios
├── docs/                       # Documentation and proof artifacts
├── CONFIGURATION.md            # Environment configuration guide
└── demo-user-guide.md          # Observable demo system guide
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

### Option 1: Run Demonstrations (Recommended First Step)

**Setup demo environment:**
```powershell
# Run as Administrator
.\scripts\Demo-Setup.ps1
```

**Start ForkerDotNet service in Demo mode:**
```powershell
# Terminal 1: Start service
$env:ASPNETCORE_ENVIRONMENT = "Demo"
cd src\Forker.Service
dotnet run
```

**Run end-to-end demo:**
```powershell
# Terminal 2: Run demo script
.\scripts\Run-Scenario1-EndToEnd.ps1
```

**This demonstrates:**
- Real file operations with Windows File Explorer
- Dual-target replication (DestinationA + DestinationB)
- SHA-256 hash verification with PowerShell Get-FileHash
- SQLite database monitoring (use DataGrip: `C:\ForkerDemo\forker.db`)

**Full demo guide:** [demo-user-guide.md](demo-user-guide.md) - 5 observable scenarios with real tools

---

### Option 2: Production Deployment

**Setup production environment:**
```powershell
# Run as Administrator
.\scripts\Production-Setup.ps1
```

**Install as Windows Service:**
```powershell
# Builds solution and installs service
.\scripts\Install-ForkerService.ps1

# Start production service
Start-Service ForkerDotNet
```

**Configuration:** Production uses `C:\ProgramData\ForkerDotNet` paths (default)
**Full configuration guide:** [CONFIGURATION.md](CONFIGURATION.md)

---

### Option 3: Development

**Build and test:**
```powershell
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run all tests (expect 249/249 passing)
dotnet test
```

**Run in specific environments:**
```powershell
# Production (default)
dotnet run --project src\Forker.Service

# Demo environment
$env:ASPNETCORE_ENVIRONMENT="Demo"
dotnet run --project src\Forker.Service

# SlowDrive test environment
$env:ASPNETCORE_ENVIRONMENT="SlowDrive"
dotnet run --project src\Forker.Service
```

---

## Database Monitoring

**For demos and development:**
- **Database path:** `C:\ForkerDemo\forker.db` (Demo) or `C:\ProgramData\ForkerDotNet\forker.db` (Production)
- **Tools:** DataGrip (recommended) or DB Browser for SQLite
- **Key tables:** FileJobs, TargetOutcomes, QuarantineEntries

**Query examples:** See [CONFIGURATION.md](CONFIGURATION.md#database-locations-summary)

---

This project emphasizes **proof** at every level - comprehensive test coverage, performance benchmarks, security validation, and reliability evidence suitable for medical data processing environments.