package server

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"path/filepath"
	"strings"
	"time"

	"forkerDotNet/console/internal/apiclient"
)

// --- HTTP Handlers (API-based, Phase 3) ---

func handleHealthAPI(w http.ResponseWriter, r *http.Request) {
	client := GetAPIClient()
	if client == nil {
		// Fallback to basic health check if API client not configured
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusOK)
		json.NewEncoder(w).Encode(map[string]string{
			"status":  "healthy",
			"service": "forker-console",
			"mode":    "standalone",
		})
		return
	}

	// Forward health check to ForkerDotNet API
	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()

	health, err := client.Health(ctx)
	if err != nil {
		log.Printf("[ERROR] API health check failed: %v", err)
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusServiceUnavailable)
		json.NewEncoder(w).Encode(map[string]interface{}{
			"status": "unhealthy",
			"error":  err.Error(),
		})
		return
	}

	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusOK)
	json.NewEncoder(w).Encode(health)
}

func handleSystemInfoAPI(w http.ResponseWriter, r *http.Request) {
	log.Printf("[DEBUG] handleSystemInfoAPI called, HX-Request=%s", r.Header.Get("HX-Request"))

	client := GetAPIClient()
	if client == nil {
		http.Error(w, "API client not configured", http.StatusServiceUnavailable)
		return
	}

	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()

	health, err := client.Health(ctx)
	if err != nil {
		log.Printf("[ERROR] Failed to get system info: %v", err)
		http.Error(w, "Failed to retrieve system info", http.StatusInternalServerError)
		return
	}

	info := map[string]interface{}{
		"database_path": health.DatabasePath,
		"timestamp":     time.Now().Format("2006-01-02 15:04:05"),
		"service":       "forker-console",
		"version":       "1.0.0-api",
		"process_id":    health.ProcessID,
		"uptime":        health.Uptime,
		"memory_mb":     health.MemoryUsageMB,
	}

	// Check if htmx request (wants HTML fragment) or regular request (wants JSON)
	if r.Header.Get("HX-Request") == "true" {
		log.Printf("[DEBUG] Returning HTML fragment")
		w.Header().Set("Content-Type", "text/html")
		if err := templates.ExecuteTemplate(w, "system-info", info); err != nil {
			log.Printf("[ERROR] Template execution failed: %v", err)
			http.Error(w, "Internal Server Error", http.StatusInternalServerError)
			return
		}
		log.Printf("[DEBUG] Template executed successfully")
	} else {
		log.Printf("[DEBUG] Returning JSON")
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(info)
	}
}

func handleDashboardAPI(w http.ResponseWriter, r *http.Request) {
	data := map[string]interface{}{
		"Title": "Dashboard",
		"Page":  "dashboard",
	}

	w.Header().Set("Content-Type", "text/html")
	if err := templates.ExecuteTemplate(w, "base.html", data); err != nil {
		log.Printf("[ERROR] Template execution failed: %v", err)
		http.Error(w, "Internal Server Error", http.StatusInternalServerError)
	}
}

