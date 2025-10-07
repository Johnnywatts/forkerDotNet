# ForkerDotNet GUI Console - Development Task List

**Technology Stack:** Go 1.21+ | htmx 1.9+ | Alpine.js (optional) | Docker | SQLite read-only access

**Estimated Total Effort:** 19-26 hours

**Repository:** `forker-console/` (separate from main service)

---

## Phase 1: Core Infrastructure (4-6 hours)

### Task 1.1: Project Bootstrap
**Estimated Time:** 1 hour

- [ ] Create new repository `forker-console/`
- [ ] Initialize Go module: `go mod init github.com/nhs/forker-console`
- [ ] Create directory structure:
  ```
  forker-console/
  ├── cmd/console/main.go
  ├── internal/
  │   ├── server/server.go
  │   ├── database/sqlite.go
  │   └── models/domain.go
  ├── web/
  │   ├── templates/
  │   └── static/
  ├── Dockerfile
  ├── docker-compose.yml
  └── go.mod
  ```
- [ ] Install dependencies:
  - `github.com/mattn/go-sqlite3` (CGO-based SQLite driver)
  - `github.com/go-chi/chi/v5` (HTTP router)

**Success Criteria:** `go build ./cmd/console` succeeds, directory structure in place

---

### Task 1.2: SQLite Read-Only Integration
**Estimated Time:** 2-3 hours

**Files to Create:**
- `internal/database/sqlite.go` - Database connection manager
- `internal/database/queries.go` - Read-only query functions
- `internal/models/domain.go` - Domain models matching ForkerDotNet schema

**Implementation Details:**
```go
// internal/database/sqlite.go
type Database struct {
    conn *sql.DB
}

func NewDatabase(path string) (*Database, error) {
    // Open SQLite in read-only mode: file:path?mode=ro&immutable=1
    connString := fmt.Sprintf("file:%s?mode=ro&immutable=1", path)
    conn, err := sql.Open("sqlite3", connString)
    if err != nil {
        return nil, err
    }
    return &Database{conn: conn}, nil
}

func (db *Database) GetRecentJobs(limit int) ([]FileJob, error) {
    // Query FileJobs with TargetOutcomes joined
}

func (db *Database) GetJobDetails(id string) (*JobDetails, error) {
    // Query single job with all related data
}
```

**Domain Models:**
```go
type FileJob struct {
    ID           string
    SourcePath   string
    State        string
    InitialSize  int64
    SourceHash   *string
    CreatedAt    time.Time
    UpdatedAt    time.Time
    VersionToken int
}

type TargetOutcome struct {
    JobID           string
    TargetID        string
    State           string
    DestinationPath *string
    Hash            *string
    BytesCopied     *int64
}

type JobDetails struct {
    Job     FileJob
    Targets []TargetOutcome
    Events  []Event
}
```

**Testing:**
- [ ] Unit tests for read-only mode enforcement
- [ ] Test with actual ForkerDemo database: `C:\ForkerDemo\forker.db`
- [ ] Verify no write operations possible

**Success Criteria:** Console can query ForkerDotNet database without interfering with service

---

### Task 1.3: Basic HTTP Server
**Estimated Time:** 1-2 hours

**Files to Create:**
- `cmd/console/main.go` - Application entry point
- `internal/server/server.go` - HTTP server setup
- `internal/server/routes.go` - Route definitions

**Implementation:**
```go
// cmd/console/main.go
func main() {
    dbPath := os.Getenv("FORKER_DB_PATH")
    if dbPath == "" {
        dbPath = "/data/forker.db" // Docker mount path
    }

    db, err := database.NewDatabase(dbPath)
    if err != nil {
        log.Fatalf("Failed to open database: %v", err)
    }

    srv := server.New(db)
    log.Printf("Console listening on http://localhost:5000")
    http.ListenAndServe(":5000", srv)
}

// internal/server/server.go
func New(db *database.Database) http.Handler {
    r := chi.NewRouter()
    r.Use(middleware.Logger)
    r.Use(middleware.Recoverer)

    r.Get("/", handleDashboard)
    r.Get("/health", handleHealth)

    return r
}
```

**Testing:**
- [ ] Health endpoint returns JSON: `{"status":"healthy"}`
- [ ] Server starts without errors
- [ ] Graceful shutdown on Ctrl+C

