package server

import (
	"net/http"
)

// NewAPIRouter creates the HTTP router using API-based handlers (Phase 3)
func NewAPIRouter() http.Handler {
	mux := http.NewServeMux()

	// Health endpoint
	mux.HandleFunc("/health", handleHealthAPI)
	mux.HandleFunc("/api/system-info", handleSystemInfoAPI)

	// Dashboard
	mux.HandleFunc("/", handleDashboardEnhancedAPI)
	mux.HandleFunc("/dashboard", handleDashboardAPI) // Legacy Phase 2 dashboard

	// API endpoints
	mux.HandleFunc("/api/jobs", handleJobListAPI)
	mux.HandleFunc("/api/jobs/", func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/api/jobs/" {
			// No ID provided, redirect to list
			handleJobListAPI(w, r)
			return
		}
		id := PathParam(r.URL.Path, "/api/jobs/")
		handleJobDetailAPI(w, r, id)
	})
	mux.HandleFunc("/api/stats", handleStatsAPI)
	mux.HandleFunc("/api/stream", handleSSEAPI)

	// Job detail page
	mux.HandleFunc("/jobs/", func(w http.ResponseWriter, r *http.Request) {
		id := PathParam(r.URL.Path, "/jobs/")
		handleJobDetailAPI(w, r, id)
	})

	// Folder scanning endpoints (Phase 3 Task 3.3)
	mux.HandleFunc("/api/folders", handleAllFolders)
	mux.HandleFunc("/api/folders/", func(w http.ResponseWriter, r *http.Request) {
		folderName := PathParam(r.URL.Path, "/api/folders/")
		if folderName == "" {
			handleAllFolders(w, r)
			return
		}
		handleFolderView(w, r, folderName)
	})

	// Static files
	mux.Handle("/static/", http.StripPrefix("/static/", http.FileServer(http.Dir("web/static"))))

	// Apply middleware chain
	handler := Recoverer(Logger(mux))
	return handler
}
