package server

import (
	"forkerDotNet/console/internal/database"
)

// Global database instance (for simple console app)
var db *database.Database
var dbPath string

// SetDatabase stores the database instance for handlers to access
func SetDatabase(database *database.Database) {
	db = database
}

// GetDatabase returns the database instance
func GetDatabase() *database.Database {
	return db
}

// SetDatabasePath stores the database path for display
func SetDatabasePath(path string) {
	dbPath = path
}

// GetDatabasePath returns the database path
func GetDatabasePath() string {
	return dbPath
}
