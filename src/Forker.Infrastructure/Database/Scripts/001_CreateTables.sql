-- ForkerDotNet Database Schema v1
-- SQLite DDL with WAL mode optimizations
-- Supports optimistic concurrency control and crash-safe operations

-- Enable WAL mode for better concurrency and crash safety
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA cache_size = 10000;
PRAGMA foreign_keys = ON;

-- FileJobs table - Core job aggregate
CREATE TABLE IF NOT EXISTS FileJobs (
    Id TEXT PRIMARY KEY,                    -- FileJobId as string (GUID)
    SourcePath TEXT NOT NULL,               -- Full path to source file
    InitialSize INTEGER NOT NULL CHECK (InitialSize >= 0), -- File size in bytes
    SourceHash TEXT,                        -- SHA-256 hash (nullable until computed)
    State TEXT NOT NULL,                    -- JobState enum as string
    RequiredTargets TEXT NOT NULL,          -- JSON array of TargetId strings
    CreatedAt TEXT NOT NULL,                -- ISO 8601 datetime (UTC)
    VersionToken INTEGER NOT NULL DEFAULT 1 CHECK (VersionToken > 0), -- Optimistic concurrency
    CONSTRAINT chk_state CHECK (State IN ('Discovered', 'Queued', 'InProgress', 'Partial', 'Verified', 'Failed', 'Quarantined'))
);

-- TargetOutcomes table - Target-specific outcomes
CREATE TABLE IF NOT EXISTS TargetOutcomes (
    JobId TEXT NOT NULL,                    -- Foreign key to FileJobs.Id
    TargetId TEXT NOT NULL,                 -- Target identifier
    CopyState TEXT NOT NULL,                -- TargetCopyState enum as string
    Attempts INTEGER NOT NULL DEFAULT 0 CHECK (Attempts >= 0), -- Number of attempts
    Hash TEXT,                              -- File hash at target (nullable)
    TempPath TEXT,                          -- Temporary file path during copy
    FinalPath TEXT,                         -- Final file path at target
    LastError TEXT,                         -- Last error message (nullable)
    LastTransitionAt TEXT NOT NULL,         -- ISO 8601 datetime (UTC)
    PRIMARY KEY (JobId, TargetId),          -- Composite primary key
    FOREIGN KEY (JobId) REFERENCES FileJobs(Id) ON DELETE CASCADE,
    CONSTRAINT chk_copy_state CHECK (CopyState IN ('Pending', 'Copying', 'Copied', 'Verifying', 'Verified', 'FailedRetryable', 'FailedPermanent'))
);

-- Indexes for performance
CREATE INDEX IF NOT EXISTS ix_filejobs_state ON FileJobs(State);
CREATE INDEX IF NOT EXISTS ix_filejobs_created_at ON FileJobs(CreatedAt);
CREATE INDEX IF NOT EXISTS ix_filejobs_source_path ON FileJobs(SourcePath);
CREATE INDEX IF NOT EXISTS ix_targetoutcomes_copy_state ON TargetOutcomes(CopyState);
CREATE INDEX IF NOT EXISTS ix_targetoutcomes_last_transition ON TargetOutcomes(LastTransitionAt);

-- Database metadata table for schema versioning
CREATE TABLE IF NOT EXISTS DatabaseMetadata (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Insert initial schema version
INSERT OR IGNORE INTO DatabaseMetadata (Key, Value, UpdatedAt)
VALUES ('SchemaVersion', '1', datetime('now'));