# Quick Testing Guide - ForkerDotNet Console

## Prerequisites

✅ Docker Desktop installed and running
✅ ForkerDotNet.Service has created `C:\ForkerDemo\forker.db`

## 1. Build & Run (2 minutes)

```powershell
# Navigate to console directory
cd C:\Dev\win_repos\forkerDotNet\src\Forker.Console

# Build and run in one command
.\build.ps1 -Docker -Run
```

**Expected Output:**
```
Building Docker image...
✓ Docker image built successfully

REPOSITORY         TAG      SIZE
forker-console    latest   ~15MB

Starting console with Docker Compose...
✓ Console started

Access console at: http://localhost:5000
Health check:      http://localhost:5000/health
```

## 2. Verify Health (30 seconds)

```powershell
# Test health endpoint
Invoke-RestMethod http://localhost:5000/health
```

**Expected Response:**
```json
{
  "status": "healthy",
  "service": "forker-console"
}
```

## 3. Open Dashboard (30 seconds)

```powershell
# Open in browser
Start-Process http://localhost:5000
```

**Expected:**
- Browser opens to console
- "ForkerDotNet Console" header visible
- Navigation menu (Dashboard | Demo Mode)
- Loading jobs message or job table

## 4. Check Logs (30 seconds)

```powershell
# View console logs
docker-compose logs
```

**Expected:**
```
[INFO] Starting ForkerDotNet Console
[INFO] Database: /data/forker.db
[INFO] Database connection established (read-only mode)
[INFO] Console listening on http://localhost:5000
[INFO] Health endpoint: http://localhost:5000/health
[INFO] GET /health 2ms
[INFO] GET / 5ms
```

## 5. Security Scan (2 minutes)

```powershell
# Run Docker Scout scan
docker scout cves forker-console:latest --only-severity critical,high,medium
```

**Expected:**
```
✅ 0 CRITICAL
✅ 0 HIGH
✅ 0 MEDIUM

VERDICT: APPROVED
```

## 6. Stop Console

```powershell
.\build.ps1 -Stop
```

---

## Troubleshooting

### Issue: "Database not found"

**Cause:** ForkerDemo database doesn't exist yet

**Fix:**
```powershell
# Check database exists
Test-Path C:\ForkerDemo\forker.db

# If false, run ForkerDotNet.Service first to create database
cd C:\ForkerDemo
dotnet run --project ..\path\to\Forker.Service
```

### Issue: "Port 5000 already in use"

**Cause:** Another application using port 5000

**Fix:** Edit `docker-compose.yml`:
```yaml
ports:
  - "5001:5000"  # Use 5001 instead
```

Then access at `http://localhost:5001`

### Issue: "Docker not found"

**Cause:** Docker Desktop not running

**Fix:**
1. Start Docker Desktop
2. Wait for "Docker Desktop is running" notification
3. Retry build command

### Issue: "Permission denied" on database

**Cause:** Database file has restrictive permissions

**Fix:**
```powershell
# Grant read permissions
icacls C:\ForkerDemo\forker.db /grant "Users:(R)"
```

---

## Quick Commands Reference

```powershell
# Build only
.\build.ps1 -Docker

# Run only (after build)
.\build.ps1 -Run

# Build and run
.\build.ps1 -Docker -Run

# Stop
.\build.ps1 -Stop

# View logs
docker-compose logs -f

# Check image size
docker images forker-console:latest

# Access console
Start-Process http://localhost:5000

# Test health
Invoke-RestMethod http://localhost:5000/health

# Security scan
docker scout cves forker-console:latest
```

---

## Next Steps After Testing

If all tests pass:

1. ✅ Verify Docker Scout shows 0 MEDIUM+ vulnerabilities
2. ✅ Confirm dashboard loads successfully
3. ✅ Check console can query database (if jobs exist)
4. → **Proceed to Phase 2: Production Dashboard**

See [console-dev-task-list.md](../../console-dev-task-list.md) for Phase 2 tasks.
