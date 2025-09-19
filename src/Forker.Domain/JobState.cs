namespace Forker.Domain;

/// <summary>
/// Represents the overall state of a file job in the system.
/// State transitions must be monotonic (except QUARANTINED → QUEUED manual intervention).
/// </summary>
public enum JobState
{
    /// <summary>
    /// File has been discovered but stability check has not passed yet.
    /// </summary>
    Discovered = 0,

    /// <summary>
    /// File is stable and queued for processing.
    /// </summary>
    Queued = 1,

    /// <summary>
    /// At least one target is being processed, but not all targets are complete.
    /// </summary>
    InProgress = 2,

    /// <summary>
    /// Some targets are verified but not all required targets are complete.
    /// </summary>
    Partial = 3,

    /// <summary>
    /// All required targets have been successfully copied and verified.
    /// Terminal state for successful jobs.
    /// </summary>
    Verified = 4,

    /// <summary>
    /// Job has failed permanently and cannot be retried automatically.
    /// Terminal state for failed jobs.
    /// </summary>
    Failed = 5,

    /// <summary>
    /// Job has been quarantined due to hash mismatch or other integrity issues.
    /// Requires manual intervention to requeue.
    /// </summary>
    Quarantined = 6
}