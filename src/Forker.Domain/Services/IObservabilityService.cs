namespace Forker.Domain.Services;

/// <summary>
/// Core observability service for managing correlation IDs, metrics, and tracing
/// across medical imaging file processing workflows.
/// </summary>
public interface IObservabilityService
{
    /// <summary>
    /// Gets the current correlation ID for the active operation context.
    /// </summary>
    string CurrentCorrelationId { get; }

    /// <summary>
    /// Creates a new operation scope with correlation ID and tracing context.
    /// </summary>
    /// <param name="operationName">Name of the operation being started</param>
    /// <param name="operationType">Type of operation for categorization</param>
    /// <param name="parentCorrelationId">Optional parent correlation ID for nested operations</param>
    /// <param name="metadata">Additional metadata for the operation</param>
    /// <returns>Disposable operation scope that tracks timing and completion</returns>
    IOperationScope StartOperation(string operationName, OperationType operationType,
        string? parentCorrelationId = null, IDictionary<string, object>? metadata = null);

    /// <summary>
    /// Records a metric value for monitoring and alerting.
    /// </summary>
    /// <param name="metricName">Name of the metric</param>
    /// <param name="value">Metric value</param>
    /// <param name="tags">Optional tags for metric categorization</param>
    void RecordMetric(string metricName, double value, IDictionary<string, string>? tags = null);

    /// <summary>
    /// Records a counter increment for event tracking.
    /// </summary>
    /// <param name="counterName">Name of the counter</param>
    /// <param name="increment">Amount to increment (default: 1)</param>
    /// <param name="tags">Optional tags for counter categorization</param>
    void IncrementCounter(string counterName, long increment = 1, IDictionary<string, string>? tags = null);

    /// <summary>
    /// Records a histogram value for distribution tracking.
    /// </summary>
    /// <param name="histogramName">Name of the histogram</param>
    /// <param name="value">Value to record</param>
    /// <param name="tags">Optional tags for histogram categorization</param>
    void RecordHistogram(string histogramName, double value, IDictionary<string, string>? tags = null);

    /// <summary>
    /// Adds structured logging context for the current operation.
    /// </summary>
    /// <param name="key">Context key</param>
    /// <param name="value">Context value</param>
    void AddLogContext(string key, object value);

    /// <summary>
    /// Removes structured logging context.
    /// </summary>
    /// <param name="key">Context key to remove</param>
    void RemoveLogContext(string key);

    /// <summary>
    /// Gets current observability statistics for monitoring.
    /// </summary>
    /// <param name="since">Optional time window start</param>
    /// <returns>Observability statistics</returns>
    Task<ObservabilityStatistics> GetStatisticsAsync(DateTime? since = null);
}

/// <summary>
/// Represents an operation scope with timing, tracing, and correlation tracking.
/// Must be disposed to complete the operation tracking.
/// </summary>
public interface IOperationScope : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Unique correlation ID for this operation.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// Name of the operation being tracked.
    /// </summary>
    string OperationName { get; }

    /// <summary>
    /// Type of operation for categorization.
    /// </summary>
    OperationType OperationType { get; }

    /// <summary>
    /// When the operation started.
    /// </summary>
    DateTime StartTime { get; }

    /// <summary>
    /// Parent correlation ID if this is a nested operation.
    /// </summary>
    string? ParentCorrelationId { get; }

    /// <summary>
    /// Additional metadata associated with this operation.
    /// </summary>
    IReadOnlyDictionary<string, object> Metadata { get; }

    /// <summary>
    /// Current elapsed time for the operation.
    /// </summary>
    TimeSpan Elapsed { get; }

    /// <summary>
    /// Adds metadata to the operation scope.
    /// </summary>
    /// <param name="key">Metadata key</param>
    /// <param name="value">Metadata value</param>
    void AddMetadata(string key, object value);

    /// <summary>
    /// Records a checkpoint in the operation timeline.
    /// </summary>
    /// <param name="checkpointName">Name of the checkpoint</param>
    /// <param name="metadata">Optional checkpoint metadata</param>
    void RecordCheckpoint(string checkpointName, IDictionary<string, object>? metadata = null);

    /// <summary>
    /// Marks the operation as completed successfully.
    /// </summary>
    /// <param name="result">Optional operation result</param>
    void MarkSuccess(object? result = null);

    /// <summary>
    /// Marks the operation as failed.
    /// </summary>
    /// <param name="exception">Exception that caused the failure</param>
    /// <param name="additionalContext">Additional failure context</param>
    void MarkFailure(Exception exception, IDictionary<string, object>? additionalContext = null);

    /// <summary>
    /// Records progress for long-running operations.
    /// </summary>
    /// <param name="percentComplete">Percentage complete (0.0 to 1.0)</param>
    /// <param name="currentStep">Description of current step</param>
    /// <param name="estimatedTimeRemaining">Optional estimated time remaining</param>
    void RecordProgress(double percentComplete, string currentStep, TimeSpan? estimatedTimeRemaining = null);
}

/// <summary>
/// Checkpoint recorded during an operation execution.
/// </summary>
public sealed class OperationCheckpoint
{
    /// <summary>
    /// Name of the checkpoint.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// When the checkpoint was recorded.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Elapsed time from operation start to this checkpoint.
    /// </summary>
    public TimeSpan ElapsedTime { get; }

