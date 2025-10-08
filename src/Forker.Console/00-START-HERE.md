# ForkerDotNet Console - Start Here 🚀

**Welcome!** This document will get you up and running quickly.

---

## Quick Start (Pick Your Path)

### 👨‍💻 I'm Developing Locally (Docker Desktop + WSL)

```powershell
cd C:\Dev\win_repos\forkerDotNet\src\Forker.Console

# Build and run
.\build.ps1 -Docker -Run

# Open browser
Start-Process http://localhost:5000

# Stop when done
.\build.ps1 -Stop
```

**Done!** See [TESTING.md](TESTING.md) for more.

---

### 🏥 I'm Deploying to NHS Servers (Docker without WSL)

```powershell
cd C:\Dev\win_repos\forkerDotNet\src\Forker.Console

# Switch Docker Desktop to Windows containers
# (Right-click tray icon → "Switch to Windows containers")

# Build and package for NHS
.\build-windows.ps1 -Package

# Transfer the generated .zip file to NHS server
# See README-DEPLOYMENT.md for detailed steps
```

**Next:** See [README-DEPLOYMENT.md](README-DEPLOYMENT.md) for deployment instructions.

---

## Documentation Index

### Getting Started
- **[00-START-HERE.md](00-START-HERE.md)** ← You are here
- **[README.md](README.md)** - Project overview and features
- **[TESTING.md](TESTING.md)** - Quick testing guide (2 minutes)

### Deployment Guides
- **[README-DEPLOYMENT.md](README-DEPLOYMENT.md)** - Quick deployment reference
- **[DEPLOYMENT-DOCKER.md](DEPLOYMENT-DOCKER.md)** - Docker-specific deployment (comprehensive)
- **[DEPLOYMENT.md](DEPLOYMENT.md)** - Alternative native Windows deployment (if no Docker)

### Security & Validation
- **[SECURITY.md](SECURITY.md)** - Security analysis and NHS compliance
- **[VALIDATION.md](VALIDATION.md)** - 21-point validation checklist

### Root Directory (Important Context)
- **[console-deployment-solution.md](../../console-deployment-solution.md)** - Dual-platform deployment solution
- **[console-phase1-complete.md](../../console-phase1-complete.md)** - Phase 1 implementation summary
- **[console-dev-task-list.md](../../console-dev-task-list.md)** - Full development roadmap
- **[console-design.md](../../console-design.md)** - Design decisions and rationale

---

## What Is This?

**ForkerDotNet Console** is a web-based monitoring dashboard for the ForkerDotNet medical imaging file replication service.

**Key Features:**
- ✅ Real-time job monitoring
- ✅ Production dashboard
- ✅ Demo mode for stakeholder presentations
- ✅ Read-only database access (zero interference with service)
- ✅ NHS-grade security (zero third-party CVE exposure)

**Technology:**
- Go 1.23 with stdlib HTTP routing (no chi, no CVE exposure)
- Pure Go SQLite driver (no CGO, no C library vulnerabilities)
- htmx for interactive UI
- Docker containers (~15MB Linux, ~300MB Windows)

---

## Platform Support

| Environment | Docker Backend | Container Type | Build Script |
|-------------|---------------|----------------|--------------|
| **Your Dev Machine** | WSL2 | Linux | `build.ps1` |
| **NHS Servers** | Native Windows | Windows | `build-windows.ps1` |

**Same Go code runs on both!**

---

## Common Questions

### Q: Why two Dockerfiles?

**A:** Your dev machine uses Docker with WSL2 (Linux containers), NHS servers use Docker without WSL (Windows containers). We support both with the same source code.

### Q: Which build script do I use?

**A:**
- **Development:** `build.ps1` (Linux containers)
- **NHS Deployment:** `build-windows.ps1` (Windows containers)

The scripts will auto-detect and warn if you're in the wrong Docker mode.

### Q: How do I switch Docker modes?

