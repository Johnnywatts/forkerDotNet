# ForkerDotNet Console - Implementation Details

**Purpose:** Technical implementation specifications extracted from the task list for reference during development.

---

## Phase 1 Implementation Details

### Task 1.2: SQLite Read-Only Integration

**Connection String:**
```go
// internal/database/sqlite.go
connString := fmt.Sprintf("file:%s?mode=ro&immutable=1", path)
conn, err := sql.Open("sqlite", connString)
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

**Database Functions:**
```go
func NewDatabase(path string) (*Database, error)
func (db *Database) Close() error
func (db *Database) Ping() error
func (db *Database) GetRecentJobs(limit int) ([]FileJob, error)
func (db *Database) GetJobDetails(id string) (*JobDetails, error)
func (db *Database) GetStats() (*Stats, error)
```

---

### Task 1.3: Basic HTTP Server

**Server Setup:**
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
```

**Router (stdlib):**
```go
// internal/server/router.go
func NewRouter() http.Handler {
    mux := http.NewServeMux()

    mux.HandleFunc("/", handleDashboard)
    mux.HandleFunc("/health", handleHealth)
    mux.HandleFunc("/api/jobs", handleJobList)
    mux.HandleFunc("/api/jobs/", func(w http.ResponseWriter, r *http.Request) {
        id := PathParam(r.URL.Path, "/api/jobs/")
        handleJobDetail(w, r, id)
    })

    handler := Recoverer(Logger(mux))
    return handler
}
```

**Custom Middleware:**
```go
// internal/server/middleware.go
func Recoverer(next http.Handler) http.Handler {
    return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
        defer func() {
            if err := recover(); err != nil {
                log.Printf("[ERROR] PANIC: %v", err)
                http.Error(w, "Internal Server Error", 500)
            }
        }()
        next.ServeHTTP(w, r)
    })
}

func Logger(next http.Handler) http.Handler {
    return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
        start := time.Now()
        next.ServeHTTP(w, r)
        log.Printf("[INFO] %s %s %v", r.Method, r.URL.Path, time.Since(start))
    })
}
```

**Path Parameter Helper:**
```go
func PathParam(path, prefix string) string {
    return strings.TrimPrefix(path, prefix)
}
```

---

## Phase 2 Implementation Details

### Task 2.1: Dashboard Layout

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

**Dashboard Sections:**
1. Summary Stats (total jobs, success rate, active, quarantined)
2. Recent Jobs Table (source, state, size, hash, targets, time)
3. Real-Time Updates (SSE, auto-refresh every 5s)

---

### Task 2.2: Real-Time Job Monitoring (SSE)

**Server-Sent Events Handler:**
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

---

### Task 2.3: Job Detail View

**Detail View Components:**
```html
<div class="job-detail">
    <!-- Job Header -->
    <h3>{{.Job.SourcePath}}</h3>
    <span class="badge {{.Job.StateClass}}">{{.Job.State}}</span>

    <!-- Target Outcomes -->
    <table>
        <tr>
            <th>Target</th>
            <th>State</th>
            <th>Destination</th>
            <th>Hash</th>
            <th>Bytes Copied</th>
        </tr>
        {{range .Targets}}
        <tr>
            <td>{{.TargetID}}</td>
            <td>{{.State}}</td>
            <td>{{.DestinationPath}}</td>
            <td>{{.Hash}}</td>
            <td>{{.BytesCopied}}</td>
        </tr>
        {{end}}
    </table>

    <!-- Event Timeline -->
    <div class="events">
        {{range .Events}}
        <div class="event">
            <span class="timestamp">{{.Timestamp}}</span>
            <span class="type">{{.EventType}}</span>
            <span class="metadata">{{.Metadata}}</span>
        </div>
        {{end}}
    </div>
</div>
```

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

---

## Phase 3 Implementation Details

### Task 3.1: Demo Mode UI

**Demo Mode Layout:**
```html
<div class="demo-mode">
    <h2>Demo Mode - Automated Scenario Testing</h2>

    <div class="scenarios">
        <div class="scenario">
            <h3>Scenario 1: End-to-End Copy</h3>
            <p>Duration: ~2 minutes</p>
            <button hx-post="/api/demo/run/scenario1"
                    hx-target="#demo-log">Run</button>
        </div>
        <!-- More scenarios... -->
    </div>

    <div id="demo-log" class="execution-log">
        <!-- Logs stream here via SSE -->
    </div>
</div>
```

---

### Task 3.2: Scenario Execution Engine

