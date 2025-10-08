package server

import (
	"encoding/json"
	"fmt"
	"html/template"
	"log"
	"net/http"
	"path/filepath"
	"time"

	"forkerDotNet/console/internal/database"
)

var templates *template.Template

// InitTemplates loads and parses all HTML templates
func InitTemplates() error {
	var err error
	pattern := filepath.Join("web", "templates", "*.html")
	templates, err = template.ParseGlob(pattern)
	if err != nil {
		return fmt.Errorf("failed to parse templates: %w", err)
	}
	if templates == nil || len(templates.Templates()) == 0 {
		return fmt.Errorf("no templates found matching pattern: %s", pattern)
	}
	log.Printf("[INFO] Loaded %d templates from %s", len(templates.Templates()), pattern)
	return nil
}

// --- HTTP Handlers ---

func handleHealth(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusOK)
	json.NewEncoder(w).Encode(map[string]string{
		"status":  "healthy",
		"service": "forker-console",
	})
}

func handleSystemInfo(w http.ResponseWriter, r *http.Request) {
	log.Printf("[DEBUG] handleSystemInfo called, HX-Request=%s", r.Header.Get("HX-Request"))

	info := map[string]interface{}{
		"database_path": GetDatabasePath(),
		"timestamp":     time.Now().Format("2006-01-02 15:04:05"),
		"service":       "forker-console",
		"version":       "1.0.0",
	}

	// Check if htmx request (wants HTML fragment) or regular request (wants JSON)
	if r.Header.Get("HX-Request") == "true" {
		log.Printf("[DEBUG] Returning HTML fragment")
		// Return HTML fragment for htmx
		w.Header().Set("Content-Type", "text/html")
		if err := templates.ExecuteTemplate(w, "system-info", info); err != nil {
			log.Printf("[ERROR] Template execution failed: %v", err)
			http.Error(w, "Internal Server Error", http.StatusInternalServerError)
			return
		}
		log.Printf("[DEBUG] Template executed successfully")
	} else {
		log.Printf("[DEBUG] Returning JSON")
		// Return JSON for API consumers
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(info)
	}
}

func handleDashboard(w http.ResponseWriter, r *http.Request) {
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

func handleJobList(w http.ResponseWriter, r *http.Request) {
	db := GetDatabase()
	if db == nil {
		http.Error(w, "Database not available", http.StatusServiceUnavailable)
		return
	}

	jobs, err := db.GetRecentJobs(100)
	if err != nil {
		log.Printf("[ERROR] Failed to get jobs: %v", err)
		http.Error(w, "Failed to retrieve jobs", http.StatusInternalServerError)
		return
	}

	// Check if htmx request (wants HTML fragment) or regular request (wants JSON)
	if r.Header.Get("HX-Request") == "true" {
		// Return HTML fragment for htmx
		data := map[string]interface{}{
			"Jobs": enrichJobsForDisplay(jobs),
		}
		w.Header().Set("Content-Type", "text/html")
		if err := templates.ExecuteTemplate(w, "job-list", data); err != nil {
			log.Printf("[ERROR] Template execution failed: %v", err)
			http.Error(w, "Internal Server Error", http.StatusInternalServerError)
		}
	} else {
		// Return JSON for API consumers
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(map[string]interface{}{
			"jobs": jobs,
		})
	}
}

func handleJobDetail(w http.ResponseWriter, r *http.Request, id string) {
	db := GetDatabase()
	if db == nil {
		http.Error(w, "Database not available", http.StatusServiceUnavailable)
		return
	}

	details, err := db.GetJobDetails(id)
	if err != nil {
		log.Printf("[ERROR] Failed to get job details for %s: %v", id, err)
		http.Error(w, "Job not found", http.StatusNotFound)
		return
	}

	// Check if htmx request or regular page load
	if r.Header.Get("HX-Request") == "true" {
		// Return JSON for htmx to process
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(details)
	} else {
		// Return full HTML page
		data := map[string]interface{}{
			"Title": "Job Details",
			"Page":  "job-detail",
			"Job":   enrichJobDetailsForDisplay(details),
		}
		w.Header().Set("Content-Type", "text/html")
		if err := templates.ExecuteTemplate(w, "base.html", data); err != nil {
			log.Printf("[ERROR] Template execution failed: %v", err)
			http.Error(w, "Internal Server Error", http.StatusInternalServerError)
		}
	}
}

func handleStats(w http.ResponseWriter, r *http.Request) {
	db := GetDatabase()
	if db == nil {
		http.Error(w, "Database not available", http.StatusServiceUnavailable)
		return
	}

	stats, err := db.GetStats()
	if err != nil {
		log.Printf("[ERROR] Failed to get stats: %v", err)
		http.Error(w, "Failed to retrieve stats", http.StatusInternalServerError)
		return
	}

	// Check if htmx request (wants HTML fragment) or regular request (wants JSON)
	if r.Header.Get("HX-Request") == "true" {
		// Return HTML fragment for htmx
		data := map[string]interface{}{
			"TotalJobs":     stats.TotalJobs,
			"ActiveJobs":    stats.Active,
			"CompletedJobs": stats.Verified,
			"FailedJobs":    stats.Failed + stats.Quarantined,
			"ThroughputMBps": "N/A", // TODO: Calculate from recent jobs
		}
		w.Header().Set("Content-Type", "text/html")
		if err := templates.ExecuteTemplate(w, "stats-bar", data); err != nil {
			log.Printf("[ERROR] Template execution failed: %v", err)
			http.Error(w, "Internal Server Error", http.StatusInternalServerError)
		}
	} else {
		// Return JSON for API consumers
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(stats)
	}
}

// handleSSE provides Server-Sent Events for real-time job updates
func handleSSE(w http.ResponseWriter, r *http.Request) {
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

	db := GetDatabase()
	if db == nil {
		http.Error(w, "Database not available", http.StatusServiceUnavailable)
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
			// Get recent jobs
			jobs, err := db.GetRecentJobs(100)
			if err != nil {
				log.Printf("[ERROR] SSE: Failed to get jobs: %v", err)
				continue
			}

			// Send job update event
			jobsJSON, _ := json.Marshal(enrichJobsForDisplay(jobs))
			fmt.Fprintf(w, "event: job-update\ndata: %s\n\n", jobsJSON)
			flusher.Flush()
		}
	}
}

