-- ForkerDotNet Database Schema v2
-- Add StateChangeLog table for audit trail and observability
-- Date: 2025-10-10

-- StateChangeLog table - Complete audit trail of all state transitions
CREATE TABLE IF NOT EXISTS StateChangeLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,   -- Unique log entry ID
    JobId TEXT NOT NULL,                     -- FileJobId (foreign key reference)
    EntityType TEXT NOT NULL,                -- 'Job' or 'Target'
    EntityId TEXT,                           -- NULL for Job, TargetId for Target
    OldState TEXT,                           -- Previous state (NULL for initial state)
    NewState TEXT NOT NULL,                  -- New state after transition
    Timestamp TEXT NOT NULL,                 -- ISO 8601 datetime (UTC) with millisecond precision
    DurationMs INTEGER,                      -- Time since last state change for this entity (milliseconds)
    AdditionalContext TEXT,                  -- JSON object with additional data (hashes, errors, bytes copied, etc.)
    CONSTRAINT chk_entity_type CHECK (EntityType IN ('Job', 'Target'))
);

-- Indexes for efficient querying
CREATE INDEX IF NOT EXISTS ix_statelog_jobid ON StateChangeLog(JobId);
CREATE INDEX IF NOT EXISTS ix_statelog_timestamp ON StateChangeLog(Timestamp);
CREATE INDEX IF NOT EXISTS ix_statelog_entity ON StateChangeLog(EntityType, EntityId);
CREATE INDEX IF NOT EXISTS ix_statelog_newstate ON StateChangeLog(NewState);

-- Update schema version
UPDATE DatabaseMetadata SET Value = '2', UpdatedAt = datetime('now') WHERE Key = 'SchemaVersion';
INSERT OR IGNORE INTO DatabaseMetadata (Key, Value, UpdatedAt) VALUES ('SchemaVersion', '2', datetime('now'));