**Success Criteria:** Server runs on http://localhost:5000, responds to health checks

---

## Phase 2: Production Dashboard (6-8 hours)

### Task 2.1: Dashboard Layout (htmx + HTML)
**Estimated Time:** 2-3 hours

**Files to Create:**
- `web/templates/base.html` - Base layout with htmx included
- `web/templates/dashboard.html` - Main dashboard view
- `web/static/style.css` - Minimal CSS for medical-grade UI

**Base Template:**
```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>ForkerDotNet Console</title>
    <script src="https://unpkg.com/htmx.org@1.9.10"></script>
    <link rel="stylesheet" href="/static/style.css">
</head>
<body>
    <header>
        <h1>ForkerDotNet Console</h1>
        <nav>
            <a href="/">Dashboard</a>
            <a href="/demo">Demo Mode</a>
        </nav>
    </header>
    <main>
        {{block "content" .}}{{end}}
    </main>
</body>
</html>
```

**Dashboard Layout Sections:**
1. **Summary Stats** (top cards)
   - Total jobs processed (24h)
   - Success rate
   - Active jobs
   - Quarantined files

2. **Recent Jobs Table**
   - Columns: Source File | State | Size | Hash | Targets | Time
   - Color coding: Green (Verified), Red (Quarantined), Yellow (InProgress)
   - Click row → detail view (htmx swap)

3. **Real-Time Updates**
   - SSE connection for new job notifications
   - Auto-refresh every 5 seconds

**Success Criteria:** Dashboard loads with static data, layout is clean and professional

---

### Task 2.2: Real-Time Job Monitoring (SSE)
**Estimated Time:** 2-3 hours

**Files to Create:**
- `internal/server/sse.go` - Server-Sent Events handler
- `internal/database/polling.go` - Database change detection

**Server-Sent Events Implementation:**
```go
// internal/server/sse.go
func handleJobUpdates(w http.ResponseWriter, r *http.Request) {
    w.Header().Set("Content-Type", "text/event-stream")
    w.Header().Set("Cache-Control", "no-cache")
    w.Header().Set("Connection", "keep-alive")

    ticker := time.NewTicker(2 * time.Second)
    defer ticker.Stop()

    lastVersion := 0
    for {
        select {
        case <-ticker.C:
            jobs, version := db.GetJobsSince(lastVersion)
            if version > lastVersion {
                html := renderJobsTable(jobs)
                fmt.Fprintf(w, "data: %s\n\n", html)
                w.(http.Flusher).Flush()
                lastVersion = version
            }
        case <-r.Context().Done():
            return
        }
    }
}
```

**HTML with htmx:**
```html
<div id="jobs-table"
     hx-get="/api/jobs/stream"
     hx-trigger="sse:update"
     hx-swap="innerHTML">
    <!-- Job rows rendered here -->
</div>
```

**Polling Strategy:**
- Query `MAX(VersionToken)` from FileJobs table
- If changed, fetch updated jobs
- Render HTML server-side, send via SSE

**Success Criteria:** Dashboard updates in real-time when ForkerDotNet processes files

---

### Task 2.3: Job Detail View
**Estimated Time:** 2 hours

**Files to Create:**
- `web/templates/job-detail.html` - Detailed job view
- `internal/server/handlers/job.go` - Job detail handler

**Detail View Components:**
1. **Job Header**
   - Source path, state badge, timestamps
   - Hash visualization (first 8 chars + copy button)

2. **Target Outcomes Table**
   - Per-target status (TargetA, TargetB)
   - Destination paths
   - Hash comparison (visual indicator if mismatch)
   - Bytes copied vs expected

3. **Event Timeline**
   - Chronological list of all state transitions
   - Timestamps, event types, metadata

4. **Actions** (if applicable)
   - Retry failed job (if State = Failed)
   - View source file info
   - Export job data (JSON)

**htmx Integration:**
```html
<tr hx-get="/api/jobs/{{.ID}}"
    hx-target="#detail-panel"
    hx-swap="innerHTML"
    class="clickable">
    <td>{{.SourcePath}}</td>
    <td><span class="badge {{.StateClass}}">{{.State}}</span></td>
</tr>
```