// --- Helper Functions ---

type JobDisplay struct {
	ID              string
	SourceFile      string
	SizeFormatted   string
	State           string
	ProgressPercent int
	StartedAt       string
	Duration        string
}

func enrichJobsForDisplay(jobs []database.FileJob) []JobDisplay {
	result := make([]JobDisplay, len(jobs))
	for i, job := range jobs {
		// Parse CreatedAt from SQLite TEXT format
		createdAt, err := time.Parse("2006-01-02 15:04:05", job.CreatedAt)
		if err != nil {
			// Try RFC3339 format as fallback
			createdAt, _ = time.Parse(time.RFC3339, job.CreatedAt)
		}

		duration := "N/A"
		if !createdAt.IsZero() {
			duration = time.Since(createdAt).Round(time.Second).String()
		}

		result[i] = JobDisplay{
			ID:              job.ID,
			SourceFile:      filepath.Base(job.SourcePath),
			SizeFormatted:   formatBytes(job.InitialSize),
			State:           job.State,
			ProgressPercent: calculateProgress(job.State),
			StartedAt:       job.CreatedAt, // Already a string
			Duration:        duration,
		}
	}
	return result
}

type JobDetailsDisplay struct {
	database.JobDetails
	SizeFormatted string
	TargetA       *TargetDisplay
	TargetB       *TargetDisplay
	Events        []EventDisplay
}

type TargetDisplay struct {
	State        string
	Path         string
	Hash         string
	BytesCopied  string
	ProgressText string
}

type EventDisplay struct {
	Timestamp string
	Type      string
	Message   string
}

func enrichJobDetailsForDisplay(details *database.JobDetails) *JobDetailsDisplay {
	result := &JobDetailsDisplay{
		JobDetails:    *details,
		SizeFormatted: formatBytes(details.Job.InitialSize),
	}

	// Find TargetA and TargetB
	for _, target := range details.Targets {
		display := &TargetDisplay{
			State:        target.State,
			Path:         target.TargetID,
			Hash:         stringOr(target.Hash, "N/A"),
			BytesCopied:  formatBytesPtr(target.BytesCopied),
			ProgressText: fmt.Sprintf("%s / %s", formatBytesPtr(target.BytesCopied), formatBytes(details.Job.InitialSize)),
		}

		if target.TargetID == "TargetA" {
			result.TargetA = display
		} else if target.TargetID == "TargetB" {
			result.TargetB = display
		}
	}

	// TODO: Load events from database when implemented
	result.Events = []EventDisplay{}

	return result
}

func formatBytes(bytes int64) string {
	const unit = 1024
	if bytes < unit {
		return fmt.Sprintf("%d B", bytes)
	}
	div, exp := int64(unit), 0
	for n := bytes / unit; n >= unit; n /= unit {
		div *= unit
		exp++
	}
	return fmt.Sprintf("%.1f %cB", float64(bytes)/float64(div), "KMGTPE"[exp])
}

func formatBytesPtr(bytes *int64) string {
	if bytes == nil {
		return "0 B"
	}
	return formatBytes(*bytes)
}

func stringOr(s *string, defaultVal string) string {
	if s == nil {
		return defaultVal
	}
	return *s
}

func calculateProgress(state string) int {
	switch state {
	case "Discovered":
		return 10
	case "Queued":
		return 20
	case "InProgress":
		return 50
	case "Partial":
		return 75
	case "Verified":
		return 100
	case "Failed", "Quarantined":
		return 0
	default:
		return 0
	}
}
