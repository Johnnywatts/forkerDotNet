package server

import (
	"encoding/json"
	"net/http"
	"strings"
)

// PathParam extracts a path parameter by removing a prefix
// Example: PathParam("/api/jobs/123", "/api/jobs/") returns "123"
func PathParam(path, prefix string) string {
	return strings.TrimPrefix(path, prefix)
}

// NewRouter creates the HTTP router with all routes configured
func NewRouter() http.Handler {
	mux := http.NewServeMux()

	// Health endpoint
	mux.HandleFunc("/health", handleHealth)

	// Dashboard
	mux.HandleFunc("/", handleDashboard)

	// API endpoints
	mux.HandleFunc("/api/jobs", handleJobList)
	mux.HandleFunc("/api/jobs/", func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/api/jobs/" {
			// No ID provided, redirect to list
			handleJobList(w, r)
			return
		}
		id := PathParam(r.URL.Path, "/api/jobs/")
		handleJobDetail(w, r, id)
	})

	// Static files
	mux.Handle("/static/", http.StripPrefix("/static/", http.FileServer(http.Dir("web/static"))))

	// Apply middleware chain
	handler := Recoverer(Logger(mux))
	return handler
}

// --- HTTP Handlers ---

func handleHealth(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusOK)
	json.NewEncoder(w).Encode(map[string]string{
		"status": "healthy",
		"service": "forker-console",
	})
}

func handleDashboard(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "text/html")
	w.Write([]byte(`<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
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
        <h2>Production Monitoring Dashboard</h2>
        <div id="jobs-table" hx-get="/api/jobs" hx-trigger="load, every 5s">
            Loading jobs...
        </div>
    </main>
</body>
</html>`))
}

func handleJobList(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	// TODO: Query database and return jobs
	json.NewEncoder(w).Encode(map[string]interface{}{
		"jobs": []map[string]string{
			{"id": "1", "source": "test.svs", "state": "Verified"},
		},
	})
}

func handleJobDetail(w http.ResponseWriter, r *http.Request, id string) {
	w.Header().Set("Content-Type", "application/json")
	// TODO: Query database for specific job
	json.NewEncoder(w).Encode(map[string]interface{}{
		"id": id,
		"source": "test.svs",
		"state": "Verified",
	})
}