func handleFoldersPage(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "text/html")

	// Write the HTML directly since template composition is complex
	html := `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Folder Scanner - ForkerDotNet Console</title>
    <script src="https://unpkg.com/htmx.org@1.9.10"></script>
    <link rel="stylesheet" href="/static/style.css">
    <style>
        .folders-grid {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 20px;
            margin-top: 20px;
        }
        .folder-card {
            border: 1px solid #ddd;
            border-radius: 8px;
            padding: 15px;
            background: #f9f9f9;
        }
        .folder-card h3 {
            margin: 0 0 10px 0;
            color: #333;
            font-size: 1.2em;
        }
        .folder-stats {
            margin-bottom: 15px;
            padding: 10px;
            background: #e9e9e9;
            border-radius: 4px;
            font-size: 0.9em;
        }
        .file-list {
            max-height: 400px;
            overflow-y: auto;
        }
        .file-item {
            padding: 8px;
            border-bottom: 1px solid #ddd;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        .file-item:hover {
            background: #f0f0f0;
        }
        .file-name {
            font-weight: 500;
            color: #0066cc;
        }
        .file-details {
            display: flex;
            gap: 15px;
            font-size: 0.85em;
            color: #666;
        }
    </style>
</head>
<body>
    <header>
        <h1>ForkerDotNet Console</h1>
        <nav>
            <a href="/">Dashboard</a>
            <a href="/folders" class="active">Folders</a>
            <a href="/transactions">Transactions</a>
            <a href="/demo">Demo Mode</a>
            <span style="margin-left: 20px; display: inline-flex; gap: 10px; align-items: center;">
                <label for="refresh-rate" style="color: #666; font-size: 0.9em;">Refresh:</label>
                <select id="refresh-rate" onchange="updateRefreshRate(this.value)" style="padding: 4px 8px; border-radius: 4px; border: 1px solid #ddd;">
                    <option value="1">1s</option>
                    <option value="2">2s</option>
                    <option value="3">3s</option>
                    <option value="5" selected>5s</option>
                    <option value="10">10s</option>
                    <option value="60">60s</option>
                </select>
                <button id="pause-btn" onclick="togglePause()" style="padding: 4px 12px; border-radius: 4px; border: 1px solid #ddd; background: white; cursor: pointer;">⏸ Pause</button>
            </span>
        </nav>
    </header>
    <main>
        <h2>ForkerDemo Folder Scanner</h2>
        <div id="folders-container">
            <div class="loading">Loading folders...</div>
        </div>
    </main>
    <script>
    // Global state (persisted across page navigations)
    let refreshInterval = null;
    let refreshRate = parseInt(localStorage.getItem('forker-refresh-rate') || '5000');
    let isPaused = localStorage.getItem('forker-paused') === 'true';

    // Initialize on page load
    document.addEventListener('DOMContentLoaded', function() {
        // Restore UI state from localStorage
        restoreRefreshControlState();

        fetchFoldersData(); // Initial load
        startAutoRefresh(); // Start polling
    });

    // Restore refresh control UI state
    function restoreRefreshControlState() {
        const rateSelect = document.getElementById('refresh-rate');
        if (rateSelect) {
            rateSelect.value = (refreshRate / 1000).toString();
        }

        const pauseBtn = document.getElementById('pause-btn');
        if (pauseBtn) {
            pauseBtn.textContent = isPaused ? '▶ Resume' : '⏸ Pause';
            pauseBtn.style.background = isPaused ? '#ffffcc' : 'white';
        }
    }

    // Fetch folders data from API
    function fetchFoldersData() {
        fetch('/api/folders')
            .then(r => {
                if (r.status !== 200) {
                    throw new Error('API returned status ' + r.status);
                }
                return r.json();
            })
            .then(data => {
                const html = renderFolders(data);
                document.getElementById('folders-container').innerHTML = html;
            })
            .catch(err => {
                console.error('Failed to fetch folders:', err);
                document.getElementById('folders-container').innerHTML =
                    '<div class="loading">Error loading folders: ' + err.message + '</div>';
            });
    }

    // Start automatic refresh
    function startAutoRefresh() {
        if (refreshInterval) clearInterval(refreshInterval);
        refreshInterval = setInterval(() => {
            if (!isPaused) {
                fetchFoldersData();
            }
        }, refreshRate);
    }

    // Toggle pause/resume
    function togglePause() {
        isPaused = !isPaused;
        localStorage.setItem('forker-paused', isPaused.toString());

        const btn = document.getElementById('pause-btn');
        btn.textContent = isPaused ? '▶ Resume' : '⏸ Pause';
        btn.style.background = isPaused ? '#ffffcc' : 'white';
    }

    // Update refresh rate
    function updateRefreshRate(seconds) {
        refreshRate = seconds * 1000;
        localStorage.setItem('forker-refresh-rate', refreshRate.toString());

        if (!isPaused) {
            startAutoRefresh(); // Restart with new rate
        }
    }

    function renderFolders(data) {
        if (!data || Object.keys(data).length === 0) {
            return '<div class="loading">No folders found</div>';
        }

        // Render in specific order: Input, DestinationA, Failed, DestinationB
        const folderOrder = ['input', 'destinationA', 'failed', 'destinationB'];
        let html = '<div class="folders-grid">';

        folderOrder.forEach(folderKey => {
            const folderData = data[folderKey];
            if (!folderData) return;

            const folderName = folderKey.charAt(0).toUpperCase() + folderKey.slice(1);
            html += ` + "`" + `
                <div class="folder-card">
                    <h3>${folderName}</h3>
                    <div class="folder-stats">
                        <strong>${folderData.count}</strong> files
                    </div>
                    <div class="file-list">
            ` + "`" + `;

            if (folderData.files && folderData.files.length > 0) {
                folderData.files.forEach(file => {
                    html += ` + "`" + `
                        <div class="file-item">
                            <span class="file-name">${file.name}</span>
                            <div class="file-details">
                                <span>${file.sizeFormatted}</span>
                                <span>${file.age}</span>
                            </div>
                        </div>
                    ` + "`" + `;
                });
            } else {
                html += '<div class="file-item">No files</div>';
            }

            html += ` + "`" + `
                    </div>
                </div>
            ` + "`" + `;
        });

        html += '</div>';
        return html;
    }
    </script>
</body>
</html>`

	w.Write([]byte(html))
}

