namespace Forker.Domain.Services;

/// <summary>
/// Service for managing permanently failed items that cannot be retried.
/// Provides audit trail and manual intervention capabilities for failed medical imaging workflows.
/// </summary>
public interface IDeadLetterService
{
    /// <summary>
    /// Adds a target outcome to the dead letter queue after exhausting all retry attempts.
    /// Used when Invariant I6 (MaxAttempts → FAILED_PERMANENT) is triggered.
    /// </summary>
    /// <param name="targetOutcome">The target outcome that failed permanently</param>
    /// <param name="reason">Reason for permanent failure</param>
    /// <param name="operationType">The operation type that failed</param>
    /// <param name="lastException">The final exception that caused permanent failure</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Dead letter entry for audit trail</returns>
    Task<DeadLetterEntry> AddToDeadLetterQueueAsync(TargetOutcome targetOutcome, string reason,
        OperationType operationType, Exception lastException, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a file job to the dead letter queue when all targets have failed permanently.
    /// </summary>
    /// <param name="fileJob">The file job that failed permanently</param>
    /// <param name="reason">Reason for permanent failure</param>
    /// <param name="failedTargets">Collection of target outcomes that caused job failure</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Dead letter entry for the entire job</returns>
    Task<DeadLetterEntry> AddJobToDeadLetterQueueAsync(FileJob fileJob, string reason,
        IEnumerable<TargetOutcome> failedTargets, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves items from the dead letter queue based on filter criteria.
    /// Used for administrative review and potential manual intervention.
    /// </summary>
    /// <param name="filter">Optional filter criteria</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Collection of dead letter entries</returns>
    Task<IReadOnlyList<DeadLetterEntry>> GetDeadLetterEntriesAsync(DeadLetterFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually requeues an item from the dead letter queue for retry.
    /// Requires administrative approval for medical data integrity.
    /// </summary>
    /// <param name="entryId">ID of the dead letter entry to requeue</param>
    /// <param name="reason">Reason for requeuing</param>
    /// <param name="requeuedBy">Identity of person/system performing requeue</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Result of requeue operation</returns>
    Task<DeadLetterRequeueResult> RequeueFromDeadLetterAsync(Guid entryId, string reason, string requeuedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently removes an item from the dead letter queue.
    /// Used for confirmed data corruption or obsolete files.
    /// </summary>
    /// <param name="entryId">ID of the dead letter entry to purge</param>
    /// <param name="reason">Reason for permanent removal</param>
    /// <param name="purgedBy">Identity of person/system performing purge</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Result of purge operation</returns>
    Task<DeadLetterPurgeResult> PurgeFromDeadLetterAsync(Guid entryId, string reason, string purgedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about dead letter queue for monitoring and alerting.
    /// </summary>
    /// <param name="since">Optional date to get statistics since</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Dead letter queue statistics</returns>
    Task<DeadLetterStatistics> GetDeadLetterStatisticsAsync(DateTime? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs cleanup of old dead letter entries that have been resolved.
    /// </summary>
    /// <param name="olderThan">Remove entries older than this date</param>
    /// <param name="onlyPurged">If true, only remove entries that have been purged</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Number of entries cleaned up</returns>
    Task<int> CleanupOldEntriesAsync(DateTime olderThan, bool onlyPurged = true,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an entry in the dead letter queue for permanently failed operations.
/// </summary>
public sealed class DeadLetterEntry
{
    /// <summary>
    /// Unique identifier for this dead letter entry.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// The file job that failed (if applicable).
    /// </summary>
    public FileJobId? JobId { get; }

    /// <summary>
    /// The specific target that failed (if applicable).
    /// </summary>
    public TargetId? TargetId { get; }

    /// <summary>
    /// Source file path.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Target file path (if applicable).
    /// </summary>
    public string? TargetPath { get; }

    /// <summary>
    /// Type of operation that failed permanently.
    /// </summary>
    public OperationType OperationType { get; }

    /// <summary>
    /// Reason for permanent failure.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Final exception details that caused the failure.
    /// </summary>
    public string ExceptionDetails { get; }

    /// <summary>
    /// Number of retry attempts made before permanent failure.
    /// </summary>
    public int AttemptCount { get; }

    /// <summary>
    /// When the item was added to the dead letter queue.
    /// </summary>
    public DateTime AddedAt { get; }

    /// <summary>
    /// Current status of this dead letter entry.
    /// </summary>
    public DeadLetterStatus Status { get; }

    /// <summary>
    /// When the entry was last modified.
    /// </summary>
    public DateTime LastModifiedAt { get; }

    /// <summary>
    /// Optional notes for manual investigation.
    /// </summary>
    public string? Notes { get; }

    public DeadLetterEntry(Guid id, FileJobId? jobId, TargetId? targetId, string sourcePath,
        string? targetPath, OperationType operationType, string reason, string exceptionDetails,
        int attemptCount, DateTime addedAt, DeadLetterStatus status = DeadLetterStatus.Active,
        DateTime? lastModifiedAt = null, string? notes = null)
    {
        Id = id;
        JobId = jobId;
        TargetId = targetId;
        SourcePath = ValidateString(sourcePath, nameof(sourcePath));
        TargetPath = targetPath;
        OperationType = operationType;
        Reason = ValidateString(reason, nameof(reason));
        ExceptionDetails = ValidateString(exceptionDetails, nameof(exceptionDetails));
        AttemptCount = ValidateAttemptCount(attemptCount);
        AddedAt = addedAt;
        Status = status;
        LastModifiedAt = lastModifiedAt ?? addedAt;
        Notes = notes;
    }

    private static string ValidateString(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} cannot be null, empty, or whitespace.", paramName);
        return value.Trim();
    }

    private static int ValidateAttemptCount(int attemptCount)
    {
        if (attemptCount < 0)
            throw new ArgumentOutOfRangeException(nameof(attemptCount), attemptCount, "Attempt count cannot be negative.");
        return attemptCount;
    }
}

/// <summary>
/// Status of a dead letter entry.
/// </summary>
public enum DeadLetterStatus
{
    /// <summary>
    /// Entry is active and awaiting manual review.
    /// </summary>
    Active,

    /// <summary>
    /// Entry has been requeued for retry.
    /// </summary>
    Requeued,

    /// <summary>
    /// Entry has been permanently purged.
    /// </summary>
    Purged,

    /// <summary>
    /// Entry is under investigation.
    /// </summary>
    UnderInvestigation,

    /// <summary>
    /// Entry has been resolved without requeue or purge.
    /// </summary>
    Resolved
}

/// <summary>
/// Filter criteria for querying dead letter entries.
/// </summary>
public sealed class DeadLetterFilter
{
    /// <summary>
    /// Filter by dead letter status.
    /// </summary>
    public DeadLetterStatus? Status { get; set; }

    /// <summary>
    /// Filter by operation type.
    /// </summary>
    public OperationType? OperationType { get; set; }

    /// <summary>
    /// Filter by entries added after this date.
    /// </summary>
    public DateTime? AddedAfter { get; set; }

    /// <summary>
    /// Filter by entries added before this date.
    /// </summary>
    public DateTime? AddedBefore { get; set; }

    /// <summary>
    /// Filter by source path pattern (supports wildcards).
    /// </summary>
    public string? SourcePathPattern { get; set; }

    /// <summary>
    /// Filter by reason containing this text.
    /// </summary>
    public string? ReasonContains { get; set; }

    /// <summary>
    /// Maximum number of entries to return.
    /// </summary>
    public int? Limit { get; set; }
}

/// <summary>
/// Result of requeuing an item from the dead letter queue.
/// </summary>
public sealed class DeadLetterRequeueResult
{
    /// <summary>
    /// Whether the requeue operation was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Message describing the result.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// ID of the dead letter entry that was requeued.
    /// </summary>
    public Guid EntryId { get; }

    /// <summary>
    /// Who performed the requeue operation.
    /// </summary>
    public string RequeuedBy { get; }

    /// <summary>
    /// When the requeue was performed.
    /// </summary>
    public DateTime RequeuedAt { get; }

    public DeadLetterRequeueResult(bool success, string message, Guid entryId, string requeuedBy, DateTime requeuedAt)
    {
        Success = success;
        Message = ValidateMessage(message);
        EntryId = entryId;
        RequeuedBy = ValidateString(requeuedBy, nameof(requeuedBy));
        RequeuedAt = requeuedAt;
    }

    public static DeadLetterRequeueResult Successful(Guid entryId, string message, string requeuedBy) =>
        new(true, message, entryId, requeuedBy, DateTime.UtcNow);

    public static DeadLetterRequeueResult Failed(Guid entryId, string message, string requeuedBy) =>
        new(false, message, entryId, requeuedBy, DateTime.UtcNow);

    private static string ValidateMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message cannot be null, empty, or whitespace.", nameof(message));
        return message.Trim();
    }

    private static string ValidateString(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} cannot be null, empty, or whitespace.", paramName);
        return value.Trim();
    }
}

/// <summary>
/// Result of purging an item from the dead letter queue.
/// </summary>
public sealed class DeadLetterPurgeResult
{
    /// <summary>
    /// Whether the purge operation was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Message describing the result.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// ID of the dead letter entry that was purged.
    /// </summary>
    public Guid EntryId { get; }

    /// <summary>
    /// Who performed the purge operation.
    /// </summary>
    public string PurgedBy { get; }

    /// <summary>
    /// When the purge was performed.
    /// </summary>
    public DateTime PurgedAt { get; }

    public DeadLetterPurgeResult(bool success, string message, Guid entryId, string purgedBy, DateTime purgedAt)
    {
        Success = success;
        Message = ValidateMessage(message);
        EntryId = entryId;
        PurgedBy = ValidateString(purgedBy, nameof(purgedBy));
        PurgedAt = purgedAt;
    }

    public static DeadLetterPurgeResult Successful(Guid entryId, string message, string purgedBy) =>
        new(true, message, entryId, purgedBy, DateTime.UtcNow);

    public static DeadLetterPurgeResult Failed(Guid entryId, string message, string purgedBy) =>
        new(false, message, entryId, purgedBy, DateTime.UtcNow);

    private static string ValidateMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message cannot be null, empty, or whitespace.", nameof(message));
        return message.Trim();
    }

    private static string ValidateString(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} cannot be null, empty, or whitespace.", paramName);
        return value.Trim();
    }
}

/// <summary>
/// Statistics about the dead letter queue.
/// </summary>
public sealed class DeadLetterStatistics
{
    /// <summary>
    /// Total number of active dead letter entries.
    /// </summary>
    public int ActiveEntries { get; }

    /// <summary>
    /// Number of entries that have been requeued.
    /// </summary>
    public int RequeuedEntries { get; }

    /// <summary>
    /// Number of entries that have been purged.
    /// </summary>
    public int PurgedEntries { get; }

    /// <summary>
    /// Entries by operation type.
    /// </summary>
    public IReadOnlyDictionary<OperationType, int> EntriesByOperationType { get; }

    /// <summary>
    /// Most common failure reasons.
    /// </summary>
    public IReadOnlyDictionary<string, int> FailureReasonCounts { get; }

    /// <summary>
    /// Oldest active entry timestamp.
    /// </summary>
    public DateTime? OldestActiveEntry { get; }

    /// <summary>
    /// Most recent entry timestamp.
    /// </summary>
    public DateTime? MostRecentEntry { get; }

    /// <summary>
    /// When statistics were calculated.
    /// </summary>
    public DateTime CalculatedAt { get; }

    public DeadLetterStatistics(int activeEntries, int requeuedEntries, int purgedEntries,
        IDictionary<OperationType, int> entriesByOperationType, IDictionary<string, int> failureReasonCounts,
        DateTime? oldestActiveEntry, DateTime? mostRecentEntry, DateTime calculatedAt)
    {
        ActiveEntries = activeEntries;
        RequeuedEntries = requeuedEntries;
        PurgedEntries = purgedEntries;
        EntriesByOperationType = entriesByOperationType?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                                ?? new Dictionary<OperationType, int>().AsReadOnly();
        FailureReasonCounts = failureReasonCounts?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                             ?? new Dictionary<string, int>().AsReadOnly();
        OldestActiveEntry = oldestActiveEntry;
        MostRecentEntry = mostRecentEntry;
        CalculatedAt = calculatedAt;
    }
}