package main

import (
	"context"
	"forkerDotNet/console/internal/apiclient"
	"forkerDotNet/console/internal/database"
	"forkerDotNet/console/internal/server"
	"log"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"
)

func main() {
	log.Printf("[INFO] Starting ForkerDotNet Console")

	// Determine mode: API (Phase 3) or SQLite (Phase 2)
	apiURL := os.Getenv("FORKER_API_URL")
	useAPI := apiURL != ""

	if useAPI {
		log.Printf("[INFO] Running in API mode")
		log.Printf("[INFO] API URL: %s", apiURL)
		runAPIMode(apiURL)
	} else {
		log.Printf("[INFO] Running in SQLite mode (legacy)")
		runSQLiteMode()
	}
}

func runAPIMode(apiURL string) {
	// Initialize API client
	client := apiclient.NewClient(apiURL)

	// Verify API connection
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	if err := client.Ping(ctx); err != nil {
		log.Printf("[WARN] API ping failed: %v", err)
		log.Printf("[WARN] Continuing anyway - API may be starting up")
	} else {
		log.Printf("[INFO] API connection established")
	}

	// Store API client in context for handlers to access
	server.SetAPIClient(client)

	// Initialize HTML templates
	if err := server.InitTemplates(); err != nil {
		log.Fatalf("[FATAL] Failed to load templates: %v", err)
	}
	log.Printf("[INFO] HTML templates loaded")

	// Create HTTP router with API handlers
	router := server.NewAPIRouter()

	// Create HTTP server
	srv := &http.Server{
		Addr:         ":5000",
		Handler:      router,
		ReadTimeout:  15 * time.Second,
		WriteTimeout: 15 * time.Second,
		IdleTimeout:  60 * time.Second,
	}

	// Start server in a goroutine
	go func() {
		log.Printf("[INFO] Console listening on http://localhost:5000")
		log.Printf("[INFO] Health endpoint: http://localhost:5000/health")
		log.Printf("[INFO] Mode: API-based (Phase 3)")
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Fatalf("[FATAL] Server failed: %v", err)
		}
	}()

	// Wait for interrupt signal to gracefully shutdown
	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	<-quit

	log.Println("[INFO] Shutting down console...")

	// Graceful shutdown with 5 second timeout
	shutdownCtx, shutdownCancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer shutdownCancel()

	if err := srv.Shutdown(shutdownCtx); err != nil {
		log.Printf("[ERROR] Server forced to shutdown: %v", err)
	}

	log.Println("[INFO] Console stopped")
}

func runSQLiteMode() {
	// Get database path from environment or use default
	dbPath := os.Getenv("FORKER_DB_PATH")
	if dbPath == "" {
		// Default to ForkerDemo path on Windows
		dbPath = `C:\ForkerDemo\forker.db`
	}
	log.Printf("[INFO] Database: %s", dbPath)

	// Initialize database connection (read-only)
	db, err := database.NewDatabase(dbPath)
	if err != nil {
		log.Fatalf("[FATAL] Failed to open database: %v", err)
	}
	defer db.Close()

	// Verify database connection
	if err := db.Ping(); err != nil {
		log.Fatalf("[FATAL] Database ping failed: %v", err)
	}
	log.Printf("[INFO] Database connection established (read-only mode)")

	// Store database in context for handlers to access
	server.SetDatabase(db)
	server.SetDatabasePath(dbPath)

	// Initialize HTML templates
	if err := server.InitTemplates(); err != nil {
		log.Fatalf("[FATAL] Failed to load templates: %v", err)
	}
	log.Printf("[INFO] HTML templates loaded")

	// Create HTTP router with SQLite handlers
	router := server.NewRouter()

	// Create HTTP server
	srv := &http.Server{
		Addr:         ":5000",
		Handler:      router,
		ReadTimeout:  15 * time.Second,
		WriteTimeout: 15 * time.Second,
		IdleTimeout:  60 * time.Second,
	}

	// Start server in a goroutine
	go func() {
		log.Printf("[INFO] Console listening on http://localhost:5000")
		log.Printf("[INFO] Health endpoint: http://localhost:5000/health")
		log.Printf("[INFO] Mode: SQLite direct (Phase 2 - deprecated)")
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Fatalf("[FATAL] Server failed: %v", err)
		}
	}()

	// Wait for interrupt signal to gracefully shutdown
	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	<-quit

	log.Println("[INFO] Shutting down console...")

	// Graceful shutdown with 5 second timeout
	shutdownCtx, shutdownCancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer shutdownCancel()

	if err := srv.Shutdown(shutdownCtx); err != nil {
		log.Printf("[ERROR] Server forced to shutdown: %v", err)
	}

	log.Println("[INFO] Console stopped")
}