func handleTransactionsPage(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "text/html")

	html := `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Transactions - ForkerDotNet Console</title>
    <script src="https://unpkg.com/htmx.org@1.9.10"></script>
    <link rel="stylesheet" href="/static/style.css">
    <style>
        /* 3-pane grid: Active, Complete, Failed */
        .transactions-grid {
            display: grid;
            grid-template-columns: 1fr 1fr 1fr;
            gap: 20px;
            margin-top: 20px;
        }
        .transaction-pane {
            border: 1px solid #ddd;
            border-radius: 8px;
            padding: 15px;
            background: #f9f9f9;
        }
        .transaction-pane h3 {
            margin: 0 0 15px 0;
            color: #333;
            font-size: 1.2em;
            border-bottom: 2px solid #0066cc;
            padding-bottom: 10px;
            display: flex;
            align-items: center;
            justify-content: space-between;
        }
        .transaction-list {
            max-height: 600px;
            overflow-y: auto;
        }
        .transaction-item {
            padding: 12px;
            margin-bottom: 10px;
            border: 1px solid #ddd;
            border-radius: 4px;
            background: white;
            cursor: pointer;
        }
        .transaction-item:hover {
            background: #f0f0f0;
        }
        .transaction-filename {
            font-weight: 600;
            color: #0066cc;
            margin-bottom: 5px;
            display: flex;
            align-items: center;
            gap: 8px;
        }
        .transaction-details {
            font-size: 0.85em;
            color: #666;
        }
        .no-transactions {
            text-align: center;
            padding: 40px;
            color: #999;
            font-style: italic;
        }

        /* State badges */
        .state-badge {
            display: inline-block;
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 0.75em;
            font-weight: 600;
            text-transform: uppercase;
        }
        .state-badge.discovered { background: #2196F3; color: white; }
        .state-badge.queued { background: #FFC107; color: black; }
        .state-badge.copying { background: #FF9800; color: white; }
        .state-badge.verifying { background: #9C27B0; color: white; }

        /* Time filter dropdown */
        #complete-filter {
            padding: 4px 8px;
            border-radius: 4px;
            border: 1px solid #ddd;
            background: white;
            font-size: 0.9em;
            cursor: pointer;
        }

        /* Expandable target details */
        .job-details-expanded {
            background: #f5f5f5;
            padding: 15px;
            border-left: 3px solid #0066cc;
            margin-top: 10px;
            border-radius: 4px;
        }
        .target-detail {
            margin: 10px 0;
            padding: 10px;
            background: white;
            border-radius: 4px;
            font-size: 0.9em;
        }
        .target-detail strong {
            color: #0066cc;
        }
        .hash-match { color: green; font-weight: bold; }
        .hash-mismatch { color: red; font-weight: bold; }

        /* Expand/collapse buttons */
        .expand-btn {
            font-size: 0.85em;
            color: #0066cc;
            text-decoration: underline;
            cursor: pointer;
            margin-top: 5px;
            display: inline-block;
        }
        .expand-btn:hover {
            color: #004499;
        }

        /* Horizontal layout for transaction items */
        .transaction-item {
            display: flex;
            align-items: center;
            padding: 10px 15px;
            margin-bottom: 8px;
            border: 1px solid #ddd;
            border-radius: 4px;
            background: white;
            gap: 15px;
        }

        .transaction-item:hover {
            background: #f0f0f0;
        }

        .transaction-filename {
            flex: 1;
            font-weight: 600;
            color: #0066cc;
            margin: 0;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            min-width: 150px;
        }

        .transaction-size {
            flex: 0 0 90px;
            text-align: right;
            color: #666;
            font-size: 0.9em;
        }

        .transaction-time {
            flex: 0 0 80px;
            text-align: right;
            color: #666;
            font-size: 0.9em;
        }

        .transaction-action {
            flex: 0 0 100px;
            text-align: right;
        }

        .transaction-operation {
            flex: 0 0 200px;
            color: #666;
            font-size: 0.85em;
            text-align: right;
        }

        .state-badge-container {
            flex: 0 0 auto;
        }

        .transaction-item-expanded {
            margin-left: 20px;
            margin-bottom: 10px;
        }
    </style>
</head>
<body>
    <header>
        <h1>ForkerDotNet Console</h1>
        <nav>
            <a href="/">Dashboard</a>
            <a href="/folders">Folders</a>
            <a href="/transactions" class="active">Transactions</a>
            <a href="/demo">Demo Mode</a>
            <span style="margin-left: 20px; display: inline-flex; gap: 10px; align-items: center;">
                <label for="refresh-rate" style="color: #666; font-size: 0.9em;">Refresh:</label>
                <select id="refresh-rate" onchange="updateRefreshRate(this.value)" style="padding: 4px 8px; border-radius: 4px; border: 1px solid #ddd;">
                    <option value="1">1s</option>
                    <option value="2" selected>2s</option>
                    <option value="3">3s</option>
                    <option value="10">10s</option>
                    <option value="60">60s</option>
                </select>
                <button id="pause-btn" onclick="togglePause()" style="padding: 4px 12px; border-radius: 4px; border: 1px solid #ddd; background: white; cursor: pointer;">⏸ Pause</button>
            </span>
        </nav>
    </header>
    <main>
        <h2>File Copy Transactions</h2>
        <div id="transactions-container">
            <div class="loading">Loading transactions...</div>
        </div>
    </main>
    <script>
    // Global state (persisted across page navigations)
    let allJobDetails = [];
    let expandedJobs = new Set();
    let refreshInterval = null;
    let refreshRate = parseInt(localStorage.getItem('forker-refresh-rate') || '2000');
    let isPaused = localStorage.getItem('forker-paused') === 'true';

    // Initialize on page load
    document.addEventListener('DOMContentLoaded', function() {
        // Restore UI state from localStorage
        restoreRefreshControlState();

        fetchJobsData(); // Initial load
        startAutoRefresh(); // Start polling
    });

    // Restore refresh control UI state
    function restoreRefreshControlState() {
        const rateSelect = document.getElementById('refresh-rate');
        if (rateSelect) {
            rateSelect.value = (refreshRate / 1000).toString();
        }

        const pauseBtn = document.getElementById('pause-btn');
        if (pauseBtn) {
            pauseBtn.textContent = isPaused ? '▶ Resume' : '⏸ Pause';
            pauseBtn.style.background = isPaused ? '#ffffcc' : 'white';
        }
    }

    // Fetch jobs data from API
    function fetchJobsData() {
        fetch('/api/jobs')
            .then(r => {
                if (r.status !== 200) {
                    throw new Error('API returned status ' + r.status);
                }
                return r.json();
            })
            .then(data => {
                const jobs = data.jobs || [];
                if (jobs.length === 0) {
                    document.getElementById('transactions-container').innerHTML =
                        '<div class="no-transactions">No jobs in database yet</div>';
                    return;
                }
                return fetchAllJobDetails(jobs);
            })
            .then(() => {
                renderTransactions();
            })
            .catch(err => {
                console.error('Failed to fetch jobs:', err);
                document.getElementById('transactions-container').innerHTML =
                    '<div class="loading">Error loading transactions: ' + err.message + '</div>';
            });
    }

    // Batch fetch all job details
    async function fetchAllJobDetails(jobs) {
        const detailPromises = jobs.map(job =>
            fetch('/api/jobs/' + job.jobId)
                .then(r => r.json())
                .catch(err => {
                    console.warn('Failed to load job ' + job.jobId + ':', err);
                    return null;
                })
        );

        const results = await Promise.all(detailPromises);
        allJobDetails = results.filter(j => j !== null);
    }

    // Start automatic refresh
    function startAutoRefresh() {
        if (refreshInterval) clearInterval(refreshInterval);
        refreshInterval = setInterval(() => {
            if (!isPaused) {
                fetchJobsData();
            }
        }, refreshRate);
    }

    // Toggle pause/resume
    function togglePause() {
        isPaused = !isPaused;
        localStorage.setItem('forker-paused', isPaused.toString());

        const btn = document.getElementById('pause-btn');
        btn.textContent = isPaused ? '▶ Resume' : '⏸ Pause';
        btn.style.background = isPaused ? '#ffffcc' : 'white';
    }

    // Update refresh rate
    function updateRefreshRate(seconds) {
        refreshRate = seconds * 1000;
        localStorage.setItem('forker-refresh-rate', refreshRate.toString());

        if (!isPaused) {
            startAutoRefresh(); // Restart with new rate
        }
    }

    // Render transactions UI
    function renderTransactions() {
        const container = document.getElementById('transactions-container');
        if (!container) return;

        // Group jobs by state
        const active = allJobDetails.filter(j =>
            ['Discovered', 'Queued', 'InProgress', 'Partial'].includes(j.state)
        );
        const allComplete = allJobDetails.filter(j => j.state === 'Verified');
        const failed = allJobDetails.filter(j =>
            ['Failed', 'Quarantined'].includes(j.state)
        );

        // Apply time filter to Complete pane
        const filterValue = document.getElementById('complete-filter')?.value || 'today';
        const complete = filterJobsByTime(allComplete, filterValue);

        let html = '<div class="transactions-grid">';
        html += renderActivePane(active);
        html += renderCompletePane(complete);
        html += renderFailedPane(failed);
        html += '</div>';

        container.innerHTML = html;
    }

    // Render Active pane (horizontal layout) - shows jobs/targets being worked on
    function renderActivePane(jobs) {
        let html = '<div class="transaction-pane"><h3>Active (' + jobs.length + ')</h3><div class="transaction-list">';

        if (jobs.length > 0) {
            jobs.forEach(job => {
                const filename = getFilename(job.sourcePath);
                const size = formatBytes(job.sizeBytes || 0);
                const queuedTime = formatTime(job.createdAt);
                let rendered = false;

                // For Discovered/Queued jobs: show job-level state (not started copying yet)
                if (job.state === 'Discovered' || job.state === 'Queued') {
                    const badge = getStateBadge(job.state);
                    const operation = getStateDescription(job.state);

                    html += '<div class="transaction-item">';
                    html += '<div class="transaction-filename">' + filename + '</div>';
                    html += '<div class="transaction-size">' + size + '</div>';
                    html += '<div class="state-badge-container">' + badge + '</div>';
                    html += '<div class="transaction-operation">' + operation + ' @ ' + queuedTime + '</div>';
                    html += '</div>';
                    rendered = true;
                }
                // For InProgress/Partial jobs: show individual target operations
                else if (job.state === 'InProgress' || job.state === 'Partial') {
                    if (job.targets && job.targets.length > 0) {
                        job.targets.forEach(target => {
                            // Show ALL targets (API returns 'copyState' field)
                            const badge = getTargetStateBadge(target.copyState);
                            const operation = getTargetStateDescription(target.copyState, target.targetId);

                            html += '<div class="transaction-item">';
                            html += '<div class="transaction-filename">' + filename + ' → ' + target.targetId + '</div>';
                            html += '<div class="transaction-size">' + size + '</div>';
                            html += '<div class="state-badge-container">' + badge + '</div>';
                            html += '<div class="transaction-operation">' + operation + ' @ ' + queuedTime + '</div>';
                            html += '</div>';
                            rendered = true;
                        });
                    }
                }

                // Fallback: if nothing rendered yet, show job-level state
                if (!rendered) {
                    const badge = getStateBadge(job.state);
                    const operation = getStateDescription(job.state);

                    html += '<div class="transaction-item">';
                    html += '<div class="transaction-filename">' + filename + '</div>';
                    html += '<div class="transaction-size">' + size + '</div>';
                    html += '<div class="state-badge-container">' + badge + '</div>';
                    html += '<div class="transaction-operation">' + operation + ' @ ' + queuedTime + '</div>';
                    html += '</div>';
                }
            });
        } else {
            html += '<div class="no-transactions">No files processing - system ready</div>';
        }

        html += '</div></div>';
        return html;
    }

    // Get target state badge (for individual target operations)
    function getTargetStateBadge(state) {
        const badges = {
            'Pending': '<span class="state-badge queued">Pending</span>',
            'Copying': '<span class="state-badge copying">Copying</span>',
            'Copied': '<span class="state-badge copying">Copied</span>',
            'Verifying': '<span class="state-badge verifying">Verifying</span>',
            'Verified': '<span class="state-badge discovered">Verified</span>',
            'FailedRetryable': '<span class="state-badge failed">Failed (Retrying)</span>',
            'FailedPermanent': '<span class="state-badge failed">Failed</span>'
        };
        return badges[state] || '<span class="state-badge">' + state + '</span>';
    }

    // Get target state description (for individual target operations)
    function getTargetStateDescription(state, targetId) {
        const descriptions = {
            'Pending': 'Waiting to copy to ' + targetId,
            'Copying': 'Copying to ' + targetId,
            'Copied': 'Copied to ' + targetId + ', waiting for verification',
            'Verifying': 'Verifying hash for ' + targetId,
            'Verified': 'Verified at ' + targetId,
            'FailedRetryable': 'Failed at ' + targetId + ' (will retry)',
            'FailedPermanent': 'Failed permanently at ' + targetId
        };
        return descriptions[state] || (state + ' - ' + targetId);
    }

    // Render Complete pane (horizontal layout)
    function renderCompletePane(jobs) {
        let html = '<div class="transaction-pane">';
        html += '<h3>Complete (' + jobs.length + ')';
        html += '<select id="complete-filter" onchange="handleFilterChange()">';
        html += '<option value="hour">Last Hour</option>';
        html += '<option value="today" selected>Today</option>';
        html += '<option value="all">All Time</option>';
        html += '</select></h3>';
        html += '<div class="transaction-list">';

        if (jobs.length > 0) {
            jobs.forEach(job => {
                const filename = getFilename(job.sourcePath);
                const size = formatBytes(job.sizeBytes || 0);
                const time = formatTime(job.createdAt);
                const isExpanded = expandedJobs.has(job.jobId);

                html += '<div class="transaction-item">';
                html += '<div class="transaction-filename">' + filename + '</div>';
                html += '<div class="transaction-size">' + size + '</div>';
                html += '<div class="transaction-time">' + time + '</div>';
                html += '<div class="transaction-action">';
                html += '<span class="expand-btn" onclick="toggleJobDetails(\'' + job.jobId + '\')">';
                html += isExpanded ? '▼ Hide' : '▶ Details';
                html += '</span>';
                html += '</div>';
                html += '</div>';

                if (isExpanded) {
                    html += '<div class="transaction-item-expanded">';
                    html += renderTargetDetails(job);
                    html += '</div>';
                }
            });
        } else {
            const filter = document.getElementById('complete-filter')?.value || 'today';
            const message = filter === 'all' ? 'No completed jobs yet' : 'No jobs completed ' + getFilterLabel(filter);
            html += '<div class="no-transactions">' + message + '</div>';
        }

        html += '</div></div>';
        return html;
    }

    // Render Failed pane (horizontal layout)
    function renderFailedPane(jobs) {
        let html = '<div class="transaction-pane"><h3>Failed (' + jobs.length + ')</h3><div class="transaction-list">';

        if (jobs.length > 0) {
            jobs.forEach(job => {
                const filename = getFilename(job.sourcePath);
                const size = formatBytes(job.sizeBytes || 0);
                const time = formatTime(job.createdAt);

                html += '<div class="transaction-item">';
                html += '<div class="transaction-filename">' + filename + '</div>';
                html += '<div class="transaction-size">' + size + '</div>';
                html += '<div class="transaction-time">' + time + '</div>';
                html += '<div class="transaction-action" style="color: red; font-weight: 600;">' + job.state + '</div>';
                html += '</div>';
            });
        } else {
            html += '<div class="no-transactions">No failures detected</div>';
        }

        html += '</div></div>';
        return html;
    }

    // Render target details (unchanged)
    function renderTargetDetails(job) {
        if (!job.targets || job.targets.length === 0) {
            return '<div class="job-details-expanded">No target data available</div>';
        }

        let html = '<div class="job-details-expanded">';

        job.targets.forEach(target => {
            html += '<div class="target-detail">';
            html += '<strong>' + target.targetId + ':</strong> ';
            html += target.state === 'Verified' ? '✓ ' : '✗ ';
            html += target.state + '<br>';

            if (target.hash) {
                const hashMatch = target.hash === job.sourceHash;
                html += 'Hash: ' + target.hash.substring(0, 16) + '... ';
                html += '<span class="' + (hashMatch ? 'hash-match' : 'hash-mismatch') + '">';
                html += hashMatch ? '(matches source)' : '(⚠️ MISMATCH)';
                html += '</span><br>';
            }

            if (target.finalPath) {
                html += 'Path: ' + target.finalPath + '<br>';
            }

            if (target.lastTransitionAt) {
                html += 'Completed: ' + formatTime(target.lastTransitionAt);
            }

            html += '</div>';
        });

        html += '</div>';
        return html;
    }

    // Toggle job details expansion
    function toggleJobDetails(jobId) {
        if (expandedJobs.has(jobId)) {
            expandedJobs.delete(jobId);
        } else {
            expandedJobs.add(jobId);
        }
        renderTransactions();
    }

    // Handle filter change
    function handleFilterChange() {
        renderTransactions();
    }

    // Filter jobs by time
    function filterJobsByTime(jobs, filter) {
        if (filter === 'all') return jobs;

        const now = new Date();
        let cutoff;

        if (filter === 'hour') {
            cutoff = new Date(now.getTime() - 60 * 60 * 1000);
        } else if (filter === 'today') {
            cutoff = new Date(now.getFullYear(), now.getMonth(), now.getDate());
        }

        return jobs.filter(j => new Date(j.createdAt) >= cutoff);
    }

    // Get state badge HTML
    function getStateBadge(state) {
        const badges = {
            'Discovered': '<span class="state-badge discovered">Discovered</span>',
            'Queued': '<span class="state-badge queued">Queued</span>',
            'InProgress': '<span class="state-badge copying">Copying</span>',
            'Partial': '<span class="state-badge verifying">Verifying</span>'
        };
        return badges[state] || '';
    }

    // Get state description
    function getStateDescription(state) {
        const descriptions = {
            'Discovered': 'File found, checking stability',
            'Queued': 'Stable, waiting for worker',
            'InProgress': 'Copying to targets',
            'Partial': 'Copy complete, verifying hashes'
        };
        return descriptions[state] || state;
    }

    // Get filter label
    function getFilterLabel(filter) {
        const labels = {
            'hour': 'in last hour',
            'today': 'today',
            'all': 'ever'
        };
        return labels[filter] || filter;
    }

    // Get filename from path
    function getFilename(path) {
        if (!path) return 'Unknown file';
        const parts = path.split(/[\\/]/);
        return parts[parts.length - 1] || 'Unknown file';
    }

    // Format bytes
    function formatBytes(bytes) {
        if (!bytes || bytes === 0) return '0 B';
        if (isNaN(bytes)) return '0 B';
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
    }

    // Format time
    function formatTime(timestamp) {
        if (!timestamp) return 'N/A';
        const date = new Date(timestamp);
        return date.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
    }
    </script>
</body>
</html>`

	w.Write([]byte(html))
}