**A:** Right-click Docker Desktop tray icon → "Switch to Linux/Windows containers"

### Q: Do I need to install Go?

**A:** No! Docker handles the Go build environment. You just need Docker and PowerShell 7.5+.

### Q: Is this ready for production?

**A:** Phase 1 (core infrastructure) is complete. Phases 2-4 add dashboard features, demo mode, and polish. Current status: **Functional but minimal UI**.

---

## File Structure

```
src/Forker.Console/
├── 00-START-HERE.md            ← You are here
├── README.md                   ← Project overview
├── README-DEPLOYMENT.md        ← Deployment quick reference
│
├── cmd/console/main.go         ← Entry point
├── internal/                   ← Source code
│   ├── server/                 ← HTTP server (stdlib)
│   └── database/               ← SQLite (read-only)
├── web/                        ← Frontend assets
│   └── static/style.css
│
├── Dockerfile                  ← Linux container
├── Dockerfile.windows          ← Windows container
├── build.ps1                   ← Linux build script
├── build-windows.ps1           ← Windows build script
│
└── [Documentation...]
```

---

## Next Steps

### For Development

1. **Test locally:** `.\build.ps1 -Docker -Run`
2. **Verify health:** `http://localhost:5000/health`
3. **Open dashboard:** `http://localhost:5000`
4. **Read:** [TESTING.md](TESTING.md) for detailed tests

### For NHS Deployment

1. **Read:** [README-DEPLOYMENT.md](README-DEPLOYMENT.md)
2. **Build:** `.\build-windows.ps1 -Package`
3. **Transfer:** Copy .zip to NHS server
4. **Deploy:** Follow deployment guide

### For Understanding the Project

1. **Read:** [console-design.md](../../console-design.md) - Why Go + htmx?
2. **Read:** [SECURITY.md](SECURITY.md) - Why zero dependencies?
3. **Read:** [console-phase1-complete.md](../../console-phase1-complete.md) - What we built

---

## Quick Commands

```powershell
# Development (Linux containers)
.\build.ps1                      # Show usage
.\build.ps1 -Docker              # Build Linux image
.\build.ps1 -Run                 # Start console
.\build.ps1 -Stop                # Stop console
.\build.ps1 -Docker -Run         # Build and run

# NHS Deployment (Windows containers)
.\build-windows.ps1              # Build Windows image
.\build-windows.ps1 -Package     # Create deployment package
.\build-windows.ps1 -Test        # Build and test locally

# View logs
docker-compose logs -f           # Linux
docker logs forker-console -f    # Windows

# Health check
Invoke-RestMethod http://localhost:5000/health

# Open dashboard
Start-Process http://localhost:5000
```

---

## Troubleshooting

**Build fails with "no matching manifest":**
- You're in wrong Docker mode
- Solution: Switch mode or use other build script

**"Access denied" on database:**
- Database permissions too restrictive
- Solution: `icacls C:\ForkerDemo\forker.db /grant "Everyone:(R)"`

**Port 5000 already in use:**
- Edit `docker-compose.yml`, change port to 5001

**More help:** See [DEPLOYMENT-DOCKER.md](DEPLOYMENT-DOCKER.md#troubleshooting)

---

## Support

**Questions?**
- See documentation index above
- Check [console-dev-task-list.md](../../console-dev-task-list.md) for roadmap
- Review [console-design.md](../../console-design.md) for design decisions

**Found a bug?**
- Check logs: `docker logs forker-console`
- See [VALIDATION.md](VALIDATION.md) for validation checklist

---

## Status

**Current Phase:** Phase 1 Complete ✅
- Core infrastructure built
- Dual-platform deployment working
- Health endpoint operational
- Basic dashboard functional

**Next Phase:** Phase 2 (Production Dashboard)
- Real-time job monitoring
- Server-Sent Events (SSE)
- Job detail views
- Statistics panel

---

**Happy coding! 🚀**

*Last Updated: 2025-10-08*
