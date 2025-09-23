using Forker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Exponential backoff retry policy with jitter for medical imaging file operations.
/// Implements Invariant I6 (MaxAttempts → FAILED_PERMANENT) and I13 (non-decreasing backoff).
/// Designed for 500MB-20GB file operations where transient failures are common.
/// </summary>
public sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly ILogger<ExponentialBackoffRetryPolicy> _logger;
    private readonly Random _random;

    /// <summary>
    /// Configuration for the retry policy.
    /// </summary>
    public ExponentialBackoffRetryPolicyOptions Options { get; }

    public int MaxAttempts => Options.MaxAttempts;

    public ExponentialBackoffRetryPolicy(
        ExponentialBackoffRetryPolicyOptions options,
        ILogger<ExponentialBackoffRetryPolicy> logger)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _random = new Random();

        ValidateOptions(options);
    }

    public RetryDecision ShouldRetry(int attemptNumber, Exception exception, OperationType operationType)
    {
        if (attemptNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), attemptNumber, "Attempt number must be >= 1");

        ArgumentNullException.ThrowIfNull(exception);

        _logger.LogDebug("Evaluating retry decision for attempt {AttemptNumber}/{MaxAttempts}, " +
                        "operation {OperationType}, exception {ExceptionType}",
            attemptNumber, MaxAttempts, operationType, exception.GetType().Name);

        // Check if max attempts reached (Invariant I6)
        if (attemptNumber >= MaxAttempts)
        {
            var reason = $"Maximum retry attempts ({MaxAttempts}) reached for {operationType}";
            _logger.LogWarning("Max attempts reached: {Reason}", reason);
            return RetryDecision.MaxAttemptsReached(reason);
        }

        // Classify the failure type
        var failureCategory = FailureClassifier.ClassifyFailure(exception, operationType);

        switch (failureCategory)
        {
            case FailureCategory.TransientFailure:
                var delay = CalculateDelay(attemptNumber, operationType);
                var retryReason = $"Transient {operationType} failure, retrying in {delay.TotalSeconds:F1}s: {exception.Message}";
                _logger.LogInformation("Scheduling retry: {Reason}", retryReason);
                return RetryDecision.Retry(delay, retryReason);

            case FailureCategory.PermanentFailure:
                var permanentReason = $"Permanent {operationType} failure: {exception.Message}";
                _logger.LogError("Permanent failure detected: {Reason}", permanentReason);
                return RetryDecision.PermanentFailure(permanentReason);

            case FailureCategory.IntegrityFailure:
                var integrityReason = $"Data integrity failure for {operationType}: {exception.Message}";
                _logger.LogError("Data integrity failure - no retry: {Reason}", integrityReason);
                return RetryDecision.PermanentFailure(integrityReason);

            case FailureCategory.ConfigurationError:
                var configReason = $"Configuration error for {operationType}: {exception.Message}";
                _logger.LogError("Configuration error - no retry: {Reason}", configReason);
                return RetryDecision.PermanentFailure(configReason);

            case FailureCategory.UnknownFailure:
            default:
                // For unknown failures, be conservative and retry with limited attempts
                if (attemptNumber < Math.Min(3, MaxAttempts))
                {
                    var unknownDelay = CalculateDelay(attemptNumber, operationType);
                    var unknownRetryReason = $"Unknown {operationType} failure, limited retry in {unknownDelay.TotalSeconds:F1}s: {exception.Message}";
                    _logger.LogWarning("Unknown failure - limited retry: {Reason}", unknownRetryReason);
                    return RetryDecision.Retry(unknownDelay, unknownRetryReason);
                }
                else
                {
                    var unknownPermanentReason = $"Unknown {operationType} failure after limited retries: {exception.Message}";
                    _logger.LogError("Unknown failure - giving up: {Reason}", unknownPermanentReason);
                    return RetryDecision.PermanentFailure(unknownPermanentReason);
                }
        }
    }

    public TimeSpan CalculateDelay(int attemptNumber, OperationType operationType)
    {
        if (attemptNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), attemptNumber, "Attempt number must be >= 1");

        // Get base delay for operation type
        var baseDelay = GetBaseDelayForOperation(operationType);

        // Calculate exponential backoff: baseDelay * (backoffMultiplier ^ (attemptNumber - 1))
        var exponentialDelay = TimeSpan.FromMilliseconds(
            baseDelay.TotalMilliseconds * Math.Pow(Options.BackoffMultiplier, attemptNumber - 1));

        // Apply maximum delay cap
        if (exponentialDelay > Options.MaxDelay)
        {
            exponentialDelay = Options.MaxDelay;
        }

        // Add jitter to avoid thundering herd (±25% randomization)
        var jitterRange = exponentialDelay.TotalMilliseconds * Options.JitterFactor;
        var jitter = (_random.NextDouble() - 0.5) * 2 * jitterRange; // -jitterRange to +jitterRange
        var finalDelayMs = Math.Max(0, exponentialDelay.TotalMilliseconds + jitter);

        var finalDelay = TimeSpan.FromMilliseconds(finalDelayMs);

        _logger.LogDebug("Calculated retry delay for {OperationType} attempt {AttemptNumber}: " +
                        "base={BaseDelayMs}ms, exponential={ExponentialDelayMs}ms, " +
                        "jitter={JitterMs}ms, final={FinalDelayMs}ms",
            operationType, attemptNumber,
            baseDelay.TotalMilliseconds, exponentialDelay.TotalMilliseconds,
            jitter, finalDelay.TotalMilliseconds);

        return finalDelay;
    }

    public bool IsRetryableException(Exception exception, OperationType operationType)
    {
        var category = FailureClassifier.ClassifyFailure(exception, operationType);
        return category == FailureCategory.TransientFailure ||
               category == FailureCategory.UnknownFailure;
    }

    private TimeSpan GetBaseDelayForOperation(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.FileCopy => Options.FileCopyBaseDelay,
            OperationType.FileVerification => Options.FileVerificationBaseDelay,
            OperationType.FileDiscovery => Options.FileDiscoveryBaseDelay,
            OperationType.FileStabilityCheck => Options.FileStabilityCheckBaseDelay,
            OperationType.DatabaseOperation => Options.DatabaseOperationBaseDelay,
            OperationType.FileSystemOperation => Options.FileSystemOperationBaseDelay,
            _ => Options.DefaultBaseDelay
        };
    }

    private static void ValidateOptions(ExponentialBackoffRetryPolicyOptions options)
    {
        if (options.MaxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxAttempts,
                "Max attempts must be >= 1");

        if (options.BackoffMultiplier <= 1.0)
            throw new ArgumentOutOfRangeException(nameof(options), options.BackoffMultiplier,
                "Backoff multiplier must be > 1.0");

        if (options.JitterFactor < 0 || options.JitterFactor > 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.JitterFactor,
                "Jitter factor must be between 0 and 1");

        if (options.MaxDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxDelay,
                "Max delay must be > 0");

        var delays = new[]
        {
            options.DefaultBaseDelay, options.FileCopyBaseDelay, options.FileVerificationBaseDelay,
            options.FileDiscoveryBaseDelay, options.FileStabilityCheckBaseDelay,
            options.DatabaseOperationBaseDelay, options.FileSystemOperationBaseDelay
        };

        foreach (var delay in delays)
        {
            if (delay <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), delay, "Base delays must be > 0");
        }
    }
}