func handleDashboardEnhancedAPI(w http.ResponseWriter, r *http.Request) {
	data := map[string]interface{}{
		"Title": "ForkerDotNet Console",
		"Page":  "dashboard",
	}

	w.Header().Set("Content-Type", "text/html")
	// Try dashboard-enhanced.html first, fall back to dashboard.html
	if err := templates.ExecuteTemplate(w, "dashboard-enhanced.html", data); err != nil {
		log.Printf("[WARN] Enhanced dashboard template not found, using basic dashboard: %v", err)
		if err := templates.ExecuteTemplate(w, "base.html", data); err != nil {
			log.Printf("[ERROR] Template execution failed: %v", err)
			http.Error(w, "Internal Server Error", http.StatusInternalServerError)
		}
	}
}

func handleJobListAPI(w http.ResponseWriter, r *http.Request) {
	client := GetAPIClient()
	if client == nil {
		http.Error(w, "API client not configured", http.StatusServiceUnavailable)
		return
	}

	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()

	jobs, err := client.GetJobs(ctx, "", 100)
	if err != nil {
		log.Printf("[ERROR] Failed to get jobs: %v", err)
		http.Error(w, "Failed to retrieve jobs", http.StatusInternalServerError)
		return
	}

	// Always return JSON - JavaScript will handle HTML rendering
	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(map[string]interface{}{
		"jobs": jobs,
	})
}

