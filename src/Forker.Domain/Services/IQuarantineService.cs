namespace Forker.Domain.Services;

/// <summary>
/// Service responsible for quarantining files that fail verification.
/// Enforces invariants I5, I16 related to hash mismatch handling and quarantine management.
/// Medical imaging files require zero tolerance for hash mismatches.
/// </summary>
public interface IQuarantineService
{
    /// <summary>
    /// Quarantines a file job due to hash mismatch or corruption.
    /// Enforces Invariant I5: Hash mismatch => QUARANTINED.
    /// Enforces Invariant I16: Quarantined requires manual action.
    /// </summary>
    /// <param name="fileJob">The file job to quarantine</param>
    /// <param name="reason">Reason for quarantine (hash mismatch, corruption, etc.)</param>
    /// <param name="affectedTargets">Collection of target outcomes that failed verification</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Quarantine entry with detailed information</returns>
    Task<QuarantineEntry> QuarantineJobAsync(FileJob fileJob, string reason,
        IEnumerable<TargetOutcome> affectedTargets, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quarantines a specific target outcome due to verification failure.
    /// Used when only one target fails while others may succeed.
    /// </summary>
    /// <param name="targetOutcome">The target outcome to quarantine</param>
    /// <param name="reason">Reason for quarantine</param>
    /// <param name="verificationResult">Verification result that caused quarantine</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Quarantine entry for the specific target</returns>
    Task<QuarantineEntry> QuarantineTargetAsync(TargetOutcome targetOutcome, string reason,
        VerificationResult verificationResult, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all quarantined jobs for manual review.
    /// Supports filtering by quarantine reason, date range, etc.
    /// </summary>
    /// <param name="filter">Optional filter criteria</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Collection of quarantine entries matching filter</returns>
    Task<IReadOnlyList<QuarantineEntry>> GetQuarantinedJobsAsync(QuarantineFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually releases a job from quarantine for retry.
    /// Requires administrative action as per Invariant I16.
    /// </summary>
    /// <param name="quarantineEntryId">ID of the quarantine entry to release</param>
    /// <param name="releaseReason">Reason for manual release</param>
    /// <param name="releasedBy">Identity of the person/system releasing</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>True if successfully released, false if not found or already released</returns>
    Task<bool> ReleaseFromQuarantineAsync(Guid quarantineEntryId, string releaseReason, string releasedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes files and records for a quarantined job.
    /// Used for confirmed corrupted files that should not be retried.
    /// </summary>
    /// <param name="quarantineEntryId">ID of the quarantine entry to purge</param>
    /// <param name="purgeReason">Reason for permanent deletion</param>
    /// <param name="purgedBy">Identity of the person/system performing purge</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>True if successfully purged, false if not found</returns>
    Task<bool> PurgeQuarantinedJobAsync(Guid quarantineEntryId, string purgeReason, string purgedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets quarantine statistics for monitoring and reporting.
    /// </summary>
    /// <param name="since">Optional date to get statistics since</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Aggregate quarantine statistics</returns>
    Task<QuarantineStatistics> GetQuarantineStatisticsAsync(DateTime? since = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a quarantined file job with detailed failure information.
/// Immutable record for audit trail purposes.
/// </summary>
public sealed class QuarantineEntry
{
    /// <summary>
    /// Unique identifier for this quarantine entry.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// The file job that was quarantined.
    /// </summary>
    public FileJobId JobId { get; }

    /// <summary>
    /// Source file path of the quarantined job.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Expected hash of the source file.
    /// </summary>
    public string? ExpectedHash { get; }

    /// <summary>
    /// Reason for quarantine (hash mismatch, corruption, etc.).
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Collection of affected target outcomes with their verification failures.
    /// </summary>
    public IReadOnlyList<QuarantinedTarget> AffectedTargets { get; }

    /// <summary>
    /// When this job was quarantined.
    /// </summary>
    public DateTime QuarantinedAt { get; }

    /// <summary>
    /// System or user that initiated the quarantine.
    /// </summary>
    public string QuarantinedBy { get; }

    /// <summary>
    /// Current status of this quarantine entry.
    /// </summary>
    public QuarantineStatus Status { get; }

    /// <summary>
    /// When this entry was released from quarantine (if applicable).
    /// </summary>
    public DateTime? ReleasedAt { get; }

    /// <summary>
    /// Reason for release from quarantine (if applicable).
    /// </summary>
    public string? ReleaseReason { get; }

    /// <summary>
    /// System or user that released from quarantine (if applicable).
    /// </summary>
    public string? ReleasedBy { get; }

    public QuarantineEntry(Guid id, FileJobId jobId, string sourcePath, string? expectedHash,
        string reason, IEnumerable<QuarantinedTarget> affectedTargets,
        DateTime quarantinedAt, string quarantinedBy, QuarantineStatus status = QuarantineStatus.Active,
        DateTime? releasedAt = null, string? releaseReason = null, string? releasedBy = null)
    {
        Id = id;
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        SourcePath = ValidateString(sourcePath, nameof(sourcePath));
        ExpectedHash = expectedHash;
        Reason = ValidateString(reason, nameof(reason));
        AffectedTargets = affectedTargets?.ToList().AsReadOnly() ?? throw new ArgumentNullException(nameof(affectedTargets));
        QuarantinedAt = quarantinedAt;
        QuarantinedBy = ValidateString(quarantinedBy, nameof(quarantinedBy));
        Status = status;
        ReleasedAt = releasedAt;
        ReleaseReason = releaseReason;
        ReleasedBy = releasedBy;
    }

    private static string ValidateString(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} cannot be null, empty, or whitespace.", paramName);
        return value.Trim();
    }
}

/// <summary>
/// Represents a quarantined target with verification failure details.
/// </summary>
public sealed class QuarantinedTarget
{
    /// <summary>
    /// The target identifier that failed verification.
    /// </summary>
    public TargetId TargetId { get; }

    /// <summary>
    /// Path to the failed target file.
    /// </summary>
    public string? TargetPath { get; }

    /// <summary>
    /// Hash computed from the failed target file.
    /// </summary>
    public string? ComputedHash { get; }

    /// <summary>
    /// Expected hash that should have matched.
    /// </summary>
    public string ExpectedHash { get; }

    /// <summary>
    /// Specific error message for this target failure.
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// When this target was verified and failed.
    /// </summary>
    public DateTime FailedAt { get; }

    public QuarantinedTarget(TargetId targetId, string? targetPath, string? computedHash,
        string expectedHash, string errorMessage, DateTime failedAt)
    {
        TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
        TargetPath = targetPath;
        ComputedHash = computedHash;
        ExpectedHash = ValidateString(expectedHash, nameof(expectedHash));
        ErrorMessage = ValidateString(errorMessage, nameof(errorMessage));
        FailedAt = failedAt;
    }

    private static string ValidateString(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} cannot be null, empty, or whitespace.", paramName);
        return value.Trim();
    }
}

/// <summary>
/// Status of a quarantine entry.
/// </summary>
public enum QuarantineStatus
{
    /// <summary>
    /// Entry is active and awaiting manual review.
    /// </summary>
    Active,

    /// <summary>
    /// Entry has been released and job requeued for retry.
    /// </summary>
    Released,

    /// <summary>
    /// Entry has been permanently purged from the system.
    /// </summary>
    Purged
}

/// <summary>
/// Filter criteria for querying quarantined jobs.
/// </summary>
public sealed class QuarantineFilter
{
    /// <summary>
    /// Filter by quarantine status.
    /// </summary>
    public QuarantineStatus? Status { get; set; }

    /// <summary>
    /// Filter by jobs quarantined after this date.
    /// </summary>
    public DateTime? QuarantinedAfter { get; set; }

    /// <summary>
    /// Filter by jobs quarantined before this date.
    /// </summary>
    public DateTime? QuarantinedBefore { get; set; }

    /// <summary>
    /// Filter by source path pattern (supports wildcards).
    /// </summary>
    public string? SourcePathPattern { get; set; }

    /// <summary>
    /// Filter by quarantine reason (partial match).
    /// </summary>
    public string? ReasonContains { get; set; }

    /// <summary>
    /// Maximum number of entries to return.
    /// </summary>
    public int? Limit { get; set; }
}

/// <summary>
/// Aggregate statistics about quarantined jobs.
/// </summary>
public sealed class QuarantineStatistics
{
    /// <summary>
    /// Total number of active quarantine entries.
    /// </summary>
    public int ActiveCount { get; }

    /// <summary>
    /// Total number of released quarantine entries.
    /// </summary>
    public int ReleasedCount { get; }

    /// <summary>
    /// Total number of purged quarantine entries.
    /// </summary>
    public int PurgedCount { get; }

    /// <summary>
    /// Most common quarantine reasons with counts.
    /// </summary>
    public IReadOnlyDictionary<string, int> ReasonCounts { get; }

    /// <summary>
    /// Most recently quarantined entry timestamp.
    /// </summary>
    public DateTime? MostRecentQuarantineAt { get; }

    /// <summary>
    /// Oldest active quarantine entry timestamp.
    /// </summary>
    public DateTime? OldestActiveQuarantineAt { get; }

    public QuarantineStatistics(int activeCount, int releasedCount, int purgedCount,
        IDictionary<string, int> reasonCounts, DateTime? mostRecentQuarantineAt, DateTime? oldestActiveQuarantineAt)
    {
        ActiveCount = activeCount;
        ReleasedCount = releasedCount;
        PurgedCount = purgedCount;
        ReasonCounts = reasonCounts?.ToList().ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                      ?? new Dictionary<string, int>().AsReadOnly();
        MostRecentQuarantineAt = mostRecentQuarantineAt;
        OldestActiveQuarantineAt = oldestActiveQuarantineAt;
    }
}