/// <summary>
/// Configuration options for exponential backoff retry policy.
/// Optimized for medical imaging file operations (500MB-20GB).
/// </summary>
public sealed class ExponentialBackoffRetryPolicyOptions
{
    /// <summary>
    /// Maximum number of retry attempts before marking as permanently failed.
    /// Default: 5 attempts (allows for substantial backoff while preventing infinite retries).
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Multiplier for exponential backoff calculation.
    /// Default: 2.0 (doubles delay each retry: 1s, 2s, 4s, 8s, 16s).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum delay between retry attempts.
    /// Default: 5 minutes (prevents excessive delays for large files).
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Jitter factor to add randomness and prevent thundering herd.
    /// Default: 0.25 (±25% randomization).
    /// </summary>
    public double JitterFactor { get; set; } = 0.25;

    /// <summary>
    /// Default base delay for operations without specific configuration.
    /// </summary>
    public TimeSpan DefaultBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Base delay for file copy operations (typically longer due to large file sizes).
    /// Default: 5 seconds (accounts for file system flushing, network latency).
    /// </summary>
    public TimeSpan FileCopyBaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Base delay for file verification operations.
    /// Default: 3 seconds (hash calculation may need I/O settling time).
    /// </summary>
    public TimeSpan FileVerificationBaseDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Base delay for file discovery operations.
    /// Default: 2 seconds (directory scanning may hit temporary locks).
    /// </summary>
    public TimeSpan FileDiscoveryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Base delay for file stability check operations.
    /// Default: 10 seconds (files may still be growing, need time to stabilize).
    /// </summary>
    public TimeSpan FileStabilityCheckBaseDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Base delay for database operations.
    /// Default: 1 second (SQLite operations typically fast, minimal backoff needed).
    /// </summary>
    public TimeSpan DatabaseOperationBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Base delay for file system operations (delete, move, etc.).
    /// Default: 2 seconds (may hit temporary file locks or antivirus scans).
    /// </summary>
    public TimeSpan FileSystemOperationBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Creates default options optimized for medical imaging workflows.
    /// </summary>
    public static ExponentialBackoffRetryPolicyOptions CreateDefault()
    {
        return new ExponentialBackoffRetryPolicyOptions();
    }

    /// <summary>
    /// Creates aggressive retry options for development/testing environments.
    /// </summary>
    public static ExponentialBackoffRetryPolicyOptions CreateAggressive()
    {
        return new ExponentialBackoffRetryPolicyOptions
        {
            MaxAttempts = 8,
            BackoffMultiplier = 1.5,
            MaxDelay = TimeSpan.FromMinutes(2),
            JitterFactor = 0.1,
            FileCopyBaseDelay = TimeSpan.FromSeconds(2),
            FileVerificationBaseDelay = TimeSpan.FromSeconds(1),
            FileDiscoveryBaseDelay = TimeSpan.FromSeconds(1),
            FileStabilityCheckBaseDelay = TimeSpan.FromSeconds(5),
            DatabaseOperationBaseDelay = TimeSpan.FromMilliseconds(500),
            FileSystemOperationBaseDelay = TimeSpan.FromSeconds(1)
        };
    }

    /// <summary>
    /// Creates conservative retry options for production environments.
    /// </summary>
    public static ExponentialBackoffRetryPolicyOptions CreateConservative()
    {
        return new ExponentialBackoffRetryPolicyOptions
        {
            MaxAttempts = 3,
            BackoffMultiplier = 3.0,
            MaxDelay = TimeSpan.FromMinutes(10),
            JitterFactor = 0.5,
            FileCopyBaseDelay = TimeSpan.FromSeconds(10),
            FileVerificationBaseDelay = TimeSpan.FromSeconds(5),
            FileDiscoveryBaseDelay = TimeSpan.FromSeconds(3),
            FileStabilityCheckBaseDelay = TimeSpan.FromSeconds(15),
            DatabaseOperationBaseDelay = TimeSpan.FromSeconds(2),
            FileSystemOperationBaseDelay = TimeSpan.FromSeconds(5)
        };
    }
}