# Start Here Wednesday - GUI Console Implementation

**Date**: 2025-10-09 (Wednesday)
**Last Session**: 2025-10-07 (Tuesday)

---

## What We Decided

**Build a separate Dockerized GUI console** for ForkerDotNet monitoring and demonstrations.

### Why?

The PowerShell demo system works, but the user experience is fragmented:
- Multiple terminals (PowerShell scripts + Service logs)
- Multiple File Explorer windows
- DataGrip for database queries
- Manual correlation between all views

**User's feedback**: *"overall this is not a great user experience"*

### The Solution

**Technology**: Go + htmx (not Blazor, not React)
- Minimal dependencies (3 Go packages vs 1000+ for React)
- Easy vulnerability scanning
- 15MB Docker container
- No npm/build pipeline complexity
- Browser-based UI (http://localhost:5000)

**Architecture**:
```
ForkerDotNet.Service          GUI Console
(Windows native)              (Docker container)
├── Pure .NET 8               ├── Go + htmx
├── SQLite database    ←───   ├── Read-only DB access
├── No external deps          ├── Browser UI
└── Minimal attack surface    └── 15MB image
```

**Two Modes**:
1. **Production Mode**: Real-time monitoring dashboard, job tracking, hash verification
2. **Demo Mode**: One-click scenario execution with progress streaming

---

## What's Ready

### Design Documents (Complete)

1. **console-design.md** (500+ lines)
   - Technology selection rationale (Go + htmx vs alternatives)
   - Architecture overview with diagrams
   - Design decisions explained
   - Arguments against Blazor/React/desktop GUI
   - Security approach and NHS deployment considerations

2. **console-dev-task-list.md** (700+ lines)
   - 4-phase implementation plan (19-26 hours)
   - Phase 1: Core Infrastructure (4-6h) - SQLite integration, HTTP server
   - Phase 2: Production Dashboard (6-8h) - Real-time monitoring with SSE
   - Phase 3: Demo Mode (6-8h) - Automated scenario execution
   - Phase 4: Polish & Security (3-4h) - Docker, vulnerability scanning
   - Detailed tasks, code examples, success criteria

3. **TASK_LIST.md** (Updated)
   - Phase 12 marked complete (PowerShell demos validated)
   - Phase 13 added: GUI Console (separate track)
   - All demo-related tasks removed from main service list

---

## What We Accomplished Tuesday

### Phase 12: Visual Demo Validation ✅ COMPLETED

**Fixed Demo Mode Timing**:
- Added `TestingConfiguration` to appsettings.Demo.json
- `VerificationDelaySeconds: 10` allows corruption testing
- Modified `VerificationOrchestrator` to inject delays in Demo mode

**DataGrip Integration**:
- Replaced SQLiteBrowser across all 5 scenarios
- Created `Open-ForkerDatabase-Demo.sql` with curated queries
- Fixed SQL reserved word bug (table alias `to` → `t_o`)

**Script Fixes**:
- Fixed corruption function (XOR with 0xFF guarantees byte change)
- Fixed encoding errors (Unicode → ASCII in Cleanup script)
- Fixed `-Filter` parameter bug (changed to `-Include`)
- Fixed `$finalSize` undefined bug in Scenario 5
- Improved File Explorer window cleanup (COM automation)

**Environment Configuration**:
- Fixed `Program.cs` to detect `ASPNETCORE_ENVIRONMENT`
- Validated overlay pattern: `appsettings.{Environment}.json`
- Created `Start-ForkerDemo.ps1` convenience script

**Testing**:
- ✅ Scenario 1 (End-to-End) working
- ✅ Scenario 2 (Corruption Detection) working with delay
- ✅ Scenario 5 (Stability Detection) working

**Commit**: 3397647 + session work (not yet committed)

---

## Next Steps (Wednesday)

### Option 1: Start GUI Console Implementation (Recommended)

**Phase 1 Tasks** (4-6 hours):
1. Create new repository `forker-console/`
2. Initialize Go module and directory structure
3. Implement SQLite read-only integration
4. Build basic HTTP server with health endpoint
5. Test connection to `C:\ForkerDemo\forker.db`

**Why Start Here**:
- Console will massively improve demo experience
- Separate track - doesn't interfere with ForkerDotNet service
- Technology chosen (Go + htmx), design approved
- All planning documents complete

### Option 2: Performance Tuning (Phase 14)

If console work needs to wait, could tackle:
- Clinical pathway prioritization
- Buffer size experiments (64KB vs 256KB vs 1MB)
- Throughput validation with real medical files

### Option 3: Pre-Production Hardening (Phase 15)

Alternative focus:
- Configuration validation
- Security hardening (path canonicalization)
- Crash recovery rehearsal

---

## Key Files to Review

**Design & Planning**:
- [console-design.md](console-design.md) - Architecture and technology selection
- [console-dev-task-list.md](console-dev-task-list.md) - Implementation roadmap
- [TASK_LIST.md](TASK_LIST.md) - Overall project status

**Recent Changes**:
- [src/Forker.Service/appsettings.Demo.json](src/Forker.Service/appsettings.Demo.json) - Testing configuration
- [src/Forker.Infrastructure/Services/VerificationOrchestrator.cs](src/Forker.Infrastructure/Services/VerificationOrchestrator.cs) - Demo delays
- [scripts/Demo-Utilities.ps1](scripts/Demo-Utilities.ps1) - Fixed corruption function
- [scripts/Open-ForkerDatabase-Demo.sql](scripts/Open-ForkerDatabase-Demo.sql) - DataGrip queries

**Chat History**:
- [chats/session-2025-10-07.md](chats/session-2025-10-07.md) - Full Tuesday conversation

---

## Important Context

### Why Go + htmx?

**User's feedback**: *"I love the sound of Go + htmx but you will be doing all the work as I have no idea about this!"*

**Advantages**:
- Industry standard for containerized apps (Docker, Kubernetes written in Go)
- Minimal attack surface (3 packages vs 1000+ for React)
- No npm/webpack/build pipeline complexity
- Easy to scan for vulnerabilities (Trivy, gosec, Nancy)
- 15MB container vs 200MB+ for .NET alternatives

### Why Not Alternatives?

**Blazor Server**: 200MB+ container, .NET dependencies, SignalR complexity
**React/Vue/Angular**: 1000+ npm packages, build pipeline, massive CVE scanning burden
**Desktop GUI (Avalonia/WPF)**: NHS deployment blockers (AppLocker, code signing, 12-24 week approval)
**Integrated into Service**: Breaks separation of concerns, introduces risk to production code

### Deployment Model

**For Demos** (now): Run console on your development laptop, Docker Desktop
**For NHS Production** (future): Submit container for security scanning and approval

**Console is read-only observer** - zero interference with ForkerDotNet.Service

---

## Recommended Action

**Start with Phase 1 of GUI Console** - build core infrastructure and validate read-only database access works correctly.

If you prefer a different starting point, review `TASK_LIST.md` and choose the next priority that makes sense for your goals.

---

## Quick Reference

**Start ForkerDotNet Service in Demo Mode**:
```powershell
.\scripts\Start-ForkerDemo.ps1
```

**Run a demo scenario**:
```powershell
.\scripts\Run-Scenario1-EndToEnd.ps1
```

**Open database in DataGrip**:
```powershell
# Use Open-ForkerDatabase-Demo.sql in DataGrip
# Database path: C:\ForkerDemo\forker.db
```

**Check service health**:
```powershell
Invoke-RestMethod http://localhost:8080/health/live
```

---

**Ready to build the console? See [console-dev-task-list.md](console-dev-task-list.md) for detailed Phase 1 tasks.**