func handleJobDetailAPI(w http.ResponseWriter, r *http.Request, id string) {
	client := GetAPIClient()
	if client == nil {
		http.Error(w, "API client not configured", http.StatusServiceUnavailable)
		return
	}

	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()

	details, err := client.GetJobDetails(ctx, id)
	if err != nil {
		log.Printf("[ERROR] Failed to get job details for %s: %v", id, err)
		http.Error(w, "Job not found", http.StatusNotFound)
		return
	}

	if details == nil {
		http.Error(w, "Job not found", http.StatusNotFound)
		return
	}

	// Check if this is an API request (from /api/jobs/{id}) or page request (from /jobs/{id})
	// API requests should always return JSON, page requests return HTML
	isAPIPath := strings.HasPrefix(r.URL.Path, "/api/")

	if isAPIPath || r.Header.Get("HX-Request") == "true" || r.Header.Get("Accept") == "application/json" {
		// Return JSON for API paths, HTMX requests, or explicit JSON requests
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(details)
	} else {
		// Return HTML template for page views (/jobs/{id})
		data := map[string]interface{}{
			"Title": "Job Details",
			"Page":  "job-detail",
			"Job":   enrichAPIJobDetailsForDisplay(details),
		}
		w.Header().Set("Content-Type", "text/html")
		if err := templates.ExecuteTemplate(w, "base.html", data); err != nil {
			log.Printf("[ERROR] Template execution failed: %v", err)
			http.Error(w, "Internal Server Error", http.StatusInternalServerError)
		}
	}
}

