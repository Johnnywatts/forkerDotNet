namespace Forker.Domain.Services;

/// <summary>
/// Interface for retry policies that determine retry behavior for failed operations.
/// Enforces Invariant I6: MaxAttempts → FAILED_PERMANENT and Invariant I13: Retry backoff non-decreasing.
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Maximum number of retry attempts before marking as permanently failed.
    /// Enforces Invariant I6: MaxAttempts → FAILED_PERMANENT.
    /// </summary>
    int MaxAttempts { get; }

    /// <summary>
    /// Determines if an operation should be retried based on the failure type and attempt count.
    /// </summary>
    /// <param name="attemptNumber">Current attempt number (1-based)</param>
    /// <param name="exception">The exception that caused the failure</param>
    /// <param name="operationType">Type of operation that failed (copy, verification, etc.)</param>
    /// <returns>Retry decision with delay information</returns>
    RetryDecision ShouldRetry(int attemptNumber, Exception exception, OperationType operationType);

    /// <summary>
    /// Calculates the delay before the next retry attempt.
    /// Enforces Invariant I13: Retry backoff non-decreasing.
    /// </summary>
    /// <param name="attemptNumber">Current attempt number (1-based)</param>
    /// <param name="operationType">Type of operation being retried</param>
    /// <returns>Delay before next retry attempt</returns>
    TimeSpan CalculateDelay(int attemptNumber, OperationType operationType);

    /// <summary>
    /// Determines if a specific exception represents a retryable failure.
    /// </summary>
    /// <param name="exception">The exception to evaluate</param>
    /// <param name="operationType">Type of operation that failed</param>
    /// <returns>True if the exception represents a transient failure that can be retried</returns>
    bool IsRetryableException(Exception exception, OperationType operationType);
}

/// <summary>
/// Decision result for retry operations.
/// </summary>
public sealed class RetryDecision
{
    /// <summary>
    /// Whether the operation should be retried.
    /// </summary>
    public bool ShouldRetry { get; }

    /// <summary>
    /// Delay before the next retry attempt.
    /// </summary>
    public TimeSpan Delay { get; }

    /// <summary>
    /// Reason for the retry decision.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Whether this failure should be treated as permanent.
    /// </summary>
    public bool IsPermanentFailure { get; }

    /// <summary>
    /// Creates a retry decision.
    /// </summary>
    public RetryDecision(bool shouldRetry, TimeSpan delay, string reason, bool isPermanentFailure = false)
    {
        ShouldRetry = shouldRetry;
        Delay = delay;
        Reason = ValidateReason(reason);
        IsPermanentFailure = isPermanentFailure;
    }

    /// <summary>
    /// Creates a decision to retry with the specified delay.
    /// </summary>
    public static RetryDecision Retry(TimeSpan delay, string reason) =>
        new(true, delay, reason, false);

    /// <summary>
    /// Creates a decision not to retry due to max attempts reached.
    /// </summary>
    public static RetryDecision MaxAttemptsReached(string reason) =>
        new(false, TimeSpan.Zero, reason, true);

    /// <summary>
    /// Creates a decision not to retry due to permanent failure.
    /// </summary>
    public static RetryDecision PermanentFailure(string reason) =>
        new(false, TimeSpan.Zero, reason, true);

    /// <summary>
    /// Creates a decision not to retry due to non-retryable exception.
    /// </summary>
    public static RetryDecision NonRetryable(string reason) =>
        new(false, TimeSpan.Zero, reason, false);

    private static string ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be null, empty, or whitespace.", nameof(reason));
        return reason.Trim();
    }
}

/// <summary>
/// Types of operations that can be retried.
/// </summary>
public enum OperationType
{
    /// <summary>
    /// File copy operation from source to target.
    /// </summary>
    FileCopy,

    /// <summary>
    /// File verification operation (hash calculation/comparison).
    /// </summary>
    FileVerification,

    /// <summary>
    /// File discovery operation (scanning directories).
    /// </summary>
    FileDiscovery,

    /// <summary>
    /// File stability check operation.
    /// </summary>
    FileStabilityCheck,

    /// <summary>
    /// Database operation (repository calls).
    /// </summary>
    DatabaseOperation,

    /// <summary>
    /// File system operation (delete, move, etc.).
    /// </summary>
    FileSystemOperation
}

/// <summary>
/// Categories of failure reasons for retry policy decisions.
/// </summary>
public enum FailureCategory
{
    /// <summary>
    /// Transient network or I/O failure that should be retried.
    /// </summary>
    TransientFailure,

    /// <summary>
    /// Permanent failure such as file not found, access denied, etc.
    /// </summary>
    PermanentFailure,

    /// <summary>
    /// Data integrity failure such as hash mismatch (should not be retried).
    /// </summary>
    IntegrityFailure,

    /// <summary>
    /// Configuration or system error that should not be retried.
    /// </summary>
    ConfigurationError,

    /// <summary>
    /// Unknown failure type requiring manual investigation.
    /// </summary>
    UnknownFailure
}

/// <summary>
/// Helper class for categorizing exceptions into failure types.
/// </summary>
public static class FailureClassifier
{
    /// <summary>
    /// Classifies an exception into a failure category for retry decisions.
    /// </summary>
    /// <param name="exception">The exception to classify</param>
    /// <param name="operationType">The operation type that failed</param>
    /// <returns>The failure category</returns>
    public static FailureCategory ClassifyFailure(Exception exception, OperationType operationType)
    {
        return exception switch
        {
            // Permanent access issues (must come before IOException since they inherit from it)
            UnauthorizedAccessException => FailureCategory.PermanentFailure,
            DirectoryNotFoundException => FailureCategory.PermanentFailure,
            FileNotFoundException => FailureCategory.PermanentFailure,
            PathTooLongException => FailureCategory.PermanentFailure,

            // Transient I/O failures (after specific exception types)
            IOException io when IsTransientIOException(io) => FailureCategory.TransientFailure,
            TimeoutException => FailureCategory.TransientFailure,
            TaskCanceledException => FailureCategory.TransientFailure,
            OperationCanceledException => FailureCategory.TransientFailure,

            // Configuration errors (ArgumentNullException must come before ArgumentException)
            ArgumentNullException => FailureCategory.ConfigurationError,
            ArgumentException => FailureCategory.ConfigurationError,
            InvalidOperationException => FailureCategory.ConfigurationError,

            // Data integrity (never retry)
            Domain.Exceptions.InvariantViolationException => FailureCategory.IntegrityFailure,

            // Default to unknown for manual investigation
            _ => FailureCategory.UnknownFailure
        };
    }

    /// <summary>
    /// Determines if an IOException represents a transient failure.
    /// </summary>
    private static bool IsTransientIOException(IOException ioException)
    {
        // Check for specific permanent I/O error conditions
        var message = ioException.Message.ToLowerInvariant();

        // These specific conditions are permanent failures
        if (message.Contains("access denied") ||
            message.Contains("file not found") ||
            message.Contains("directory not found") ||
            message.Contains("path not found") ||
            message.Contains("invalid path"))
        {
            return false;
        }

        // Default IOException instances are considered transient
        // (network issues, temporary locks, resource contention, etc.)
        return true;
    }
}