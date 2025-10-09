package filesystem

import (
	"fmt"
	"os"
	"path/filepath"
	"time"
)

// FileInfo represents a file in a scanned folder
type FileInfo struct {
	Name         string    `json:"name"`
	FullPath     string    `json:"fullPath"`
	Size         int64     `json:"size"`
	SizeFormatted string   `json:"sizeFormatted"`
	ModifiedTime time.Time `json:"modifiedTime"`
	Age          string    `json:"age"`
}

// FolderStats represents aggregate statistics for a folder
type FolderStats struct {
	Path          string `json:"path"`
	TotalFiles    int    `json:"totalFiles"`
	TotalSize     int64  `json:"totalSize"`
	TotalSizeFormatted string `json:"totalSizeFormatted"`
	OldestFile    *FileInfo `json:"oldestFile"`
	NewestFile    *FileInfo `json:"newestFile"`
}

// ScanFolder scans a directory and returns a list of files
// Returns files sorted by modification time (newest first)
func ScanFolder(path string) ([]FileInfo, error) {
	// Check if path exists
	info, err := os.Stat(path)
	if err != nil {
		if os.IsNotExist(err) {
			return []FileInfo{}, nil // Return empty list for non-existent folders
		}
		return nil, fmt.Errorf("stat folder: %w", err)
	}

	if !info.IsDir() {
		return nil, fmt.Errorf("path is not a directory: %s", path)
	}

	// Read directory entries
	entries, err := os.ReadDir(path)
	if err != nil {
		return nil, fmt.Errorf("read directory: %w", err)
	}

	var files []FileInfo
	for _, entry := range entries {
		// Skip directories
		if entry.IsDir() {
			continue
		}

		// Get file info
		fileInfo, err := entry.Info()
		if err != nil {
			continue // Skip files we can't stat
		}

		fullPath := filepath.Join(path, entry.Name())
		modTime := fileInfo.ModTime()

		files = append(files, FileInfo{
			Name:         entry.Name(),
			FullPath:     fullPath,
			Size:         fileInfo.Size(),
			SizeFormatted: formatBytes(fileInfo.Size()),
			ModifiedTime: modTime,
			Age:          formatAge(modTime),
		})
	}

	// Sort by modification time (newest first)
	// Using simple bubble sort for small lists
	for i := 0; i < len(files)-1; i++ {
		for j := i + 1; j < len(files); j++ {
			if files[j].ModifiedTime.After(files[i].ModifiedTime) {
				files[i], files[j] = files[j], files[i]
			}
		}
	}

	return files, nil
}

// GetFolderStats returns aggregate statistics for a folder
func GetFolderStats(path string) (*FolderStats, error) {
	files, err := ScanFolder(path)
	if err != nil {
		return nil, err
	}

	stats := &FolderStats{
		Path:       path,
		TotalFiles: len(files),
		TotalSize:  0,
	}

	if len(files) == 0 {
		stats.TotalSizeFormatted = "0 B"
		return stats, nil
	}

	// Calculate total size and find oldest/newest
	var oldest, newest *FileInfo
	for i := range files {
		stats.TotalSize += files[i].Size

		if oldest == nil || files[i].ModifiedTime.Before(oldest.ModifiedTime) {
			oldest = &files[i]
		}
		if newest == nil || files[i].ModifiedTime.After(newest.ModifiedTime) {
			newest = &files[i]
		}
	}

	stats.TotalSizeFormatted = formatBytes(stats.TotalSize)
	stats.OldestFile = oldest
	stats.NewestFile = newest

	return stats, nil
}

// formatBytes converts bytes to human-readable format
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

// formatAge converts a time to a human-readable age string
func formatAge(t time.Time) string {
	duration := time.Since(t)

	if duration < time.Minute {
		return fmt.Sprintf("%ds ago", int(duration.Seconds()))
	} else if duration < time.Hour {
		return fmt.Sprintf("%dm ago", int(duration.Minutes()))
	} else if duration < 24*time.Hour {
		hours := int(duration.Hours())
		if hours == 1 {
			return "1h ago"
		}
		return fmt.Sprintf("%dh ago", hours)
	} else {
		days := int(duration.Hours() / 24)
		if days == 1 {
			return "1 day ago"
		}
		return fmt.Sprintf("%d days ago", days)
	}
}