**Success Criteria:** Clicking job row loads detail view without page refresh

---

## Phase 3: Demo Mode (6-8 hours)

### Task 3.1: Demo Mode UI
**Estimated Time:** 2-3 hours

**Files to Create:**
- `web/templates/demo.html` - Demo mode page
- `internal/server/handlers/demo.go` - Demo execution handlers

**Demo Mode Layout:**
```
┌─────────────────────────────────────────────┐
│ Demo Mode - Automated Scenario Testing     │
├─────────────────────────────────────────────┤
│                                             │
│  [Scenario 1: End-to-End Copy] ─── RUN     │
│  Status: Ready                              │
│  Duration: ~2 minutes                       │
│                                             │
│  [Scenario 2: Corruption Detection] ─ RUN  │
│  Status: Ready                              │
│  Duration: ~3 minutes                       │
│                                             │
│  [Scenario 3: Concurrent Access] ─── RUN   │
│  [Scenario 4: Crash Recovery] ────── RUN   │
│  [Scenario 5: Stability Detection] ── RUN  │
│                                             │
├─────────────────────────────────────────────┤
│ Execution Log:                              │
│ [INFO] Environment check: PASSED            │
│ [INFO] Service health: HEALTHY              │
│ └─────────────────────────────────────────┘ │
└─────────────────────────────────────────────┘
```

**Scenario Buttons:**
- Each scenario is a button with htmx `hx-post="/api/demo/run/scenario1"`
- On click: Button disabled, progress indicator shown
- Execution log streams via SSE
- Completion shows success/failure badge

**Success Criteria:** Demo mode page loads with 5 scenario buttons, UI is clear

---

### Task 3.2: Scenario Execution Engine
**Estimated Time:** 3-4 hours

**Files to Create:**
- `internal/demo/scenarios.go` - Scenario definitions
- `internal/demo/executor.go` - Execution orchestration
- `internal/demo/fileops.go` - File generation, corruption, etc.

**Scenario Implementation:**
```go
// internal/demo/scenarios.go
type Scenario struct {
    ID          string
    Name        string
    Description string
    Duration    time.Duration
    Steps       []Step
}

type Step struct {
    Name     string
    Action   func(ctx context.Context) error
    Expected string // Expected outcome
}

var Scenario1_EndToEnd = Scenario{
    ID:   "scenario1",
    Name: "End-to-End Copy",
    Steps: []Step{
        {
            Name:     "Generate test file (10MB)",
            Action:   generateTestFile(10 * 1024 * 1024),
            Expected: "File created in Input folder",
        },
        {
            Name:     "Wait for discovery",
            Action:   waitForJobState("Discovered", 10*time.Second),
            Expected: "Job state = Discovered",
        },
        {
            Name:     "Wait for copy completion",
            Action:   waitForJobState("Copied", 30*time.Second),
            Expected: "Files in DestinationA and DestinationB",
        },
        {
            Name:     "Wait for verification",
            Action:   waitForJobState("Verified", 20*time.Second),
            Expected: "Job state = Verified, hashes match",
        },
    },
}
```

**Execution Handler:**
```go
func handleRunScenario(w http.ResponseWriter, r *http.Request) {
    scenarioID := chi.URLParam(r, "id")
    scenario := getScenario(scenarioID)

    // Stream execution via SSE
    w.Header().Set("Content-Type", "text/event-stream")

    for _, step := range scenario.Steps {
        fmt.Fprintf(w, "data: [INFO] %s\n\n", step.Name)
        w.(http.Flusher).Flush()

        err := step.Action(r.Context())
        if err != nil {
            fmt.Fprintf(w, "data: [ERROR] %s failed: %v\n\n", step.Name, err)
            return
        }

        fmt.Fprintf(w, "data: [OK] %s\n\n", step.Expected)
        w.(http.Flusher).Flush()
    }

    fmt.Fprintf(w, "data: [SUCCESS] Scenario completed\n\n")
}
```

**Critical Functions:**
- `generateTestFile(size)` - Create random binary data
- `corruptFile(path, position)` - XOR byte at position with 0xFF
- `waitForJobState(state, timeout)` - Poll database until state reached
- `verifyFileExists(path)` - Check file system
- `compareHashes(path1, path2)` - SHA-256 comparison

