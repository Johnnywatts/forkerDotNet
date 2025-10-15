package server

import (
	"net/http"
	"strings"
)

// NewAPIRouter creates the HTTP router using API-based handlers (Phase 3)
func NewAPIRouter() http.Handler {
	mux := http.NewServeMux()

	// Health endpoint
	mux.HandleFunc("/health", handleHealthAPI)
	mux.HandleFunc("/api/system-info", handleSystemInfoAPI)

	// Dashboard
	mux.HandleFunc("/", handleDashboardAPI)
	mux.HandleFunc("/dashboard", handleDashboardAPI)
	mux.HandleFunc("/folders", handleFoldersPage)
	mux.HandleFunc("/transactions", handleTransactionsPage)

	// API endpoints
	mux.HandleFunc("/api/jobs", handleJobListAPI)
	mux.HandleFunc("/api/jobs/", func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/api/jobs/" {
			// No ID provided, redirect to list
			handleJobListAPI(w, r)
			return
		}

		// Check if this is a state-history request
		if strings.HasSuffix(r.URL.Path, "/state-history") {
			id := strings.TrimSuffix(PathParam(r.URL.Path, "/api/jobs/"), "/state-history")
			handleJobStateHistoryAPI(w, r, id)
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
