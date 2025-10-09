package server

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"path/filepath"
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
        </nav>
    </header>
    <main>
        <h2>ForkerDemo Folder Scanner</h2>
        <div id="folders-container" hx-get="/api/folders" hx-trigger="load, every 5s" hx-swap="none">
            <div class="loading">Loading folders...</div>
        </div>
    </main>
    <script>
    document.body.addEventListener('htmx:afterRequest', function(evt) {
        if (evt.detail.target.id === 'folders-container') {
            try {
                const data = JSON.parse(evt.detail.xhr.responseText);
                const html = renderFolders(data);
                evt.detail.target.innerHTML = html;
            } catch (e) {
                console.error('Failed to parse folder data:', e, evt.detail.xhr.responseText);
                evt.detail.target.innerHTML = '<div class="loading">Error loading folders: ' + e.message + '</div>';
            }
        }
    });

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
        .transactions-grid {
            display: grid;
            grid-template-columns: 1fr 1fr;
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
        }
        .transaction-item:hover {
            background: #f0f0f0;
        }
        .transaction-filename {
            font-weight: 600;
            color: #0066cc;
            margin-bottom: 5px;
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
        </nav>
    </header>
    <main>
        <h2>File Copy Transactions</h2>
        <div id="transactions-container" hx-get="/api/jobs" hx-trigger="load, every 5s" hx-swap="none">
            <div class="loading">Loading transactions...</div>
        </div>
    </main>
    <script>
    document.body.addEventListener('htmx:afterRequest', function(evt) {
        if (evt.detail.target.id === 'transactions-container') {
            try {
                const data = JSON.parse(evt.detail.xhr.responseText);
                // API returns {"jobs": [...]} so extract the jobs array
                const jobs = data.jobs || [];
                const html = renderTransactions(jobs);
                evt.detail.target.innerHTML = html;
            } catch (e) {
                console.error('Failed to parse transaction data:', e);
                evt.detail.target.innerHTML = '<div class="loading">Error loading transactions: ' + e.message + '</div>';
            }
        }
    });

    function renderTransactions(jobs) {
        if (!jobs || jobs.length === 0) {
            return '<div class="no-transactions">No transactions yet</div>';
        }

        // Group jobs by state (API returns camelCase: InProgress, Verified, etc.)
        const pending = jobs.filter(j => j.state === 'Queued' || j.state === 'Discovered');
        const copied = jobs.filter(j => j.state === 'InProgress' || j.state === 'Partial');
        const verified = jobs.filter(j => j.state === 'Verified');
        const failed = jobs.filter(j => j.state === 'Failed' || j.state === 'Quarantined');

        let html = '<div class="transactions-grid">';

        // Pending pane
        html += ` + "`" + `
            <div class="transaction-pane">
                <h3>Pending (${pending.length})</h3>
                <div class="transaction-list">
        ` + "`" + `;

        if (pending.length > 0) {
            pending.forEach(job => {
                html += ` + "`" + `
                    <div class="transaction-item">
                        <div class="transaction-filename">${job.filename}</div>
                        <div class="transaction-details">
                            State: ${job.state}<br>
                            Size: ${formatBytes(job.sizeBytes)}
                        </div>
                    </div>
                ` + "`" + `;
            });
        } else {
            html += '<div class="no-transactions">No pending transactions</div>';
        }

        html += '</div></div>';

        // Copied pane
        html += ` + "`" + `
            <div class="transaction-pane">
                <h3>Copied (${copied.length})</h3>
                <div class="transaction-list">
        ` + "`" + `;

        if (copied.length > 0) {
            copied.forEach(job => {
                html += ` + "`" + `
                    <div class="transaction-item">
                        <div class="transaction-filename">${job.filename}</div>
                        <div class="transaction-details">
                            State: ${job.state}<br>
                            Size: ${formatBytes(job.sizeBytes)}
                        </div>
                    </div>
                ` + "`" + `;
            });
        } else {
            html += '<div class="no-transactions">No copied transactions</div>';
        }

        html += '</div></div>';

        // Verified pane
        html += ` + "`" + `
            <div class="transaction-pane">
                <h3>Verified (${verified.length})</h3>
                <div class="transaction-list">
        ` + "`" + `;

        if (verified.length > 0) {
            verified.forEach(job => {
                html += ` + "`" + `
                    <div class="transaction-item">
                        <div class="transaction-filename">${job.filename}</div>
                        <div class="transaction-details">
                            State: ${job.state}<br>
                            Size: ${formatBytes(job.sizeBytes)}
                        </div>
                    </div>
                ` + "`" + `;
            });
        } else {
            html += '<div class="no-transactions">No verified transactions</div>';
        }

        html += '</div></div>';

        // Failed pane
        html += ` + "`" + `
            <div class="transaction-pane">
                <h3>Failed (${failed.length})</h3>
                <div class="transaction-list">
        ` + "`" + `;

        if (failed.length > 0) {
            failed.forEach(job => {
                html += ` + "`" + `
                    <div class="transaction-item">
                        <div class="transaction-filename">${job.filename}</div>
                        <div class="transaction-details">
                            State: ${job.state}<br>
                            Size: ${formatBytes(job.sizeBytes)}
                        </div>
                    </div>
                ` + "`" + `;
            });
        } else {
            html += '<div class="no-transactions">No failed transactions</div>';
        }

        html += '</div></div></div>';
        return html;
    }

    function formatBytes(bytes) {
        if (bytes === 0) return '0 B';
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
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

	// Check if htmx request or regular page load
	if r.Header.Get("HX-Request") == "true" {
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(details)
	} else {
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
			State:        target.State,
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