**Success Criteria:** Scenario 1 runs end-to-end, logs stream to UI, completes successfully

---

### Task 3.3: Demo Environment Validation
**Estimated Time:** 1 hour

**Files to Create:**
- `internal/demo/validation.go` - Environment checks

**Pre-Flight Checks:**
```go
func ValidateDemoEnvironment() []ValidationResult {
    results := []ValidationResult{}

    // Check 1: Service health endpoint
    results = append(results, checkServiceHealth("http://localhost:8080/health/live"))

    // Check 2: Demo database exists
    results = append(results, checkDatabaseExists("C:\\ForkerDemo\\forker.db"))

    // Check 3: Input folder writable
    results = append(results, checkFolderWritable("C:\\ForkerDemo\\Input"))

    // Check 4: Environment = Demo
    results = append(results, checkEnvironment("Demo"))

    // Check 5: Testing config enabled
    results = append(results, checkTestingConfig())

    return results
}
```

**UI Display:**
```
Pre-Flight Checks:
✓ Service health: HEALTHY
✓ Database: C:\ForkerDemo\forker.db (accessible)
✓ Input folder: Writable
✓ Environment: Demo mode
✓ Testing config: Enabled (VerificationDelaySeconds=10)
```

**Success Criteria:** All validation checks pass before allowing scenario execution

---

## Phase 4: Polish & Security (3-4 hours)

### Task 4.1: Docker Containerization
**Estimated Time:** 1-2 hours

**Files to Create:**
- `Dockerfile` - Multi-stage build
- `docker-compose.yml` - Service orchestration
- `.dockerignore` - Build exclusions

**Dockerfile:**
```dockerfile
# Stage 1: Build
FROM golang:1.21-alpine AS builder
WORKDIR /build
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=1 go build -ldflags="-w -s" -o console ./cmd/console

# Stage 2: Runtime
FROM alpine:3.19
RUN apk add --no-cache ca-certificates sqlite-libs

# Security hardening
RUN addgroup -g 1000 console && \
    adduser -D -u 1000 -G console console

WORKDIR /app
COPY --from=builder /build/console .
COPY --chown=console:console web/ ./web/

USER console
EXPOSE 5000

CMD ["./console"]
```

**docker-compose.yml:**
```yaml
version: '3.9'

services:
  console:
    build: .
    ports:
      - "5000:5000"
    volumes:
      - C:\ForkerDemo\forker.db:/data/forker.db:ro  # Read-only!
    environment:
      - FORKER_DB_PATH=/data/forker.db
      - FORKER_MODE=demo  # or 'production'
    restart: unless-stopped
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
    cap_add:
      - NET_BIND_SERVICE
    read_only: true
    tmpfs:
      - /tmp
```

**Build Commands:**
```bash
docker build -t forker-console:latest .
docker-compose up -d
```

**Testing:**
- [ ] Build succeeds, image size < 20MB
- [ ] Container starts without errors
- [ ] UI accessible at http://localhost:5000
- [ ] Database mounted read-only (verify with `docker exec`)

**Success Criteria:** Console runs in Docker, connects to ForkerDotNet database

---

### Task 4.2: Security Scanning & Hardening
**Estimated Time:** 1 hour

**Tools to Use:**
- **Trivy** - Container vulnerability scanning
- **gosec** - Go security linter
- **Nancy** - Go dependency vulnerability scanner

**Commands:**
```bash
# Container scanning
trivy image forker-console:latest

# Go code scanning
gosec ./...

# Dependency scanning
nancy go.sum
```

**Hardening Checklist:**
- [ ] Run as non-root user (UID 1000)
- [ ] Read-only root filesystem
- [ ] Drop all capabilities except NET_BIND_SERVICE
- [ ] No secrets in environment variables
- [ ] TLS for production deployment (optional for localhost demo)
- [ ] Rate limiting on API endpoints
- [ ] CORS headers configured

**Documentation:**
- [ ] Create `SECURITY.md` with scanning results
- [ ] Document vulnerability remediation
- [ ] Include SBOM generation command: `syft forker-console:latest -o spdx-json`

**Success Criteria:** Zero HIGH or CRITICAL vulnerabilities, security best practices documented

---

