# ForkerDotNet Management Console - Design Document

**Version:** 1.1
**Date:** 2025-10-08
**Status:** Updated Design - API-First Architecture

---

## Executive Summary

The ForkerDotNet Management Console is a **web-based monitoring and demonstration tool** delivered as a **Dockerized application** using **Go + htmx** technology stack. It provides:

1. **Production Monitoring Mode:** Real-time dashboard for operational monitoring
2. **Demo Mode:** One-click automated scenarios for stakeholder demonstrations

**Key Architecture Decision:** The console is **completely separate** from the ForkerDotNet.Service, ensuring zero impact on production reliability.

---

## Table of Contents

1. [Technology Selection](#technology-selection)
2. [Architecture Overview](#architecture-overview)
3. [Design Decisions & Rationale](#design-decisions--rationale)
4. [Arguments Against Alternative Options](#arguments-against-alternative-options)
5. [Security & NHS Deployment](#security--nhs-deployment)
6. [Implementation Approach](#implementation-approach)

---

## Technology Selection

### Selected Stack: Go + htmx

**Backend:** Go 1.21+
- Modern compiled language (Google-developed)
- Industry standard for containerized applications
- Used by Docker, Kubernetes, HashiCorp, Cloudflare
- Minimal dependencies, excellent security track record

**Frontend:** htmx + Alpine.js (optional)
- htmx: 14KB JavaScript library for interactive HTML
- Server-rendered UI (like Blazor, but simpler)
- No build pipeline, no npm/webpack complexity
- Alpine.js: Optional 15KB library for client-side interactivity

**Data Access:** HTTP API + Filesystem
- HTTP API for database queries (job states, statistics, service health)
- Direct filesystem access for folder views (Input, DestA, DestB, Failed)
- Read-only access to all data sources

**Deployment:** Docker container
- Self-contained, isolated environment
- Easy vulnerability scanning
- NHS-friendly deployment model

---

## Architecture Overview

### System Context

```
┌──────────────────────────────────────────────────────────────────┐
│ Windows Server / Workstation                                     │
│                                                                   │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ ForkerDotNet.Service (Native Windows Service)             │  │
│  │                                                            │  │
│  │ • Pure .NET 8 C# service                                  │  │
│  │ • SQLite database with WAL mode                           │  │
│  │ • HealthService API on localhost:8080                     │  │
│  │ • MonitoringService API on 0.0.0.0:8081 (NEW)             │  │
│  │ • Manages file operations (copy, verify, requeue)         │  │
│  │                                                            │  │
│  │ API Endpoints (port 8081):                                │  │
│  │   GET  /api/monitoring/health                             │  │
│  │   GET  /api/monitoring/stats                              │  │
│  │   GET  /api/monitoring/jobs?state={state}                 │  │
│  │   GET  /api/monitoring/jobs/{id}                          │  │
│  │   POST /api/monitoring/requeue                            │  │
│  │                                                            │  │
│  │ ✅ Production-critical path                               │  │
│  │ ✅ Minimal dependencies                                   │  │
│  └───────────────────────────────────────────────────────────┘  │
│                          ▲ HTTP API (8081)                       │
│                          │                                        │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ Docker Desktop                                            │  │
│  │                                                            │  │
│  │  ┌─────────────────────────────────────────────────────┐ │  │
│  │  │ ForkerDotNet.Console (Docker Container)             │ │  │
│  │  │                                                      │ │  │
│  │  │ • Go web server (port 5000)                         │ │  │
│  │  │ • htmx-powered UI                                   │ │  │
│  │  │ • HTTP client for ForkerDotNet API                  │ │  │
│  │  │ • Filesystem scanner for folder views               │ │  │
│  │  │                                                      │ │  │
│  │  │ Data Access:                                        │ │  │
│  │  │ • HTTP: host.docker.internal:8081 (DB queries)      │ │  │
│  │  │ • Filesystem: /data/* (read-only folders)           │ │  │
│  │  │                                                      │ │  │
│  │  │ Mounted Volumes:                                    │ │  │
│  │  │ • C:\ForkerDemo:/data:ro (read-only)                │ │  │
│  │  │                                                      │ │  │
│  │  │ Network Config:                                     │ │  │
│  │  │ • extra_hosts: host.docker.internal:host-gateway    │ │  │
│  │  │   (Works on Windows Docker Desktop + WSL Docker)    │ │  │
│  │  │                                                      │ │  │
│  │  │ ✅ Monitoring and demo tool                         │ │  │
│  │  │ ✅ Non-critical (service works without it)          │ │  │
│  │  │ ✅ Isolated in container (zero-CVE requirement)     │ │  │
│  │  │ ✅ No direct database access (prevents WAL issues)  │ │  │
│  │  └─────────────────────────────────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                   │
│  User accesses: http://localhost:5000 in Edge/Chrome            │
└──────────────────────────────────────────────────────────────────┘
```

### Separation of Concerns

**Critical Production Path:**
- ForkerDotNet.Service runs independently
- No dependencies on console
- Console failure ≠ Service failure

**Monitoring Layer:**
- Console queries ForkerDotNet HTTP API (database access)
- Console reads filesystem directly (folder views)
- Console can stop/restart without affecting service
- Updates to console don't require service changes
- No SQLite WAL locking issues (HTTP API abstracts database)

---

## Design Decisions & Rationale

### Decision 1: Separate Console from Service

**Decision:** Build console as standalone application, not integrated into service

**Rationale:**
- ✅ **Zero production risk:** Console bugs/crashes don't affect file processing
- ✅ **Independent deployment:** Update console without touching production service
- ✅ **Technology freedom:** Can use modern web frameworks without affecting service reliability
- ✅ **Optional component:** Service works perfectly without console

**Alternative Considered:** Integrate monitoring UI into ForkerDotNet.Service
- ❌ **Rejected:** Would add dependencies to production service
- ❌ **Rejected:** Service restart required for console updates
- ❌ **Rejected:** Increased attack surface on critical path

---

### Decision 2: Docker Deployment (Not Native .exe)

**Decision:** Deploy console as Docker container, not native Windows executable

**Rationale:**
- ✅ **NHS Deployment:** Docker is acceptable in NHS environments (native .exe requires formal approval)
- ✅ **Isolation:** Container sandboxing provides security boundary
- ✅ **Vulnerability Scanning:** Standard Docker scanning tools (Trivy, Snyk)
- ✅ **Reproducible Builds:** Same container works everywhere
- ✅ **Easy Updates:** `docker pull` new version, restart container

**Alternative Considered:** Native Windows .exe (Avalonia/WPF)
- ❌ **Rejected:** Requires NHS software approval process (12-24 weeks)
- ❌ **Rejected:** Code signing certificate required
- ❌ **Rejected:** Distribution challenges (how to update 50 workstations?)
- ⚠️ **Acceptable IF:** NHS requires desktop app specifically

---

### Decision 3: Go + htmx (Not Blazor, Not React)

**Decision:** Use Go backend with htmx frontend

**Rationale:**

**Why Go:**
- ✅ **Minimal Dependencies:** Go standard library is comprehensive (no external packages needed)
- ✅ **Single Binary:** Compiles to self-contained executable (easy to containerize)
- ✅ **Fast:** ~10ms startup, low memory (~10MB)
- ✅ **Industry Proven:** Docker/Kubernetes/Consul/Terraform all written in Go
- ✅ **Security:** Small attack surface, easy to audit
- ✅ **NHS Use:** Already used in NHS Digital tools

**Why htmx:**
- ✅ **No Build Pipeline:** Just HTML + 14KB JavaScript file
- ✅ **No npm/webpack:** Zero JavaScript dependency management
- ✅ **Server-Rendered:** HTML generated on server (like Blazor Server)
- ✅ **Real-Time Capable:** Server-Sent Events for live updates
- ✅ **Simple:** HTML-first approach, progressive enhancement

**Container Size Comparison:**
- Go + htmx: **~15MB**
- ASP.NET Blazor: **~200MB**
- React/Node.js: **~150MB**

**Dependency Comparison:**
- Go + htmx: **2-3 packages** (SQLite driver, HTTP router)
- ASP.NET Blazor: **20+ packages** (.NET runtime, Blazor libs)
- React/Node.js: **1000+ packages** (npm transitive dependencies)

---

### Decision 4: Web-Based UI (Not Desktop GUI)

**Decision:** Browser-based interface accessed via localhost:5000

**Rationale:**
- ✅ **No Client Installation:** Just open browser (Edge/Chrome already installed)
- ✅ **Cross-Platform:** Works on Windows/Linux/Mac (same container)
- ✅ **Easy Updates:** Refresh browser page (no .exe redistribution)
- ✅ **Remote Access:** Can access from other machines if needed (Docker port mapping)
- ✅ **Modern UX:** Can use modern CSS frameworks (Tailwind, etc.)

**Alternative Considered:** Native Desktop GUI (WPF/Avalonia)
- ❌ **Rejected:** Awkward to run in Docker (requires X11 forwarding)
- ❌ **Rejected:** Requires .exe distribution
- ❌ **Rejected:** One user at a time (web can support multiple concurrent users)

---

### Decision 5: SQLite Retained (Not SQL Server/PostgreSQL)

**Decision:** Keep SQLite as database for ForkerDotNet.Service

**Rationale:**
- ✅ **Perfect for Use Case:** Single-server, sequential job processing
- ✅ **Mission-Critical Proven:** Used in aircraft (Airbus), medical devices (FDA-approved), military
- ✅ **Crash-Safe:** WAL mode provides ACID transactions
- ✅ **Operational Simplicity:** No database server to manage
- ✅ **NHS Deployment:** No additional infrastructure required

**When to Reconsider:**
- ⚠️ Multi-site replication needed (100+ concurrent writers)
- ⚠️ NHS policy mandates SQL Server
- ⚠️ Central dashboard across 10+ hospitals

**Current Status:** SQLite is correct choice, revisit if requirements change

---

### Decision 6: API-First Data Access (Not Direct SQLite)

**Decision:** Console uses HTTP API for database queries, direct filesystem for folder views

**Rationale:**
- ✅ **No WAL Issues:** HTTP API eliminates cross-platform SQLite WAL locking problems
- ✅ **Separation of Concerns:** Database is ForkerDotNet's internal implementation detail
- ✅ **Flexible:** Can change database schema without breaking console
- ✅ **Auditable:** All database operations go through service (single source of truth)
- ✅ **Simple Folder Views:** Direct filesystem reads for file listings (no DB queries needed)

**Docker Configuration:**
```yaml
volumes:
  - C:\ForkerDemo:/data:ro  # Read-only filesystem access

environment:
  - FORKER_API_URL=http://host.docker.internal:8081

extra_hosts:
  - "host.docker.internal:host-gateway"  # Works on Windows + WSL
```

**Data Access Pattern:**
- **Database queries** → HTTP GET `http://host.docker.internal:8081/api/monitoring/*`
- **Folder views** → Direct filesystem read of `/data/Input`, `/data/DestinationA`, etc.
- **File operations** → HTTP POST `http://host.docker.internal:8081/api/monitoring/requeue`

**Why This Approach:**
- **Initial attempt:** Direct SQLite access with `immutable=1` - caused stale data (snapshot)
- **Second attempt:** SQLite with `mode=ro` - WAL locking errors across Windows/Linux boundary
- **Final solution:** HTTP API for DB + filesystem for folders - works reliably on both platforms

---

## Arguments Against Alternative Options

### Alternative 1: ASP.NET Blazor Server

**Arguments For:**
- ✅ Microsoft framework (official support)
- ✅ C# on frontend and backend (language consistency with service)
- ✅ SignalR for real-time updates

**Arguments Against (Why Rejected):**
- ❌ **Container Size:** 200MB vs 15MB (Go)
- ❌ **Dependencies:** 20+ NuGet packages vs 2-3 Go packages
- ❌ **Memory Usage:** 50-100MB baseline vs 10MB (Go)
- ❌ **Startup Time:** 100-200ms vs 10ms (Go)
- ❌ **Vulnerability Surface:** Larger .NET runtime attack surface
- ❌ **Not Leveraging Strength:** Using .NET for web UI doesn't leverage its strengths (better for desktop or enterprise APIs)

**When Blazor Would Be Better:**
- Team has zero Go experience and strong C# expertise
- NHS mandates Microsoft-only frameworks (unlikely for containerized apps)
- Need deep integration with .NET ecosystem

**Verdict:** Go + htmx is objectively better for this specific use case

---

### Alternative 2: React/Vue + Node.js Backend

**Arguments For:**
- ✅ Industry standard for web UIs
- ✅ Rich component ecosystem
- ✅ Modern, interactive UIs

**Arguments Against (Why Rejected):**
- ❌ **Dependency Hell:** 1000+ npm packages (React app typical)
- ❌ **Vulnerability Management:** Constant CVEs in npm ecosystem
- ❌ **Build Complexity:** webpack/vite/babel/transpilation
- ❌ **Maintenance Burden:** npm audit never clears, always breaking changes
- ❌ **Attack Surface:** JavaScript supply chain attacks common
- ❌ **Not NHS-Friendly:** Difficult to scan/patch/harden

**Example of npm Nightmare:**
```bash
npm audit
# Found 47 vulnerabilities (12 high, 3 critical)

npm audit fix
# Can't fix 23 dependencies without breaking changes

npm update
# Now 64 vulnerabilities (added new ones during update!)
```

**Verdict:** Completely unsuitable for NHS security requirements

---

### Alternative 3: Native Desktop GUI (Avalonia/WPF)

**Arguments For:**
- ✅ Native Windows application
- ✅ Responsive, desktop-feel UI
- ✅ Can work offline (no browser needed)

**Arguments Against (Why Rejected):**
- ❌ **NHS Approval:** Requires 12-24 week software approval process
- ❌ **Code Signing:** Need NHS-approved certificate
- ❌ **Distribution:** Must deploy .exe to every workstation
- ❌ **Updates:** Redistribution required for every update
- ❌ **Docker Awkward:** Requires X11 forwarding (defeats purpose of Docker)
- ❌ **Multi-User:** One user at a time (web supports concurrent users)

**When Desktop GUI Would Be Better:**
- NHS specifically requests desktop application
- No Docker infrastructure available
- Offline operation critical

**Verdict:** Web-based is superior for this deployment model

---

### Alternative 4: PowerShell Scripts Only (No GUI)

**Arguments For:**
- ✅ Already working (5 scenario scripts exist)
- ✅ Zero additional development
- ✅ PowerShell pre-approved in NHS

**Arguments Against (Why Rejected for Production):**
- ❌ **Not Production-Ready:** Scripts are demo tools, not operational monitors
- ❌ **Fragmented Experience:** Multiple terminal windows, manual correlation
- ❌ **Not Reviewer-Friendly:** Hard to demonstrate to stakeholders
- ❌ **Limited Real-Time:** Polling-based, not push-based updates
- ❌ **No Historical View:** Can't see job history easily

**Verdict:** Keep PowerShell scripts for development, add GUI for production/demos

---

### Alternative 5: Integrate UI into Service (Extend HealthService)

**Arguments For:**
- ✅ Single deployment unit
- ✅ No Docker needed
- ✅ Direct access to service internals

**Arguments Against (Why Rejected):**
- ❌ **Production Risk:** UI bugs crash service
- ❌ **Deployment Coupling:** Can't update UI without service restart
- ❌ **Attack Surface:** Web framework dependencies in production service
- ❌ **Technology Lock-In:** Must use C#/.NET for UI (can't use Go/htmx)

**Architectural Principle Violated:** *"Separate critical path from observability layer"*

**Verdict:** Separation of concerns is paramount for production reliability

---

## Security & NHS Deployment

### Container Security

**Base Image:**
```dockerfile
FROM alpine:3.19
```

**Why Alpine:**
- ✅ Minimal: ~5MB base image
- ✅ Security-focused: Designed for containers
- ✅ Regular updates: Active CVE patching
- ✅ Package manager: apk for adding only what's needed

**Security Hardening:**
```dockerfile
# Non-root user
RUN adduser -D -u 1000 forker
USER forker

# Read-only root filesystem
RUN chmod -R 755 /app

# Drop capabilities
SECURITY_OPT: no-new-privileges:true

# Resource limits
--memory=256m --cpus=0.5
```

---

### Vulnerability Scanning Pipeline

**1. Go Dependency Scanning:**
```bash
# Nancy (Sonatype scanner for Go)
go list -json -m all | nancy sleuth

# Go vulnerability database
govulncheck ./...

# SBOM generation
syft . -o json > sbom.json
```

**2. Container Image Scanning:**
```bash
# Trivy (open source, comprehensive)
trivy image forker-console:1.0 \
  --severity HIGH,CRITICAL \
  --exit-code 1  # Fail build if vulnerabilities found

# Snyk (commercial, deep analysis)
snyk container test forker-console:1.0 \
  --severity-threshold=high

# Anchore (policy-based scanning)
anchore-cli image add forker-console:1.0
anchore-cli image vuln forker-console:1.0 all
```

**3. Runtime Scanning:**
```bash
# Docker Desktop built-in scanning
docker scan forker-console:1.0

# Microsoft Defender for Containers (if available)
az security sub-assessment list
```

---

### NHS Deployment Model

**Deployment Artifact:**
- Docker image: `forker-console:1.0.tar`
- Compressed size: ~20MB
- No external registry required (offline deployment)

**Deployment Steps:**
```bash
# 1. Load image (no internet needed)
docker load -i forker-console-1.0.tar

# 2. Run container
docker run -d \
  --name forker-console \
  -p 127.0.0.1:5000:5000 \
  -v C:\ForkerDemo\forker.db:/app/data/forker.db:ro \
  --memory=256m \
  --cpus=0.5 \
  --restart=unless-stopped \
  forker-console:1.0

# 3. Verify health
curl http://localhost:5000/health

# 4. Access in browser
start http://localhost:5000
```

**Update Process:**
```bash
# 1. Stop old container
docker stop forker-console

# 2. Load new image
docker load -i forker-console-1.1.tar

# 3. Remove old container
docker rm forker-console

# 4. Run new version
docker run -d ... forker-console:1.1
```

**Zero Downtime Update:**
```bash
# Blue-green deployment
docker run -d --name forker-console-new -p 127.0.0.1:5001:5000 ...
# Test on :5001
# Switch port mapping, remove old container
```

---

### Network Security

**Principle:** Console exposed only on localhost by default

**Docker Compose (Development):**
```yaml
version: '3.8'

services:
  forker-console:
    build: ./src/Forker.Console.Web
    ports:
      - "127.0.0.1:5000:5000"  # Localhost only
    volumes:
      - C:/ForkerDemo/forker.db:/app/data/forker.db:ro
    environment:
      - ENVIRONMENT=Demo
      - LOG_LEVEL=info
    networks:
      - forker-internal
    security_opt:
      - no-new-privileges:true
    cap_drop:
      - ALL
    cap_add:
      - NET_BIND_SERVICE
    read_only: true
    tmpfs:
      - /tmp

networks:
  forker-internal:
    driver: bridge
```

**Remote Access (Optional):**
If NHS needs to access console remotely:
```yaml
ports:
  - "0.0.0.0:5000:5000"  # Allow external access

# Add authentication layer (basic auth or OAuth)
environment:
  - AUTH_ENABLED=true
  - AUTH_USERNAME=admin
  - AUTH_PASSWORD_HASH=bcrypt_hash_here
```

---

## Implementation Approach

### Phase 1: Core Infrastructure (4-6 hours) ✅ COMPLETE

**Deliverables:**
- ✅ Go web server with stdlib HTTP routing (zero third-party HTTP CVEs)
- ✅ Docker multi-stage build (16.5MB final size)
- ✅ Health endpoint
- ✅ Static file serving (htmx, CSS)
- ✅ Template system (8 HTML templates)
- ✅ Dual-platform Docker support (Linux + Windows containers)
- ⚠️ SQLite integration attempted (WAL locking issues discovered)

**Success Criteria:**
- ✅ Container builds successfully
- ✅ Health check passes
- ✅ Container size: 16.5MB (< 20MB target)
- ✅ Docker Scout: 0C 0H 0M 0L (zero vulnerabilities)
- ⚠️ SQLite direct access abandoned (cross-platform WAL incompatibility)

---

### Phase 2: Production Monitoring Dashboard (6-8 hours)

**UI Layout (Based on Mockup1.jpg):**

**Top Panel:**
- **Service Health Info:** PID, uptime, memory usage, database path, last activity

**Main Dashboard - Two View Modes:**

**A) Folder View (Explorer-Style):**
- 4 scrollable panes showing file listings:
  1. **Input** - Files waiting/being copied (sorted by descending age)
  2. **Dest A** - Verified copies at destination A
  3. **Dest B** - Verified copies at destination B
  4. **Failed** - Permanent copy failures after retries
- Each pane shows: filename, size, age/modified time
- Pane title shows count (e.g., "Input (5 files)")
- Updates every 2 seconds (configurable)

**B) Transaction View (State-Based):**
- 2 scrollable panes showing job states:
  1. **In Progress** - Discovered/Queued/InProgress/Partial states
  2. **Completed** - Verified/Failed/Quarantined terminal states
- Each row shows: filename, current state, timestamp
- Color-coded badges for states

**Stats Bar (Always Visible):**
- Total jobs, Active, Verified, Failed, Quarantined counts
- Throughput (MB/s) if available

**Deliverables:**
- Folder view with 4 explorer-style panes (direct filesystem reads)
- Transaction view with 2 state panes (HTTP API data)
- Service health panel (HTTP API data)
- Stats bar with live updates (htmx polling)
- Re-queue button for failed files (HTTP POST to API)
- View toggle button (Folder View ↔ Transaction View)

**Success Criteria:**
- Dashboard updates every 2 seconds automatically
- Folder views show live file listings (newest first)
- Transaction view shows correct job states
- Re-queue operation moves files from Failed → Input
- Works on both Windows Docker Desktop and WSL Docker
- No SQLite WAL locking errors

---

### Phase 3: Demo Mode (6-8 hours)

**Deliverables:**
- Demo scenario buttons (5 scenarios)
- Scenario orchestration (calls PowerShell scripts OR pure Go implementation)
- Live scenario progress tracking
- System state monitoring (database/filesystem/service metrics)
- Evidence export (copy summary to clipboard, download audit log)

**Success Criteria:**
- Can run Scenario 1 with one click
- See real-time progress as scenario executes
- Scenario completes and shows success/failure
- Can export evidence as Markdown

---

### Phase 4: Polish & Security (3-4 hours)

**Deliverables:**
- Professional CSS styling (Tailwind or custom)
- Error handling and user feedback
- Container security hardening
- Vulnerability scanning integration
- Documentation (README, deployment guide)

**Success Criteria:**
- Professional, polished UI
- No console errors in browser
- Trivy scan shows zero HIGH/CRITICAL vulnerabilities
- Clear deployment instructions

---

**Total Implementation Effort:** 19-26 hours

---

## Technology Stack Summary

| Layer | Technology | Version | Justification |
|-------|------------|---------|---------------|
| **Backend** | Go | 1.21+ | Minimal dependencies, fast, secure |
| **HTTP Router** | Go stdlib or gorilla/mux | 1.8+ | Simple, proven, no external deps needed |
| **Database** | SQLite (read-only) | 3.x | Existing ForkerDotNet database |
| **Frontend** | htmx | 1.9+ | Interactive HTML without build pipeline |
| **Styling** | Tailwind CSS or custom | 3.x | Modern, professional appearance |
| **Interactivity** | Alpine.js (optional) | 3.x | Client-side UI state (15KB) |
| **Real-Time** | Server-Sent Events | Native | Live dashboard updates |
| **Container** | Alpine Linux | 3.19 | Minimal, secure base image |
| **Deployment** | Docker | 20.10+ | NHS-acceptable container platform |

---

## Conclusion

The **Go + htmx** stack provides the optimal balance of:

✅ **Security:** Minimal dependencies, easy vulnerability scanning
✅ **Simplicity:** No build pipeline, straightforward codebase
✅ **Performance:** 15MB container, 10ms startup, 10MB RAM
✅ **Maintainability:** 2-3 dependencies to track vs 1000+ (React)
✅ **NHS Suitability:** Docker deployment, proven in healthcare
✅ **Production Safety:** Isolated from ForkerDotNet.Service

This design prioritizes **long-term operational success** over short-term development convenience, ensuring the console can be maintained and secured throughout its lifecycle in NHS production environments.

---

**Document Status:** ✅ Approved for Implementation
**Next Step:** See `console-dev-task-list.md` for implementation plan
