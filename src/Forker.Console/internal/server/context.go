package server

import (
	"forkerDotNet/console/internal/apiclient"
	"forkerDotNet/console/internal/database"
)

// Global instances (for simple console app)
var db *database.Database       // Legacy SQLite client (Phase 2)
var apiClient *apiclient.Client // HTTP API client (Phase 3)
var dbPath string

// SetDatabase stores the database instance for handlers to access (Phase 2 - deprecated)
func SetDatabase(database *database.Database) {
	db = database
}

// GetDatabase returns the database instance (Phase 2 - deprecated)
func GetDatabase() *database.Database {
	return db
}

// SetAPIClient stores the API client for handlers to access (Phase 3)
func SetAPIClient(client *apiclient.Client) {
	apiClient = client
}

// GetAPIClient returns the API client (Phase 3)
func GetAPIClient() *apiclient.Client {
	return apiClient
}

// SetDatabasePath stores the database path for display
func SetDatabasePath(path string) {
	dbPath = path
}

// GetDatabasePath returns the database path
func GetDatabasePath() string {
	return dbPath
}
