using Forker.Domain.Exceptions;

namespace Forker.Domain.Services;

/// <summary>
/// Orchestrator service responsible for coordinating verification across multiple targets.
/// Enforces invariants I2, I11, I19 related to multi-target verification logic.
/// </summary>
public interface IVerificationOrchestrator
{
    /// <summary>
    /// Orchestrates verification for a complete file job with all its targets.
    /// Enforces Invariant I2: Job VERIFIED only if all targets VERIFIED & hashes match.
    /// Enforces Invariant I11: Partial not VERIFIED.
    /// Enforces Invariant I19: Independent target progress.
    /// </summary>
    /// <param name="fileJob">The file job to verify</param>
    /// <param name="targetOutcomes">All target outcomes for this job</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Job verification result with detailed target information</returns>
    Task<JobVerificationResult> VerifyJobAsync(FileJob fileJob,
        IReadOnlyList<TargetOutcome> targetOutcomes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies individual targets in parallel while maintaining state consistency.
    /// Updates target outcome states based on verification results.
    /// </summary>
    /// <param name="targetOutcomes">Collection of target outcomes to verify</param>
    /// <param name="expectedHash">Expected SHA-256 hash from source</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Collection of target verification results</returns>
    Task<IReadOnlyList<TargetVerificationResult>> VerifyTargetsAsync(
        IReadOnlyList<TargetOutcome> targetOutcomes,
        string expectedHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles post-verification actions based on results.
    /// Coordinates quarantine actions, job state transitions, and metrics updates.
    /// </summary>
    /// <param name="fileJob">The file job that was verified</param>
    /// <param name="verificationResult">Results from job verification</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Post-verification action results</returns>
    Task<PostVerificationResult> HandleVerificationResultAsync(FileJob fileJob,
        JobVerificationResult verificationResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules verification for jobs that have completed copying.
    /// Monitors for jobs in PARTIAL state and queues them for verification.
    /// </summary>
    /// <param name="maxConcurrentVerifications">Maximum number of concurrent verification operations</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Number of verification operations scheduled</returns>
    Task<int> SchedulePendingVerificationsAsync(int maxConcurrentVerifications = 5,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of verifying an entire file job with all its targets.
/// </summary>
public sealed class JobVerificationResult
{
    /// <summary>
    /// The file job that was verified.
    /// </summary>
    public FileJobId JobId { get; }

    /// <summary>
    /// Overall verification status for the job.
    /// </summary>
    public JobVerificationStatus Status { get; }

    /// <summary>
    /// Individual verification results for each target.
    /// </summary>
    public IReadOnlyList<TargetVerificationResult> TargetResults { get; }

    /// <summary>
    /// Expected hash from the source file.
    /// </summary>
    public string ExpectedHash { get; }

    /// <summary>
    /// Total time taken to verify all targets.
    /// </summary>
    public TimeSpan TotalVerificationTime { get; }

    /// <summary>
    /// Number of targets that successfully verified.
    /// </summary>
    public int SuccessfulTargetCount { get; }

    /// <summary>
    /// Number of targets that failed verification.
    /// </summary>
    public int FailedTargetCount { get; }

    /// <summary>
    /// Targets that require quarantine due to hash mismatch.
    /// </summary>
    public IReadOnlyList<TargetVerificationResult> TargetsRequiringQuarantine { get; }

    /// <summary>
    /// Optional error message if job-level verification failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// When verification started.
    /// </summary>
    public DateTime VerificationStartedAt { get; }

    /// <summary>
    /// When verification completed.
    /// </summary>
    public DateTime VerificationCompletedAt { get; }

    public JobVerificationResult(FileJobId jobId, string expectedHash,
        IEnumerable<TargetVerificationResult> targetResults,
        DateTime verificationStartedAt, DateTime verificationCompletedAt,
        string? errorMessage = null)
    {
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        ExpectedHash = ValidateHash(expectedHash);
        TargetResults = targetResults?.ToList().AsReadOnly() ?? throw new ArgumentNullException(nameof(targetResults));
        VerificationStartedAt = verificationStartedAt;
        VerificationCompletedAt = verificationCompletedAt;
        TotalVerificationTime = verificationCompletedAt - verificationStartedAt;
        ErrorMessage = errorMessage;

        SuccessfulTargetCount = TargetResults.Count(r => r.VerificationResult.IsMatch && r.VerificationResult.VerificationSucceeded);
        FailedTargetCount = TargetResults.Count - SuccessfulTargetCount;
        TargetsRequiringQuarantine = TargetResults.Where(r => r.RequiresQuarantine).ToList().AsReadOnly();

        // Determine overall status based on target results
        Status = DetermineJobStatus();
    }

    private JobVerificationStatus DetermineJobStatus()
    {
        if (!string.IsNullOrEmpty(ErrorMessage))
            return JobVerificationStatus.Failed;

        var allTargetsVerified = TargetResults.All(r => r.VerificationResult.VerificationSucceeded);
        if (!allTargetsVerified)
            return JobVerificationStatus.Failed;

        var allHashesMatch = TargetResults.All(r => r.VerificationResult.IsMatch);
        if (!allHashesMatch)
            return JobVerificationStatus.QuarantineRequired;

        return JobVerificationStatus.AllTargetsVerified;
    }

    private static string ValidateHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("Hash cannot be null, empty, or whitespace.", nameof(hash));
        return hash.Trim();
    }
}

/// <summary>
/// Result of verifying a specific target outcome.
/// </summary>
public sealed class TargetVerificationResult
{
    /// <summary>
    /// The target outcome that was verified.
    /// </summary>
    public TargetId TargetId { get; }

    /// <summary>
    /// Detailed verification result from the verification service.
    /// </summary>
    public VerificationResult VerificationResult { get; }

    /// <summary>
    /// Updated state of the target after verification.
    /// </summary>
    public TargetCopyState UpdatedState { get; }

    /// <summary>
    /// True if this target requires quarantine due to verification failure.
    /// </summary>
    public bool RequiresQuarantine { get; }

    /// <summary>
    /// Optional error message specific to this target.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Action that should be taken for this target based on verification result.
    /// </summary>
    public TargetVerificationAction RecommendedAction { get; }

    public TargetVerificationResult(TargetId targetId, VerificationResult verificationResult,
        TargetCopyState updatedState, TargetVerificationAction recommendedAction,
        string? errorMessage = null)
    {
        TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
        VerificationResult = verificationResult ?? throw new ArgumentNullException(nameof(verificationResult));
        UpdatedState = updatedState;
        RecommendedAction = recommendedAction;
        ErrorMessage = errorMessage;

        // Determine if quarantine is required
        RequiresQuarantine = DetermineQuarantineRequirement();
    }

    private bool DetermineQuarantineRequirement()
    {
        // Quarantine required if verification succeeded but hash doesn't match
        return VerificationResult.VerificationSucceeded && !VerificationResult.IsMatch;
    }
}

/// <summary>
/// Overall verification status for a complete file job.
/// </summary>
public enum JobVerificationStatus
{
    /// <summary>
    /// All targets successfully verified with matching hashes.
    /// Job can transition to VERIFIED state.
    /// </summary>
    AllTargetsVerified,

    /// <summary>
    /// Some targets completed but job remains in PARTIAL state.
    /// Additional targets still need verification.
    /// </summary>
    PartiallyVerified,

    /// <summary>
    /// One or more targets failed verification due to hash mismatch.
    /// Job should be quarantined for manual review.
    /// </summary>
    QuarantineRequired,

    /// <summary>
    /// Verification process failed due to I/O errors or system issues.
    /// Job should be marked as failed and potentially retried.
    /// </summary>
    Failed
}

/// <summary>
/// Recommended action for a target based on its verification result.
/// </summary>
public enum TargetVerificationAction
{
    /// <summary>
    /// Target verified successfully, mark as VERIFIED.
    /// </summary>
    MarkVerified,

    /// <summary>
    /// Target verification failed due to I/O error, retry verification.
    /// </summary>
    RetryVerification,

    /// <summary>
    /// Target hash mismatch, quarantine for manual review.
    /// </summary>
    QuarantineTarget,

    /// <summary>
    /// Target permanently failed, mark as FAILED_PERMANENT.
    /// </summary>
    MarkPermanentlyFailed,

    /// <summary>
    /// Target has transient error, mark as FAILED_RETRYABLE for copy retry.
    /// </summary>
    MarkRetryableFailed
}

/// <summary>
/// Result of post-verification processing actions.
/// </summary>
public sealed class PostVerificationResult
{
    /// <summary>
    /// The file job that was processed.
    /// </summary>
    public FileJobId JobId { get; }

    /// <summary>
    /// Final state of the job after post-verification processing.
    /// </summary>
    public JobState FinalJobState { get; }

    /// <summary>
    /// Number of targets that were successfully verified.
    /// </summary>
    public int VerifiedTargetCount { get; }

    /// <summary>
    /// Number of targets that were quarantined.
    /// </summary>
    public int QuarantinedTargetCount { get; }

    /// <summary>
    /// Number of targets that failed and will be retried.
    /// </summary>
    public int FailedRetryableTargetCount { get; }

    /// <summary>
    /// Number of targets that failed permanently.
    /// </summary>
    public int FailedPermanentTargetCount { get; }

    /// <summary>
    /// Quarantine entries created during post-processing (if any).
    /// </summary>
    public IReadOnlyList<QuarantineEntry>? QuarantineEntries { get; }

    /// <summary>
    /// Whether any metrics were updated as part of post-processing.
    /// </summary>
    public bool MetricsUpdated { get; }

    /// <summary>
    /// Optional summary message describing actions taken.
    /// </summary>
    public string? Summary { get; }

    public PostVerificationResult(FileJobId jobId, JobState finalJobState,
        int verifiedTargetCount, int quarantinedTargetCount,
        int failedRetryableTargetCount, int failedPermanentTargetCount,
        IEnumerable<QuarantineEntry>? quarantineEntries = null,
        bool metricsUpdated = false, string? summary = null)
    {
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        FinalJobState = finalJobState;
        VerifiedTargetCount = verifiedTargetCount;
        QuarantinedTargetCount = quarantinedTargetCount;
        FailedRetryableTargetCount = failedRetryableTargetCount;
        FailedPermanentTargetCount = failedPermanentTargetCount;
        QuarantineEntries = quarantineEntries?.ToList().AsReadOnly();
        MetricsUpdated = metricsUpdated;
        Summary = summary;
    }
}