### Task 4.3: Documentation & User Guide
**Estimated Time:** 1 hour

**Files to Create:**
- `README.md` - Project overview and quick start
- `ARCHITECTURE.md` - Technical architecture
- `USER_GUIDE.md` - End-user documentation

**README.md Contents:**
```markdown
# ForkerDotNet GUI Console

Browser-based monitoring and demo console for ForkerDotNet service.

## Quick Start

### Demo Mode
docker-compose up -d
# Navigate to http://localhost:5000

### Production Mode
docker run -d -p 5000:5000 \
  -v /path/to/forker.db:/data/forker.db:ro \
  -e FORKER_MODE=production \
  forker-console:latest

## Features
- Real-time job monitoring
- Interactive demo scenarios
- SHA-256 hash verification visualization
- Zero-interference read-only database access
```

**USER_GUIDE.md Sections:**
1. Dashboard navigation
2. Interpreting job states and colors
3. Running demo scenarios
4. Troubleshooting common issues
5. FAQ (Why read-only? Why Go? etc.)

**Success Criteria:** Documentation is complete, new users can start console without assistance

---

## Testing & Validation

### Integration Testing (Throughout Development)
- [ ] Console reads database while ForkerDotNet service is running
- [ ] No write conflicts or locks
- [ ] Real-time updates appear within 5 seconds
- [ ] All 5 demo scenarios complete successfully
- [ ] Dashboard remains responsive with 1000+ jobs in database

### Security Testing (Phase 4)
- [ ] Trivy scan: 0 HIGH/CRITICAL vulnerabilities
- [ ] gosec scan: No security issues
- [ ] Read-only database mount enforced
- [ ] Container runs as non-root
- [ ] No sensitive data in logs

### Performance Testing
- [ ] Dashboard loads in < 500ms
- [ ] SSE updates don't cause memory leaks
- [ ] Container uses < 50MB RAM idle
- [ ] Handles 100+ concurrent SSE connections

---

## Deployment Checklist

### For Demos (Development Laptop)
- [ ] Docker Desktop installed
- [ ] ForkerDotNet service running in Demo mode
- [ ] Console docker-compose up
- [ ] Pre-flight checks pass
- [ ] All 5 scenarios tested

### For NHS Production (Future)
- [ ] Code signing certificate obtained
- [ ] Security scan reports generated
- [ ] SBOM submitted to security team
- [ ] Change request approved (12-24 weeks)
- [ ] Deployment documentation complete
- [ ] Rollback plan documented

---

## Dependency Summary

**Go Dependencies (Total: 3)**
- `github.com/mattn/go-sqlite3` - SQLite driver (CGO)
- `github.com/go-chi/chi/v5` - HTTP router
- Standard library only (net/http, html/template, etc.)

**Frontend Dependencies (CDN, no npm)**
- `htmx.org` - 14KB JavaScript library
- Custom CSS (~5KB)

**Container Dependencies**
- Alpine Linux base image
- sqlite-libs package
- ca-certificates

**Total Attack Surface:** ~15MB container, 3 Go packages, 1 frontend library

---

## Success Criteria (Overall)

### Phase 1 Complete When:
- Console queries ForkerDotNet database successfully
- HTTP server runs without errors
- Read-only access verified

### Phase 2 Complete When:
- Dashboard displays real-time job data
- SSE updates work reliably
- Job detail view loads correctly

### Phase 3 Complete When:
- All 5 demo scenarios execute successfully
- Execution logs stream to UI
- Pre-flight validation works

### Phase 4 Complete When:
- Docker image builds < 20MB
- Security scans pass
- Documentation complete
- User can run console with zero configuration

---

## Time Tracking

| Phase | Estimated | Actual | Notes |
|-------|-----------|--------|-------|
| Phase 1 | 4-6h | | |
| Phase 2 | 6-8h | | |
| Phase 3 | 6-8h | | |
| Phase 4 | 3-4h | | |
| **Total** | **19-26h** | | |

---

## Notes

- Console is **separate repository** from ForkerDotNet.Service
- No modifications to ForkerDotNet codebase required
- Console is purely read-only observer
- Docker is acceptable deployment model for NHS
- Go + htmx chosen for minimal dependencies and easy scanning
- All scenarios replicate PowerShell demo scripts but automated
