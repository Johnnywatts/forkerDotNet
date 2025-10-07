-- ForkerDotNet Demo Database Connection
-- Open this file in DataGrip to connect to the Demo environment database
--
-- Database Location: C:\ForkerDemo\forker.db
-- Environment: Demo
--
-- To connect in DataGrip:
-- 1. Open this file in DataGrip
-- 2. DataGrip should auto-detect the SQLite database from the file path below
-- 3. Or manually create a connection:
--    - Data Source: SQLite
--    - File: C:\ForkerDemo\forker.db
--
-- Key Tables to Monitor:
-- - FileJobs: Main job tracking with state transitions
-- - TargetOutcomes: Per-target copy/verify status (TargetA, TargetB)
-- - DatabaseMetadata: Schema version info

-- Quick Status Query: View all active jobs
SELECT
    Id,
    SourcePath,
    State,
    InitialSize,
    SourceHash,
    CreatedAt,
    VersionToken
FROM FileJobs
ORDER BY CreatedAt DESC
LIMIT 20;

-- Target Status Query: See per-target outcomes
SELECT
    fj.SourcePath,
    t_o.TargetId,
    t_o.CopyState,
    t_o.Attempts,
    t_o.Hash,
    t_o.FinalPath,
    t_o.LastError,
    t_o.LastTransitionAt
FROM TargetOutcomes t_o
JOIN FileJobs fj ON t_o.JobId = fj.Id
ORDER BY t_o.LastTransitionAt DESC
LIMIT 20;

-- Combined View: Jobs with their target outcomes
SELECT
    fj.SourcePath,
    fj.State as JobState,
    t_o.TargetId,
    t_o.CopyState as TargetState,
    t_o.Attempts,
    t_o.LastError
FROM FileJobs fj
LEFT JOIN TargetOutcomes t_o ON fj.Id = t_o.JobId
ORDER BY fj.CreatedAt DESC;

-- Data Integrity Check: Verify hash matching
SELECT
    fj.SourcePath,
    fj.SourceHash,
    t_o.TargetId,
    t_o.Hash as TargetHash,
    CASE
        WHEN fj.SourceHash = t_o.Hash THEN 'MATCH ✓'
        WHEN fj.SourceHash IS NULL OR t_o.Hash IS NULL THEN 'PENDING...'
        ELSE 'MISMATCH ✗'
    END as HashStatus,
    fj.State as JobState,
    t_o.CopyState as TargetState
FROM FileJobs fj
JOIN TargetOutcomes t_o ON fj.Id = t_o.JobId
WHERE fj.State IN ('Verified', 'Quarantined', 'InProgress')
ORDER BY fj.CreatedAt DESC;

-- Failed Jobs: Show errors
SELECT
    fj.SourcePath,
    fj.State,
    t_o.TargetId,
    t_o.CopyState,
    t_o.Attempts,
    t_o.LastError,
    t_o.LastTransitionAt
FROM FileJobs fj
JOIN TargetOutcomes t_o ON fj.Id = t_o.JobId
WHERE fj.State = 'Failed' OR t_o.CopyState LIKE 'Failed%'
ORDER BY t_o.LastTransitionAt DESC;

-- Processing Timeline: Watch state progression
SELECT
    fj.SourcePath,
    fj.State as JobState,
    t_o.TargetId,
    t_o.CopyState as TargetState,
    t_o.LastTransitionAt
FROM FileJobs fj
LEFT JOIN TargetOutcomes t_o ON fj.Id = t_o.JobId
WHERE fj.CreatedAt >= datetime('now', '-1 hour')
ORDER BY fj.CreatedAt DESC, t_o.TargetId;
