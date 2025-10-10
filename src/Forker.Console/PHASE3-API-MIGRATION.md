# Phase 3: API Migration - Console HTTP Client

## Overview

Phase 3 replaces direct SQLite access with HTTP API calls to eliminate cross-platform SQLite WAL locking issues between Windows ForkerDotNet service and Linux Docker console.

## Architecture Changes

### Before (Phase 2)
```
┌─────────────────────┐
│ Linux Docker        │
│ Console (Go)        │
│   └─ SQLite Driver  │
└──────────┬──────────┘
           │ nolock=1
           ▼
    ┌──────────────┐
    │ forker.db    │ (Windows host)
    │ + WAL files  │
    └──────────────┘
           ▲
           │ WAL mode
┌──────────┴──────────┐
│ ForkerDotNet        │
│ Service (.NET 8)    │
└─────────────────────┘
```

**Problem**: SQLite WAL locking conflicts between Windows and Linux file systems.

### After (Phase 3)
```
┌─────────────────────┐
│ Linux Docker        │
│ Console (Go)        │
│   └─ HTTP Client    │
└──────────┬──────────┘
           │ HTTP :8081
           ▼
┌─────────────────────┐
│ ForkerDotNet        │
│ MonitoringService   │ (Windows host)
│   └─ IJobRepository │
└──────────┬──────────┘
           │
           ▼
    ┌──────────────┐
    │ forker.db    │
    │ + WAL files  │
    └──────────────┘
```

**Solution**: Console accesses data via HTTP API on port 8081, eliminating direct database access.

## New Components

### 1. API Client (Go)
**Location**: `src/Forker.Console/internal/apiclient/`

**Files**:
- `client.go` - HTTP client with 5 endpoints
- `models.go` - DTOs matching C# MonitoringModels

**API Endpoints**:
- `GET /api/monitoring/health` - Service health check
- `GET /api/monitoring/stats` - Job statistics by state
- `GET /api/monitoring/jobs?state={state}&limit={n}` - Job summaries
- `GET /api/monitoring/jobs/{id}` - Job details with targets
- `POST /api/monitoring/requeue` - Requeue failed jobs

### 2. API-Based Handlers (Go)
**Location**: `src/Forker.Console/internal/server/handlers_api.go`

Replaces direct SQLite queries with HTTP API calls:
- `handleHealthAPI()` - Forwards to MonitoringService health endpoint
- `handleJobListAPI()` - Fetches jobs via API
- `handleJobDetailAPI()` - Fetches job details via API
- `handleStatsAPI()` - Fetches statistics via API
- `handleSSEAPI()` - Real-time updates via API polling

### 3. Dual-Mode Main (Go)
**Location**: `src/Forker.Console/cmd/console/main.go`

Supports both modes via environment variables:
- **API Mode** (Phase 3): `FORKER_API_URL=http://host.docker.internal:8081`
- **SQLite Mode** (Phase 2): `FORKER_DB_PATH=/data/forker.db`

### 4. MonitoringService (C#)
**Location**: `src/Forker.Service/MonitoringService.cs`

HTTP service exposing repository data on port 8081.

**Tests**: `tests/Forker.Infrastructure.Tests/Services/MonitoringServiceTests.cs` (10 tests, all passing)

## Environment Variables

### Phase 3 (API Mode)
```bash
FORKER_API_URL=http://host.docker.internal:8081  # Required for API mode
FORKER_MODE=api                                   # Optional documentation
```

### Phase 2 (SQLite Mode - Deprecated)
```bash
FORKER_DB_PATH=/data/forker.db                    # Path to SQLite database
FORKER_MODE=demo                                   # Optional documentation
```

## Docker Configuration

### Updated docker-compose.yml
```yaml
services:
  forker-console:
    environment:
      - FORKER_API_URL=http://host.docker.internal:8081
      - FORKER_MODE=api
    extra_hosts:
      - "host.docker.internal:host-gateway"  # Route to Windows host
    # No longer needs volume mount for database access
```

### Key Changes
1. Removed `volumes:` section (no direct DB access)
2. Added `extra_hosts:` to route `host.docker.internal` to Windows host
3. Changed environment variables from DB path to API URL

## Testing

### Unit Tests (C#)
```bash
cd tests/Forker.Infrastructure.Tests
dotnet test --filter "MonitoringServiceTests"
```

**Results**: 10/10 tests passing

### Integration Testing
1. Start ForkerDotNet service (Windows):
   ```bash
   $env:ASPNETCORE_ENVIRONMENT="Demo"
   dotnet run --project src/Forker.Service
   ```

2. Verify MonitoringService is running:
   ```bash
   curl http://localhost:8081/api/monitoring/health
   ```

3. Build and run console (Docker):
   ```bash
   cd src/Forker.Console
   docker-compose up --build
   ```

4. Access console:
   - Dashboard: http://localhost:5000
   - Health: http://localhost:5000/health

## Deployment

### Step 1: Deploy ForkerDotNet Service
```bash
# Windows host
dotnet publish src/Forker.Service -c Release
# Install as Windows Service (existing process)
```

### Step 2: Deploy Console (Docker)
```bash
cd src/Forker.Console
docker-compose up -d
```

### Step 3: Verify Communication
```bash
# Check console can reach API
docker exec -it forker-console /console
# Should log: "API connection established"
```

## Rollback to Phase 2

If issues occur, revert to SQLite mode:

1. Update `docker-compose.yml`:
   ```yaml
   environment:
     - FORKER_DB_PATH=/data/forker.db
     - FORKER_MODE=demo
   volumes:
     - C:\ForkerDemo:/data
   # Remove extra_hosts section
   ```

