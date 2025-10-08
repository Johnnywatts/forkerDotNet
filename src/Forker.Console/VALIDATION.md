# ForkerDotNet Console - Validation Checklist

## Pre-Build Validation

### ✅ Directory Structure
```
src/Forker.Console/
├── cmd/console/main.go          ✓ Created
├── internal/
│   ├── server/
│   │   ├── router.go            ✓ Created
│   │   ├── middleware.go        ✓ Created
│   │   └── context.go           ✓ Created
│   └── database/
│       ├── sqlite.go            ✓ Created
│       └── models.go            ✓ Created
├── web/
│   └── static/
│       └── style.css            ✓ Created
├── Dockerfile                   ✓ Created
├── docker-compose.yml           ✓ Created
├── go.mod                       ✓ Created
├── go.sum                       ✓ Created
├── README.md                    ✓ Created
├── SECURITY.md                  ✓ Created
└── build.ps1                    ✓ Created
```

### ✅ Dependency Verification

**Go Modules:**
```bash
cd src/Forker.Console
cat go.mod
```

Expected output:
```
module forkerDotNet/console
go 1.23
require modernc.org/sqlite v1.34.4
```

**Dependency Count: 1 (modernc.org/sqlite)**

### ✅ Security Architecture

- [ ] No `github.com/go-chi/chi` (GHSA-vrw8-fxc6-2r93 avoided) ✓
- [ ] No `github.com/mattn/go-sqlite3` (CVE-2025-6965 avoided) ✓
- [ ] Pure Go implementation only ✓
- [ ] Zero CGO dependencies ✓

## Build Validation

### Test 1: Docker Build

```bash
cd src/Forker.Console
docker build -t forker-console:latest .
```

**Expected:**
- Build succeeds without errors
- Image size: ~15-20MB
- No build warnings

**Verify:**
```bash
docker images forker-console:latest
# SIZE should be ~15MB
```

### Test 2: Container Startup

```bash
# Ensure ForkerDemo database exists
ls C:\ForkerDemo\forker.db

# Start console
docker-compose up -d

# Check logs
docker-compose logs
```

**Expected Logs:**
```
[INFO] Starting ForkerDotNet Console
[INFO] Database: /data/forker.db
[INFO] Database connection established (read-only mode)
[INFO] Console listening on http://localhost:5000
[INFO] Health endpoint: http://localhost:5000/health
```

### Test 3: Health Check

```powershell
Invoke-WebRequest http://localhost:5000/health
```

**Expected Response:**
```json
{
  "status": "healthy",
  "service": "forker-console"
}
```

**Status Code:** 200 OK

### Test 4: Dashboard Access

```powershell
Start-Process http://localhost:5000
```

**Expected:**
- Browser opens to console dashboard
- Page loads without errors
- CSS styling applied
- "ForkerDotNet Console" header visible

## Security Validation

### Test 5: Read-Only Database Enforcement

```bash
docker exec -it forker-console /console

# Inside container, attempt to open database in write mode
# Should fail because volume mounted with :ro flag
```

**Expected:** Cannot write to database (permission denied or read-only filesystem)

### Test 6: Docker Scout Scan

```bash
docker scout cves forker-console:latest --only-severity critical,high,medium
```

**Expected Output:**
```
✅ 0 CRITICAL vulnerabilities
✅ 0 HIGH vulnerabilities
✅ 0 MEDIUM vulnerabilities

Target: forker-console:latest

Package                        Version    Vulnerability    Severity
modernc.org/sqlite             v1.34.4    (none)           N/A
Go stdlib                      1.23.x     (check latest)   (may have LOW)

VERDICT: APPROVED FOR DEPLOYMENT
```

### Test 7: Vulnerability Scan (Alternative: Trivy)

```bash
trivy image forker-console:latest --severity HIGH,CRITICAL
```

**Expected:**
```
Total: 0 (HIGH: 0, CRITICAL: 0)
```

### Test 8: Non-Root User Verification

```bash
docker exec forker-console id
```

**Expected:**
```
uid=1000 gid=1000
```

**Not root (uid=0)**

### Test 9: Read-Only Filesystem

```bash
docker exec forker-console touch /test-file
```

**Expected:** `Read-only file system` error

### Test 10: No Capabilities

```bash
docker inspect forker-console | grep -A 10 CapDrop
```