**Scenario Definition:**
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
    Expected string
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
        // More steps...
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
```go
func generateTestFile(size int64) func(context.Context) error
func corruptFile(path string, position int64) func(context.Context) error
func waitForJobState(state string, timeout time.Duration) func(context.Context) error
func verifyFileExists(path string) func(context.Context) error
func compareHashes(path1, path2 string) func(context.Context) error
```

---

### Task 3.3: Demo Environment Validation

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

---

## Phase 4 Implementation Details

### Task 4.1: Docker Containerization

**Linux Dockerfile (scratch):**
```dockerfile
FROM golang:1.23-alpine AS builder
WORKDIR /build
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -ldflags="-s -w" -o console ./cmd/console

FROM scratch
COPY --from=builder /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/
COPY --from=builder /build/console /console
COPY --from=builder /build/web /web
EXPOSE 5000
USER 1000:1000
ENTRYPOINT ["/console"]
```

**Windows Dockerfile (nanoserver):**
```dockerfile
FROM golang:1.23-windowsservercore-ltsc2022 AS builder
WORKDIR C:\build
COPY go.mod go.sum .\
RUN go mod download
COPY . .
RUN go build -ldflags="-s -w" -o console.exe .\cmd\console

FROM mcr.microsoft.com/windows/nanoserver:ltsc2022
WORKDIR C:\app
COPY --from=builder C:\build\console.exe .
COPY --from=builder C:\build\web .\web
EXPOSE 5000
ENTRYPOINT ["C:\\app\\console.exe"]
```

**docker-compose.yml:**
```yaml
version: '3.9'

services:
  forker-console:
    build:
      context: .
      dockerfile: Dockerfile
    container_name: forker-console
    ports:
      - "127.0.0.1:5000:5000"
    volumes:
      - C:\ForkerDemo\forker.db:/data/forker.db:ro
    environment:
      - FORKER_DB_PATH=/data/forker.db
      - FORKER_MODE=demo
    restart: unless-stopped
    security_opt:
      - no-new-privileges:true
    read_only: true
    tmpfs:
      - /tmp
    cap_drop:
      - ALL
    mem_limit: 256m
    cpus: 0.5
```

---

### Task 4.2: Security Scanning Commands

**Trivy:**
```bash
trivy image forker-console:latest \
  --severity HIGH,CRITICAL \
  --exit-code 1
```

**Docker Scout:**
```bash
docker scout cves forker-console:latest \
  --only-severity critical,high,medium
```

**Go Vulnerability Check:**
```bash
govulncheck ./...
```

**SBOM Generation:**
```bash
syft forker-console:latest -o spdx-json > sbom.json
```

---

## Reference Architecture

### Directory Structure
```
src/Forker.Console/
├── cmd/console/main.go              # Entry point
├── internal/
│   ├── server/
│   │   ├── router.go                # HTTP routing (stdlib)
│   │   ├── middleware.go            # Recoverer, Logger
│   │   ├── context.go               # Database context
│   │   └── sse.go                   # Server-Sent Events
│   ├── database/
│   │   ├── sqlite.go                # Read-only SQLite
│   │   ├── models.go                # Domain models
│   │   └── polling.go               # Change detection
│   └── demo/
│       ├── scenarios.go             # Scenario definitions
│       ├── executor.go              # Execution engine
│       ├── fileops.go               # File operations
│       └── validation.go            # Environment checks
├── web/
│   ├── templates/
│   │   ├── base.html
│   │   ├── dashboard.html
│   │   ├── job-detail.html
│   │   └── demo.html
│   └── static/
│       └── style.css
├── go.mod
├── go.sum
├── Dockerfile                       # Linux container
├── Dockerfile.windows               # Windows container
├── docker-compose.yml
├── docker-compose.windows.yml
├── build.ps1
└── build-windows.ps1
```

### API Endpoints
```
GET  /                      # Dashboard
GET  /health                # Health check
GET  /api/jobs              # List jobs
GET  /api/jobs/{id}         # Job details
GET  /api/jobs/stream       # SSE job updates
GET  /demo                  # Demo mode page
POST /api/demo/run/{id}     # Run scenario
GET  /static/*              # Static files
```

### Data Flow
```
Browser
  ↓ HTTP
Server (Go stdlib router)
  ↓ Read-only SQL
SQLite Database (ForkerDotNet)
  ↑ Write
ForkerDotNet.Service
```

---

**Last Updated:** 2025-10-08
**See Also:**
- [console-dev-task-list.md](../../console-dev-task-list.md) - Clean task list
- [console-design.md](../../console-design.md) - Design decisions
- [SECURITY.md](SECURITY.md) - Security architecture
