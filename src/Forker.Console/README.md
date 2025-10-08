# ForkerDotNet Console

Browser-based monitoring and demonstration console for ForkerDotNet medical imaging file replication service.

## Features

- **Production Monitoring**: Real-time dashboard showing file copy job status
- **Demo Mode**: Automated scenario testing for stakeholder demonstrations
- **Read-Only Access**: Zero-interference monitoring via read-only SQLite access
- **NHS-Grade Security**: Zero third-party HTTP dependencies, pure Go SQLite driver

## Technology Stack

- **Backend**: Go 1.23+ with stdlib HTTP routing
- **Database**: modernc.org/sqlite (pure Go, no CGO)
- **Frontend**: htmx for interactive UI
- **Deployment**: Docker container (~15MB)

## Quick Start

### Prerequisites

- Docker Desktop installed and running
- ForkerDotNet.Service running with database at `C:\ForkerDemo\forker.db`

### Run with Docker Compose

```bash
cd src/Forker.Console
docker-compose up -d
```

Access the console at: **http://localhost:5000**

Check health: **http://localhost:5000/health**

### Run Locally (Development)

```bash
cd src/Forker.Console
go mod download
go run cmd/console/main.go
```

## Architecture

```
┌────────────────────────────────────────┐
│ Browser (Edge/Chrome)                  │
│ http://localhost:5000                  │
└───────────────┬────────────────────────┘
                │ HTTPS
┌───────────────▼────────────────────────┐
│ ForkerDotNet Console (Docker)          │
│ - Go HTTP Server                       │
│ - htmx UI                              │
│ - Read-only SQLite access              │
└───────────────┬────────────────────────┘
                │ Read-only mount
┌───────────────▼────────────────────────┐
│ C:\ForkerDemo\forker.db                │
│ (SQLite database, WAL mode)            │
└────────────────────────────────────────┘
```

## Security

### Zero-Dependency Architecture

- **No third-party HTTP routers** (stdlib only)
- **No CGO dependencies** (pure Go SQLite)
- **No npm packages** (htmx served via CDN)

### Docker Security Features

- Runs from `scratch` base image (no OS vulnerabilities)
- Read-only root filesystem
- Non-root user (UID 1000)
- All capabilities dropped
- Resource limits enforced

### Vulnerability Scanning

```bash
# Scan Docker image
docker scout cves forker-console:latest

# Expected result: Zero MEDIUM+ vulnerabilities
```

## Development

### Build Binary

```bash
go build -o console.exe ./cmd/console
```

### Run Tests

```bash
go test ./...
```

### Build Docker Image

```bash
docker build -t forker-console:latest .
```

### Check Image Size

```bash
docker images forker-console:latest
# Expected: ~15MB
```

## Environment Variables

- `FORKER_DB_PATH`: Path to SQLite database (default: `C:\ForkerDemo\forker.db`)
- `FORKER_MODE`: Operation mode (`demo` or `production`)

## API Endpoints

- `GET /` - Dashboard homepage
- `GET /health` - Health check (JSON)
- `GET /api/jobs` - List recent jobs (JSON)
- `GET /api/jobs/{id}` - Job details (JSON)
- `GET /demo` - Demo mode page

## Project Structure

```
src/Forker.Console/
├── cmd/console/main.go          # Application entry point
├── internal/
│   ├── server/
│   │   ├── router.go            # HTTP routing (stdlib)
│   │   ├── middleware.go        # Custom middleware
│   │   └── context.go           # Database context
│   └── database/
│       ├── sqlite.go            # SQLite connection (read-only)
│       └── models.go            # Data models
├── web/
│   ├── templates/               # HTML templates
│   └── static/
│       └── style.css            # CSS styling
├── Dockerfile                   # Multi-stage Docker build
├── docker-compose.yml           # Docker Compose config
└── go.mod                       # Go dependencies (1 only!)
```

## Dependencies

**Go Modules (Total: 1 direct dependency)**
- `modernc.org/sqlite` - Pure Go SQLite implementation

**No External Dependencies For:**
- HTTP routing (stdlib `net/http`)
- JSON encoding (stdlib `encoding/json`)
- Logging (stdlib `log`)
- Middleware (custom implementation)

## Deployment

### For Local Demo

```bash
docker-compose up -d
```

### For NHS Production

1. Build image: `docker build -t forker-console:latest .`
2. Save image: `docker save -o forker-console-latest.tar forker-console:latest`
3. Scan image: `docker scout cves forker-console:latest`
4. Transfer tar file to production server
5. Load image: `docker load -i forker-console-latest.tar`
6. Run container with production paths

## Troubleshooting

### Console can't connect to database

```bash
# Check database exists
ls C:\ForkerDemo\forker.db

# Check ForkerDotNet.Service is running
curl http://localhost:8080/health/live
```

### Permission denied error

```bash
# Ensure database has read permissions
icacls C:\ForkerDemo\forker.db /grant "Users:(R)"
```

### Port 5000 already in use

```bash
# Change port in docker-compose.yml
ports:
  - "5001:5000"  # Use 5001 instead
```

## License

Proprietary - ForkerDotNet Project

## Contact

For issues or questions, see main ForkerDotNet repository documentation.
