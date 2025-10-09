package server

import (
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"os"
	"path/filepath"

	"forkerDotNet/console/internal/filesystem"
)

// FolderPaths holds the configured folder paths
type FolderPaths struct {
	Input        string
	DestinationA string
	DestinationB string
	Failed       string
}

// GetFolderPaths returns the configured folder paths from environment
func GetFolderPaths() FolderPaths {
	dataPath := os.Getenv("FORKER_DATA_PATH")
	if dataPath == "" {
		dataPath = "/data" // Default for Docker
	}

	return FolderPaths{
		Input:        filepath.Join(dataPath, "Input"),
		DestinationA: filepath.Join(dataPath, "DestinationA"),
		DestinationB: filepath.Join(dataPath, "DestinationB"),
		Failed:       filepath.Join(dataPath, "Failed"),
	}
}

// handleFolderView handles GET /api/folders/{folder}
// Returns file listing for Input, DestinationA, DestinationB, or Failed
func handleFolderView(w http.ResponseWriter, r *http.Request, folderName string) {
	paths := GetFolderPaths()

	var folderPath string
	switch folderName {
	case "input":
		folderPath = paths.Input
	case "destinationa", "desta":
		folderPath = paths.DestinationA
	case "destinationb", "destb":
		folderPath = paths.DestinationB
	case "failed":
		folderPath = paths.Failed
	default:
		http.Error(w, "Invalid folder name", http.StatusBadRequest)
		return
	}

	// Scan folder
	files, err := filesystem.ScanFolder(folderPath)
	if err != nil {
		log.Printf("[ERROR] Failed to scan folder %s: %v", folderPath, err)
		http.Error(w, fmt.Sprintf("Failed to scan folder: %v", err), http.StatusInternalServerError)
		return
	}

	// Get folder stats
	stats, err := filesystem.GetFolderStats(folderPath)
	if err != nil {
		log.Printf("[ERROR] Failed to get folder stats %s: %v", folderPath, err)
		// Continue with just files, no stats
	}

	response := map[string]interface{}{
		"folder": folderName,
		"path":   folderPath,
		"files":  files,
		"stats":  stats,
		"count":  len(files),
	}

	// Check if htmx request (wants HTML fragment) or regular request (wants JSON)
	if r.Header.Get("HX-Request") == "true" {
		// Return HTML fragment for htmx
		data := map[string]interface{}{
			"FolderName": folderName,
			"Files":      files,
			"Count":      len(files),
			"Stats":      stats,
		}
		w.Header().Set("Content-Type", "text/html")
		if err := templates.ExecuteTemplate(w, "folder-pane", data); err != nil {
			log.Printf("[ERROR] Template execution failed: %v", err)
			http.Error(w, "Internal Server Error", http.StatusInternalServerError)
		}
	} else {
		// Return JSON for API consumers
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(response)
	}
}

// handleAllFolders handles GET /api/folders
// Returns file listings for all 4 folders
func handleAllFolders(w http.ResponseWriter, r *http.Request) {
	paths := GetFolderPaths()

	// Scan all folders
	inputFiles, _ := filesystem.ScanFolder(paths.Input)
	destAFiles, _ := filesystem.ScanFolder(paths.DestinationA)
	destBFiles, _ := filesystem.ScanFolder(paths.DestinationB)
	failedFiles, _ := filesystem.ScanFolder(paths.Failed)

	// Get stats for all folders
	inputStats, _ := filesystem.GetFolderStats(paths.Input)
	destAStats, _ := filesystem.GetFolderStats(paths.DestinationA)
	destBStats, _ := filesystem.GetFolderStats(paths.DestinationB)
	failedStats, _ := filesystem.GetFolderStats(paths.Failed)

	response := map[string]interface{}{
		"input": map[string]interface{}{
			"files": inputFiles,
			"stats": inputStats,
			"count": len(inputFiles),
		},
		"destinationA": map[string]interface{}{
			"files": destAFiles,
			"stats": destAStats,
			"count": len(destAFiles),
		},
		"destinationB": map[string]interface{}{
			"files": destBFiles,
			"stats": destBStats,
			"count": len(destBFiles),
		},
		"failed": map[string]interface{}{
			"files": failedFiles,
			"stats": failedStats,
			"count": len(failedFiles),
		},
	}

	// Check if htmx request (wants HTML fragment) or regular request (wants JSON)
	if r.Header.Get("HX-Request") == "true" {
		// Return HTML with all 4 folder panes
		data := map[string]interface{}{
			"InputFiles":      inputFiles,
			"InputCount":      len(inputFiles),
			"InputStats":      inputStats,
			"DestAFiles":      destAFiles,
			"DestACount":      len(destAFiles),
			"DestAStats":      destAStats,
			"DestBFiles":      destBFiles,
			"DestBCount":      len(destBFiles),
			"DestBStats":      destBStats,
			"FailedFiles":     failedFiles,
			"FailedCount":     len(failedFiles),
			"FailedStats":     failedStats,
		}
		w.Header().Set("Content-Type", "text/html")
		if err := templates.ExecuteTemplate(w, "folders-view", data); err != nil {
			log.Printf("[ERROR] Template execution failed: %v", err)
			http.Error(w, "Internal Server Error", http.StatusInternalServerError)
		}
	} else {
		// Return JSON for API consumers
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(response)
	}
}