func handleStatsAPI(w http.ResponseWriter, r *http.Request) {
	client := GetAPIClient()
	if client == nil {
		http.Error(w, "API client not configured", http.StatusServiceUnavailable)
		return
	}

	ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
	defer cancel()

	stats, err := client.GetStats(ctx)
	if err != nil {
		log.Printf("[ERROR] Failed to get stats: %v", err)
		http.Error(w, "Failed to retrieve stats", http.StatusInternalServerError)
		return
	}

	// Check if htmx request (wants HTML fragment) or regular request (wants JSON)
	if r.Header.Get("HX-Request") == "true" {
		data := map[string]interface{}{
			"TotalJobs":      stats.TotalJobs,
			"ActiveJobs":     stats.Discovered + stats.Queued + stats.InProgress + stats.Partial,
			"CompletedJobs":  stats.Verified,
			"FailedJobs":     stats.Failed + stats.Quarantined,
			"ThroughputMBps": "N/A", // TODO: Calculate from recent jobs
		}
		w.Header().Set("Content-Type", "text/html")
		if err := templates.ExecuteTemplate(w, "stats-bar", data); err != nil {
			log.Printf("[ERROR] Template execution failed: %v", err)
			http.Error(w, "Internal Server Error", http.StatusInternalServerError)
		}
	} else {
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(stats)
	}
}

