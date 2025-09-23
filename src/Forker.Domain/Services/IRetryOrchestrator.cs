using Forker.Domain.Repositories;

namespace Forker.Domain.Services;

/// <summary>
/// Service responsible for orchestrating retry operations across file jobs and target outcomes.
/// Enforces Invariant I6 (MaxAttempts → FAILED_PERMANENT) and coordinates with retry policies.
/// </summary>
public interface IRetryOrchestrator
{
    /// <summary>
    /// Processes all retryable failed operations and schedules them for retry based on retry policy.
    /// This is typically called by a background service on a regular interval.
    /// </summary>
    /// <param name="maxConcurrentRetries">Maximum number of concurrent retry operations</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Summary of retry processing results</returns>
    Task<RetryProcessingResult> ProcessRetryableFailuresAsync(int maxConcurrentRetries = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a specific target outcome for retry eligibility and schedules retry if appropriate.
    /// </summary>
    /// <param name="targetOutcome">The target outcome to evaluate for retry</param>
    /// <param name="lastException">The exception that caused the failure</param>
    /// <param name="operationType">The type of operation that failed</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Result of retry evaluation and any actions taken</returns>
    Task<RetryEvaluationResult> EvaluateAndScheduleRetryAsync(TargetOutcome targetOutcome,
        Exception lastException, OperationType operationType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a target outcome as permanently failed after exhausting retry attempts.
    /// Enforces Invariant I6: MaxAttempts → FAILED_PERMANENT.
    /// </summary>
    /// <param name="targetOutcome">The target outcome to mark as permanently failed</param>
    /// <param name="reason">Reason for permanent failure</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Task representing the async operation</returns>
    Task MarkAsPermanentlyFailedAsync(TargetOutcome targetOutcome, string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets retry statistics for monitoring and alerting.
    /// </summary>
    /// <param name="since">Optional date to get statistics since</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Aggregate retry statistics</returns>
    Task<RetryStatistics> GetRetryStatisticsAsync(DateTime? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually triggers retry for a specific target outcome.
    /// Used for administrative override of retry policies.
    /// </summary>
    /// <param name="targetOutcome">The target outcome to retry</param>
    /// <param name="operationType">The type of operation to retry</param>
    /// <param name="reason">Reason for manual retry</param>
    /// <param name="triggeredBy">Identity of person/system triggering retry</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Result of manual retry attempt</returns>
    Task<ManualRetryResult> ManualRetryAsync(TargetOutcome targetOutcome, OperationType operationType,
        string reason, string triggeredBy, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of processing retryable failures in a batch.
/// </summary>
public sealed class RetryProcessingResult
{
    /// <summary>
    /// Number of target outcomes evaluated for retry.
    /// </summary>
    public int EvaluatedCount { get; }

    /// <summary>
    /// Number of retries scheduled.
    /// </summary>
    public int RetriesScheduled { get; }

    /// <summary>
    /// Number of targets marked as permanently failed.
    /// </summary>
    public int PermanentFailures { get; }

    /// <summary>
    /// Number of retries that were skipped (too soon, etc.).
    /// </summary>
    public int RetriesSkipped { get; }

    /// <summary>
    /// Individual retry evaluation results.
    /// </summary>
    public IReadOnlyList<RetryEvaluationResult> IndividualResults { get; }

    /// <summary>
    /// Total processing time for the batch.
    /// </summary>
    public TimeSpan ProcessingTime { get; }

    /// <summary>
    /// Any errors encountered during processing.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    public RetryProcessingResult(int evaluatedCount, int retriesScheduled, int permanentFailures,
        int retriesSkipped, IEnumerable<RetryEvaluationResult> individualResults,
        TimeSpan processingTime, IEnumerable<string>? errors = null)
    {
        EvaluatedCount = evaluatedCount;
        RetriesScheduled = retriesScheduled;
        PermanentFailures = permanentFailures;
        RetriesSkipped = retriesSkipped;
        IndividualResults = individualResults?.ToList().AsReadOnly() ?? Array.Empty<RetryEvaluationResult>().ToList().AsReadOnly();
        ProcessingTime = processingTime;
        Errors = errors?.ToList().AsReadOnly() ?? Array.Empty<string>().ToList().AsReadOnly();
    }
}

/// <summary>
/// Result of evaluating a single target outcome for retry.
/// </summary>
public sealed class RetryEvaluationResult
{
    /// <summary>
    /// The target outcome that was evaluated.
    /// </summary>
    public TargetId TargetId { get; }

    /// <summary>
    /// The job this target belongs to.
    /// </summary>
    public FileJobId JobId { get; }

    /// <summary>
    /// The retry decision made by the retry policy.
    /// </summary>
    public RetryDecision Decision { get; }

    /// <summary>
    /// Action taken based on the retry decision.
    /// </summary>
    public RetryAction ActionTaken { get; }

    /// <summary>
    /// When the retry is scheduled to execute (if applicable).
    /// </summary>
    public DateTime? ScheduledRetryAt { get; }

    /// <summary>
    /// Optional error message if evaluation failed.
    /// </summary>
    public string? ErrorMessage { get; }

    public RetryEvaluationResult(TargetId targetId, FileJobId jobId, RetryDecision decision,
        RetryAction actionTaken, DateTime? scheduledRetryAt = null, string? errorMessage = null)
    {
        TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
        ActionTaken = actionTaken;
        ScheduledRetryAt = scheduledRetryAt;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Actions that can be taken based on retry evaluation.
/// </summary>
public enum RetryAction
{
    /// <summary>
    /// Retry was scheduled for future execution.
    /// </summary>
    RetryScheduled,

    /// <summary>
    /// Target was marked as permanently failed.
    /// </summary>
    MarkedPermanentlyFailed,

    /// <summary>
    /// Retry was skipped (too soon, already processing, etc.).
    /// </summary>
    RetrySkipped,

    /// <summary>
    /// No action taken (target not eligible for retry).
    /// </summary>
    NoActionRequired,

    /// <summary>
    /// Error occurred during evaluation.
    /// </summary>
    EvaluationError
}

/// <summary>
/// Aggregate statistics about retry operations.
/// </summary>
public sealed class RetryStatistics
{
    /// <summary>
    /// Total number of retries attempted.
    /// </summary>
    public int TotalRetriesAttempted { get; }

    /// <summary>
    /// Number of retries that succeeded.
    /// </summary>
    public int SuccessfulRetries { get; }

    /// <summary>
    /// Number of targets marked as permanently failed after max attempts.
    /// </summary>
    public int PermanentFailuresAfterMaxAttempts { get; }

    /// <summary>
    /// Average number of attempts before success.
    /// </summary>
    public double AverageAttemptsToSuccess { get; }

    /// <summary>
    /// Retry counts by operation type.
    /// </summary>
    public IReadOnlyDictionary<OperationType, int> RetriesByOperationType { get; }

    /// <summary>
    /// Most common failure reasons.
    /// </summary>
    public IReadOnlyDictionary<string, int> FailureReasonCounts { get; }

    /// <summary>
    /// When statistics were calculated.
    /// </summary>
    public DateTime CalculatedAt { get; }

    public RetryStatistics(int totalRetriesAttempted, int successfulRetries,
        int permanentFailuresAfterMaxAttempts, double averageAttemptsToSuccess,
        IDictionary<OperationType, int> retriesByOperationType,
        IDictionary<string, int> failureReasonCounts, DateTime calculatedAt)
    {
        TotalRetriesAttempted = totalRetriesAttempted;
        SuccessfulRetries = successfulRetries;
        PermanentFailuresAfterMaxAttempts = permanentFailuresAfterMaxAttempts;
        AverageAttemptsToSuccess = averageAttemptsToSuccess;
        RetriesByOperationType = retriesByOperationType?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                                ?? new Dictionary<OperationType, int>().AsReadOnly();
        FailureReasonCounts = failureReasonCounts?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                             ?? new Dictionary<string, int>().AsReadOnly();
        CalculatedAt = calculatedAt;
    }
}

/// <summary>
/// Result of a manual retry operation.
/// </summary>
public sealed class ManualRetryResult
{
    /// <summary>
    /// Whether the manual retry was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Message describing the result.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// The target that was retried.
    /// </summary>
    public TargetId TargetId { get; }

    /// <summary>
    /// Who triggered the manual retry.
    /// </summary>
    public string TriggeredBy { get; }

    /// <summary>
    /// When the manual retry was triggered.
    /// </summary>
    public DateTime TriggeredAt { get; }

    public ManualRetryResult(bool success, string message, TargetId targetId, string triggeredBy, DateTime triggeredAt)
    {
        Success = success;
        Message = ValidateMessage(message);
        TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
        TriggeredBy = ValidateTriggeredBy(triggeredBy);
        TriggeredAt = triggeredAt;
    }

    public static ManualRetryResult Successful(TargetId targetId, string message, string triggeredBy) =>
        new(true, message, targetId, triggeredBy, DateTime.UtcNow);

    public static ManualRetryResult Failed(TargetId targetId, string message, string triggeredBy) =>
        new(false, message, targetId, triggeredBy, DateTime.UtcNow);

    private static string ValidateMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message cannot be null, empty, or whitespace.", nameof(message));
        return message.Trim();
    }

    private static string ValidateTriggeredBy(string triggeredBy)
    {
        if (string.IsNullOrWhiteSpace(triggeredBy))
            throw new ArgumentException("TriggeredBy cannot be null, empty, or whitespace.", nameof(triggeredBy));
        return triggeredBy.Trim();
    }
}