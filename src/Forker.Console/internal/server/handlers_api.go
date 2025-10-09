package server

import (
	"context"
	"encoding/json"
	"fmt"
	"html/template"
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

	// Check if htmx request (wants HTML fragment) or regular request (wants JSON)
	if r.Header.Get("HX-Request") == "true" {
		data := map[string]interface{}{
			"Jobs": enrichAPIJobsForDisplay(jobs),
		}
		w.Header().Set("Content-Type", "text/html")
		if err := templates.ExecuteTemplate(w, "job-list", data); err != nil {
			log.Printf("[ERROR] Template execution failed: %v", err)
			http.Error(w, "Internal Server Error", http.StatusInternalServerError)
		}
	} else {
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(map[string]interface{}{
			"jobs": jobs,
		})
	}
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