// handleSSEAPI provides Server-Sent Events for real-time job updates
func handleSSEAPI(w http.ResponseWriter, r *http.Request) {
	// Set SSE headers
	w.Header().Set("Content-Type", "text/event-stream")
	w.Header().Set("Cache-Control", "no-cache")
	w.Header().Set("Connection", "keep-alive")
	w.Header().Set("Access-Control-Allow-Origin", "*")

	// Get flusher for streaming
	flusher, ok := w.(http.Flusher)
	if !ok {
		http.Error(w, "Streaming unsupported", http.StatusInternalServerError)
		return
	}

	client := GetAPIClient()
	if client == nil {
		http.Error(w, "API client not configured", http.StatusServiceUnavailable)
		return
	}

	// Client context for cancellation
	ctx := r.Context()
	ticker := time.NewTicker(2 * time.Second)
	defer ticker.Stop()

	log.Println("[INFO] SSE client connected")

	for {
		select {
		case <-ctx.Done():
			log.Println("[INFO] SSE client disconnected")
			return
		case <-ticker.C:
			// Get recent jobs via API
			jobCtx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
			jobs, err := client.GetJobs(jobCtx, "", 100)
			cancel()

			if err != nil {
				log.Printf("[ERROR] SSE: Failed to get jobs: %v", err)
				continue
			}

			// Send job update event
			jobsJSON, _ := json.Marshal(enrichAPIJobsForDisplay(jobs))
			fmt.Fprintf(w, "event: job-update\ndata: %s\n\n", jobsJSON)
			flusher.Flush()
		}
	}
}

