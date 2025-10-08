package database

import (
	"database/sql"
	"fmt"

	_ "modernc.org/sqlite" // Pure Go SQLite driver
)

// Database wraps the SQLite connection
type Database struct {
	conn *sql.DB
}

// NewDatabase opens a SQLite database in read-only mode
func NewDatabase(path string) (*Database, error) {
	// Open SQLite with nolock mode for cross-platform container access
	// This prevents WAL locking issues when accessing Windows DB from Linux container
	connString := fmt.Sprintf("file:%s?mode=ro&nolock=1", path)

	conn, err := sql.Open("sqlite", connString)
	if err != nil {
		return nil, fmt.Errorf("failed to open database: %w", err)
	}

	// Configure connection pool
	conn.SetMaxOpenConns(5)
	conn.SetMaxIdleConns(2)
	conn.SetConnMaxLifetime(0)

	return &Database{conn: conn}, nil
}

// Close closes the database connection
func (db *Database) Close() error {
	if db.conn != nil {
		return db.conn.Close()
	}
	return nil
}

// Ping verifies the database connection is alive
func (db *Database) Ping() error {
	return db.conn.Ping()
}

// GetRecentJobs retrieves the most recent file jobs
func (db *Database) GetRecentJobs(limit int) ([]FileJob, error) {
	query := `
		SELECT
			Id, SourcePath, State, InitialSize, SourceHash,
			CreatedAt, VersionToken
		FROM FileJobs
		ORDER BY CreatedAt DESC
		LIMIT ?
	`

	rows, err := db.conn.Query(query, limit)
	if err != nil {
		return nil, fmt.Errorf("query failed: %w", err)
	}
	defer rows.Close()

	var jobs []FileJob
	for rows.Next() {
		var job FileJob
		err := rows.Scan(
			&job.ID,
			&job.SourcePath,
			&job.State,
			&job.InitialSize,
			&job.SourceHash,
			&job.CreatedAt,
			&job.VersionToken,
		)
		if err != nil {
			return nil, fmt.Errorf("scan failed: %w", err)
		}
		jobs = append(jobs, job)
	}

	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("rows error: %w", err)
	}

	return jobs, nil
}

// GetJobDetails retrieves a specific job with all related data
func (db *Database) GetJobDetails(id string) (*JobDetails, error) {
	// Query job
	jobQuery := `
		SELECT
			Id, SourcePath, State, InitialSize, SourceHash,
			CreatedAt, VersionToken
		FROM FileJobs
		WHERE Id = ?
	`

	var job FileJob
	err := db.conn.QueryRow(jobQuery, id).Scan(
		&job.ID,
		&job.SourcePath,
		&job.State,
		&job.InitialSize,
		&job.SourceHash,
		&job.CreatedAt,
		&job.VersionToken,
	)
	if err != nil {
		return nil, fmt.Errorf("job not found: %w", err)
	}

	// Query target outcomes
	targetQuery := `
		SELECT
			JobId, TargetId, CopyState, Hash, BytesCopied, LastTransitionAt
		FROM TargetOutcomes
		WHERE JobId = ?
	`

	rows, err := db.conn.Query(targetQuery, id)
	if err != nil {
		return nil, fmt.Errorf("query targets failed: %w", err)
	}
	defer rows.Close()

	var targets []TargetOutcome
	for rows.Next() {
		var target TargetOutcome
		err := rows.Scan(
			&target.JobID,
			&target.TargetID,
			&target.State,
			&target.Hash,
			&target.BytesCopied,
			&target.LastTransitionAt,
		)
		if err != nil {
			return nil, fmt.Errorf("scan target failed: %w", err)
		}
		targets = append(targets, target)
	}

	return &JobDetails{
		Job:     job,
		Targets: targets,
	}, nil
}

// GetStats retrieves summary statistics
func (db *Database) GetStats() (*Stats, error) {
	var stats Stats

	// Count total jobs
	err := db.conn.QueryRow("SELECT COUNT(*) FROM FileJobs").Scan(&stats.TotalJobs)
	if err != nil {
		return nil, fmt.Errorf("count total jobs failed: %w", err)
	}

	// Count verified jobs
	err = db.conn.QueryRow("SELECT COUNT(*) FROM FileJobs WHERE State = 'Verified'").Scan(&stats.Verified)
	if err != nil {
		return nil, fmt.Errorf("count verified jobs failed: %w", err)
	}

	// Count failed jobs
	err = db.conn.QueryRow("SELECT COUNT(*) FROM FileJobs WHERE State = 'Failed'").Scan(&stats.Failed)
	if err != nil {
		return nil, fmt.Errorf("count failed jobs failed: %w", err)
	}

	// Count quarantined jobs
	err = db.conn.QueryRow("SELECT COUNT(*) FROM FileJobs WHERE State = 'Quarantined'").Scan(&stats.Quarantined)
	if err != nil {
		return nil, fmt.Errorf("count quarantined jobs failed: %w", err)
	}

	// Count active jobs (not in terminal states)
	err = db.conn.QueryRow("SELECT COUNT(*) FROM FileJobs WHERE State NOT IN ('Verified', 'Failed', 'Quarantined')").Scan(&stats.Active)
	if err != nil {
		return nil, fmt.Errorf("count active jobs failed: %w", err)
	}

	return &stats, nil
}