    /// <summary>
    /// Optional metadata associated with this checkpoint.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; }

    public OperationCheckpoint(string name, DateTime timestamp, TimeSpan elapsedTime,
        IDictionary<string, object>? metadata = null)
    {
        Name = ValidateName(name);
        Timestamp = timestamp;
        ElapsedTime = elapsedTime;
        Metadata = metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                  ?? new Dictionary<string, object>().AsReadOnly();
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Checkpoint name cannot be null, empty, or whitespace.", nameof(name));
        return name.Trim();
    }
}

/// <summary>
/// Progress information for long-running operations.
/// </summary>
public sealed class OperationProgress
{
    /// <summary>
    /// Percentage complete (0.0 to 1.0).
    /// </summary>
    public double PercentComplete { get; }

    /// <summary>
    /// Description of current step.
    /// </summary>
    public string CurrentStep { get; }

    /// <summary>
    /// When this progress was recorded.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Elapsed time from operation start.
    /// </summary>
    public TimeSpan ElapsedTime { get; }

    /// <summary>
    /// Optional estimated time remaining.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; }

    public OperationProgress(double percentComplete, string currentStep, DateTime timestamp,
        TimeSpan elapsedTime, TimeSpan? estimatedTimeRemaining = null)
    {
        PercentComplete = ValidatePercentComplete(percentComplete);
        CurrentStep = ValidateCurrentStep(currentStep);
        Timestamp = timestamp;
        ElapsedTime = elapsedTime;
        EstimatedTimeRemaining = estimatedTimeRemaining;
    }

    private static double ValidatePercentComplete(double percentComplete)
    {
        if (percentComplete < 0.0 || percentComplete > 1.0)
            throw new ArgumentOutOfRangeException(nameof(percentComplete), percentComplete,
                "Percent complete must be between 0.0 and 1.0");
        return percentComplete;
    }

    private static string ValidateCurrentStep(string currentStep)
    {
        if (string.IsNullOrWhiteSpace(currentStep))
            throw new ArgumentException("Current step cannot be null, empty, or whitespace.", nameof(currentStep));
        return currentStep.Trim();
    }
}

/// <summary>
/// Observability statistics for monitoring and alerting.
/// </summary>
public sealed class ObservabilityStatistics
{
    /// <summary>
    /// Total number of operations tracked.
    /// </summary>
    public long TotalOperations { get; }

    /// <summary>
    /// Number of operations currently in progress.
    /// </summary>
    public int ActiveOperations { get; }

    /// <summary>
    /// Number of successful operations.
    /// </summary>
    public long SuccessfulOperations { get; }

    /// <summary>
    /// Number of failed operations.
    /// </summary>
    public long FailedOperations { get; }

    /// <summary>
    /// Average operation duration by type.
    /// </summary>
    public IReadOnlyDictionary<OperationType, TimeSpan> AverageDurations { get; }

    /// <summary>
    /// Success rates by operation type.
    /// </summary>
    public IReadOnlyDictionary<OperationType, double> SuccessRates { get; }

    /// <summary>
    /// Total number of metrics recorded.
    /// </summary>
    public long MetricsRecorded { get; }

    /// <summary>
    /// Total number of correlation IDs generated.
    /// </summary>
    public long CorrelationIdsGenerated { get; }

    /// <summary>
    /// When these statistics were calculated.
    /// </summary>
    public DateTime CalculatedAt { get; }

    /// <summary>
    /// Statistics calculation window start time.
    /// </summary>
    public DateTime WindowStart { get; }

    public ObservabilityStatistics(long totalOperations, int activeOperations, long successfulOperations,
        long failedOperations, IDictionary<OperationType, TimeSpan>? averageDurations,
        IDictionary<OperationType, double>? successRates, long metricsRecorded,
        long correlationIdsGenerated, DateTime calculatedAt, DateTime windowStart)
    {
        TotalOperations = ValidateOperationCount(totalOperations, nameof(totalOperations));
        ActiveOperations = ValidateActiveOperations(activeOperations);
        SuccessfulOperations = ValidateOperationCount(successfulOperations, nameof(successfulOperations));
        FailedOperations = ValidateOperationCount(failedOperations, nameof(failedOperations));
        AverageDurations = averageDurations?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                          ?? new Dictionary<OperationType, TimeSpan>().AsReadOnly();
        SuccessRates = successRates?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                      ?? new Dictionary<OperationType, double>().AsReadOnly();
        MetricsRecorded = ValidateOperationCount(metricsRecorded, nameof(metricsRecorded));
        CorrelationIdsGenerated = ValidateOperationCount(correlationIdsGenerated, nameof(correlationIdsGenerated));
        CalculatedAt = calculatedAt;
        WindowStart = windowStart;
    }

    private static long ValidateOperationCount(long count, string paramName)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(paramName, count, $"{paramName} cannot be negative");
        return count;
    }

    private static int ValidateActiveOperations(int activeOperations)
    {
        if (activeOperations < 0)
            throw new ArgumentOutOfRangeException(nameof(activeOperations), activeOperations, "Active operations cannot be negative");
        return activeOperations;
    }
}