2. Restart container:
   ```bash
   docker-compose up -d
   ```

The dual-mode `main.go` will detect missing `FORKER_API_URL` and fall back to SQLite mode.

## Security Considerations

### Advantages
- **Eliminates file system access**: Console no longer needs read access to Windows files
- **Proper abstraction**: API enforces read-only access via repository layer
- **Audit trail**: All API requests logged by MonitoringService

### Network Security
- MonitoringService binds to `localhost:8081` (not exposed to network)
- Docker uses `host.docker.internal` for localhost routing
- No authentication required (localhost-only, trusted environment)

## Performance Impact

### API Overhead
- HTTP request latency: ~5-15ms (localhost)
- JSON serialization: ~1-2ms per request
- Total overhead: ~10-20ms per page load

### Benefits
- **Eliminates SQLite locking delays**: No more 1-2 second WAL lock waits
- **Net improvement**: 10-20ms API overhead << 1000-2000ms locking delays

## Files Modified

### New Files
- `src/Forker.Console/internal/apiclient/client.go`
- `src/Forker.Console/internal/apiclient/models.go`
- `src/Forker.Console/internal/server/handlers_api.go`
- `src/Forker.Console/internal/server/router_api.go`
- `src/Forker.Console/cmd/console/main.go` (replaced)
- `tests/Forker.Infrastructure.Tests/Services/MonitoringServiceTests.cs`

### Modified Files
- `src/Forker.Console/docker-compose.yml` - API mode configuration
- `src/Forker.Console/internal/server/context.go` - Added API client storage
- `src/Forker.Service/Program.cs` - Registered MonitoringService

### Deprecated Files (Phase 2)
- `src/Forker.Console/internal/database/sqlite.go` - Direct DB access (kept for rollback)
- `src/Forker.Console/internal/server/handlers.go` - SQLite-based handlers (kept for rollback)
- `src/Forker.Console/cmd/console/main_sqlite_only.go.bak` - Original main.go

## Next Steps (Phase 3 Remaining)

- [x] Task 3.1: ForkerDotNet Monitoring API (C#) ✅ COMPLETE
- [x] Task 3.2: Console HTTP Client (Go) ✅ COMPLETE
- [ ] Task 3.3: Filesystem Scanner (Go) - Scan folders for file listings
- [ ] Task 3.4: Update UI Templates - 4-pane folder view + 2-pane transaction view
- [ ] Task 3.5: Docker Configuration - Update docker-compose.yml ✅ COMPLETE (part of 3.2)
- [ ] Task 3.6: Integration Testing - Verify console ↔ API communication

## Troubleshooting

### Issue: "undefined" Target States in Active Pane (2025-10-10)

**Symptom**: Transactions page Active pane showed "undefined" for target copy states (e.g., "Copying", "Verifying").

**Root Cause**: Field name mismatch in data flow chain:
1. **C# Service** (`MonitoringModels.cs:68`): Returns `CopyState` property → JSON serializes as `copyState` (camelCase)
2. **Go API Client** (`apiclient/models.go:51`): Had `State` field with `json:"state"` tag → Failed to unmarshal `copyState`
3. **Go Console** (`handlers_api.go:1208`): Referenced `target.State` → Got empty string
4. **JavaScript** (`handlers_api.go:714`): Read `target.copyState` → Got `undefined`

**Fix Applied**:
```go
// apiclient/models.go - BEFORE (WRONG)
type TargetOutcome struct {
    TargetID string  `json:"targetId"`
    State    string  `json:"state"`      // ❌ Mismatched field name
    ...
}

// apiclient/models.go - AFTER (CORRECT)
type TargetOutcome struct {
    TargetID  string  `json:"targetId"`
    CopyState string  `json:"copyState"` // ✅ Matches C# API
    ...
}
```

```go
// handlers_api.go:1208 - BEFORE (WRONG)
State: target.State,  // ❌ Referenced old field name

// handlers_api.go:1208 - AFTER (CORRECT)
State: target.CopyState,  // ✅ Updated reference
```

```javascript
// handlers_api.go:714-715 - Already correct (no change needed)
const badge = getTargetStateBadge(target.copyState);
const operation = getTargetStateDescription(target.copyState, target.targetId);
```

**Important Rebuild Note**: When modifying Go code, use `docker-compose build --no-cache` instead of `docker build`. Docker Compose creates its own image (`forkerconsole-forker-console`) separate from manually tagged images (`forker-console:latest`).

**Testing the Fix**:
```bash
# Test C# API directly (should show copyState)
curl -s "http://localhost:8081/api/monitoring/jobs/{id}" | grep copyState

# Test Go Console API (should also show copyState)
curl -s "http://localhost:5000/api/jobs/{id}" | grep copyState

# Both should return: "copyState": "Copying" (or Verifying, Verified, etc.)
```

**Data Flow Verification**:
```
C# Service → JSON: {"copyState": "Copying"}
     ↓
Go Client unmarshals: TargetOutcome.CopyState = "Copying"
     ↓
Go Handler accesses: target.CopyState → "Copying"
     ↓
Go Console serializes: {"copyState": "Copying"}
     ↓
Browser JavaScript reads: target.copyState → "Copying" ✅
```

## References

- **Design Document**: `console-design.md` (Decision 6)
- **Implementation Plan**: `console-dev-task-list.md` (Phase 3)
- **Test Results**: `consolidated_tests_results_run_1.md` (MonitoringServiceTests)
