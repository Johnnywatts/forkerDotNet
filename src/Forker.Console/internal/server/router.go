package server

import (
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
	mux.HandleFunc("/api/stats", handleStats)
	mux.HandleFunc("/api/stream", handleSSE)

	// Job detail page
	mux.HandleFunc("/jobs/", func(w http.ResponseWriter, r *http.Request) {
		id := PathParam(r.URL.Path, "/jobs/")
		handleJobDetail(w, r, id)
	})

	// Static files
	mux.Handle("/static/", http.StripPrefix("/static/", http.FileServer(http.Dir("web/static"))))

	// Apply middleware chain
	handler := Recoverer(Logger(mux))
	return handler
}