// --- Helper Functions for API Models ---

func enrichAPIJobsForDisplay(jobs []apiclient.JobSummary) []JobDisplay {
	result := make([]JobDisplay, len(jobs))
	for i, job := range jobs {
		// Parse CreatedAt from RFC3339 or SQLite TEXT format
		createdAt, err := time.Parse(time.RFC3339, job.CreatedAt)
		if err != nil {
			createdAt, _ = time.Parse("2006-01-02 15:04:05", job.CreatedAt)
		}

		duration := "N/A"
		if !createdAt.IsZero() {
			duration = time.Since(createdAt).Round(time.Second).String()
		}

		result[i] = JobDisplay{
			ID:              job.JobID,
			SourceFile:      filepath.Base(job.SourcePath),
			SizeFormatted:   formatBytes(job.InitialSize),
			State:           job.State,
			ProgressPercent: calculateProgress(job.State),
			StartedAt:       job.CreatedAt,
			Duration:        duration,
		}
	}
	return result
}

type APIJobDetailsDisplay struct {
	JobID         string
	SourcePath    string
	State         string
	SizeFormatted string
	SourceHash    string
	CreatedAt     string
	VersionToken  int
	TargetA       *TargetDisplay
	TargetB       *TargetDisplay
	Events        []EventDisplay
}

func enrichAPIJobDetailsForDisplay(details *apiclient.JobDetails) *APIJobDetailsDisplay {
	result := &APIJobDetailsDisplay{
		JobID:         details.JobID,
		SourcePath:    details.SourcePath,
		State:         details.State,
		SizeFormatted: formatBytes(details.InitialSize),
		SourceHash:    stringOr(details.SourceHash, "N/A"),
		CreatedAt:     details.CreatedAt,
		VersionToken:  details.VersionToken,
	}

	// Find TargetA and TargetB
	for _, target := range details.Targets {
		display := &TargetDisplay{
			State:        target.CopyState,
			Path:         target.TargetID,
			Hash:         stringOr(target.Hash, "N/A"),
			BytesCopied:  formatBytesPtr(target.BytesCopied),
			ProgressText: fmt.Sprintf("%s / %s", formatBytesPtr(target.BytesCopied), formatBytes(details.InitialSize)),
		}

		if target.TargetID == "TargetA" {
			result.TargetA = display
		} else if target.TargetID == "TargetB" {
			result.TargetB = display
		}
	}

	// TODO: Load events when API supports it
	result.Events = []EventDisplay{}

	return result
}