**Expected:**
```json
"CapDrop": ["ALL"]
```

## Functional Validation

### Test 11: Database Query

**If ForkerDemo has jobs:**

```powershell
Invoke-RestMethod http://localhost:5000/api/jobs
```

**Expected:** JSON array of jobs

### Test 12: Static Assets

```powershell
Invoke-WebRequest http://localhost:5000/static/style.css
```

**Expected:** CSS file returned (200 OK)

### Test 13: Graceful Shutdown

```bash
docker-compose down
```

**Expected Logs:**
```
[INFO] Shutting down console...
[INFO] Console stopped
```

**Clean shutdown, no errors**

## Performance Validation

### Test 14: Memory Usage

```bash
docker stats forker-console --no-stream
```

**Expected:**
- MEM USAGE: < 50MB
- MEM LIMIT: 256MB
- MEM %: < 20%

### Test 15: CPU Usage

**Expected:**
- CPU %: < 1% (idle)
- CPU %: < 10% (under load with 50 concurrent requests)

### Test 16: Startup Time

```bash
time docker-compose up -d
```

**Expected:** < 5 seconds (after image built)

## Integration Validation

### Test 17: Concurrent Access with ForkerDotNet.Service

**Setup:**
1. Start ForkerDotNet.Service
2. Start Console
3. Service processes a file job
4. Console displays job in dashboard

**Expected:**
- No database lock errors
- Console updates within 5 seconds
- Service continues processing normally

### Test 18: Read-Only Mount Safety

**Scenario:** Service writes to database while console reads

**Expected:**
- Service write succeeds
- Console read succeeds
- No lock contention
- No corruption

### Test 19: Console Crash Does Not Affect Service

```bash
# Kill console abruptly
docker kill forker-console

# Check service health
curl http://localhost:8080/health/live
```

**Expected:** Service remains healthy (200 OK)

## Deployment Validation

### Test 20: Offline Deployment

```bash
# Save image
docker save -o forker-console.tar forker-console:latest

# Simulate transfer to air-gapped system
# (Copy tar file to another machine)

# Load image
docker load -i forker-console.tar

# Run without internet access
docker run -d -p 5000:5000 \
  -v C:\ForkerDemo\forker.db:/data/forker.db:ro \
  forker-console:latest
```

**Expected:** Runs successfully without internet

### Test 21: Update Scenario

```bash
# Build new version
docker build -t forker-console:v1.1 .

# Stop old version
docker stop forker-console

# Start new version
docker run -d --name forker-console \
  -p 5000:5000 \
  -v C:\ForkerDemo\forker.db:/data/forker.db:ro \
  forker-console:v1.1
```

**Expected:** < 10 seconds downtime, data persists

## Final Checklist

### Security ✅
- [ ] Zero MEDIUM+ CVEs in Docker Scout scan
- [ ] Read-only database mount enforced
- [ ] Non-root user (UID 1000)
- [ ] Read-only root filesystem
- [ ] All capabilities dropped
- [ ] No secrets in environment

### Functionality ✅
- [ ] Health endpoint returns 200 OK
- [ ] Dashboard loads successfully
- [ ] Database queries return results
- [ ] CSS/static assets load
- [ ] Graceful shutdown works

### Performance ✅
- [ ] Image size < 20MB
- [ ] Memory usage < 50MB
- [ ] CPU usage < 1% idle
- [ ] Startup time < 5 seconds

### NHS Compliance ✅
- [ ] Docker Scout approved
- [ ] SBOM generated
- [ ] Security documentation complete
- [ ] Read-only architecture verified
- [ ] No patient data modification possible

## Validation Sign-Off

**Validation Date:** _____________

**Validated By:** _____________

**Environment:**
- [ ] Development (Demo mode)
- [ ] Production (NHS deployment)

**Result:**
- [ ] ✅ PASS - Ready for deployment
- [ ] ❌ FAIL - Issues found (see below)

**Issues Found:**
```
(List any issues discovered during validation)
```

**Remediation:**
```
(Steps taken to fix issues)
```

**Final Approval:**
- [ ] Technical Lead: _____________
- [ ] Security Review: _____________
- [ ] NHS Deployment Authority: _____________

---

**Document Version:** 1.0
**Last Updated:** 2025-10-08
