package server

import (
	"forkerDotNet/console/internal/database"
)

// Global database instance (for simple console app)
var db *database.Database

// SetDatabase stores the database instance for handlers to access
func SetDatabase(database *database.Database) {
	db = database
}

// GetDatabase returns the database instance
func GetDatabase() *database.Database {
	return db
}
