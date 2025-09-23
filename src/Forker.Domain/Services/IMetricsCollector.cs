namespace Forker.Domain.Services;

/// <summary>
/// Interface for collecting and exposing metrics for medical imaging file processing workflows.
/// Supports Prometheus-compatible metrics for monitoring and alerting.
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Records a counter metric that only increases.
    /// </summary>
    /// <param name="name">Metric name</param>
    /// <param name="value">Value to add to counter</param>
    /// <param name="help">Help text describing the metric</param>
    /// <param name="labels">Optional labels for metric dimensions</param>
    void Counter(string name, double value = 1, string? help = null, params (string Key, string Value)[] labels);

    /// <summary>
    /// Records a gauge metric that can increase or decrease.
    /// </summary>
    /// <param name="name">Metric name</param>
    /// <param name="value">Current gauge value</param>
    /// <param name="help">Help text describing the metric</param>
    /// <param name="labels">Optional labels for metric dimensions</param>
    void Gauge(string name, double value, string? help = null, params (string Key, string Value)[] labels);

    /// <summary>
    /// Records a histogram observation for distribution tracking.
    /// </summary>
    /// <param name="name">Metric name</param>
    /// <param name="value">Value to observe</param>
    /// <param name="help">Help text describing the metric</param>
    /// <param name="labels">Optional labels for metric dimensions</param>
    void Histogram(string name, double value, string? help = null, params (string Key, string Value)[] labels);

    /// <summary>
    /// Records a summary observation for percentile tracking.
    /// </summary>
    /// <param name="name">Metric name</param>
    /// <param name="value">Value to observe</param>
    /// <param name="help">Help text describing the metric</param>
    /// <param name="labels">Optional labels for metric dimensions</param>
    void Summary(string name, double value, string? help = null, params (string Key, string Value)[] labels);

    /// <summary>
    /// Records timing information for operations.
    /// </summary>
    /// <param name="name">Timer name</param>
    /// <param name="duration">Duration to record</param>
    /// <param name="operationType">Type of operation for categorization</param>
    /// <param name="success">Whether the operation was successful</param>
    /// <param name="labels">Optional additional labels</param>
    void Timer(string name, TimeSpan duration, OperationType operationType, bool success,
        params (string Key, string Value)[] labels);

    /// <summary>
    /// Creates a timer scope that automatically records duration when disposed.
    /// </summary>
    /// <param name="name">Timer name</param>
    /// <param name="operationType">Type of operation for categorization</param>
    /// <param name="labels">Optional labels for metric dimensions</param>
    /// <returns>Disposable timer scope</returns>
    ITimerScope StartTimer(string name, OperationType operationType, params (string Key, string Value)[] labels);

    /// <summary>
    /// Records file processing metrics specific to medical imaging workflows.
    /// </summary>
    /// <param name="metrics">File processing metrics</param>
    void RecordFileProcessingMetrics(FileProcessingMetrics metrics);

    /// <summary>
    /// Records system resource utilization metrics.
    /// </summary>
    /// <param name="utilization">Resource utilization data</param>
    void RecordResourceUtilization(ResourceUtilization utilization);

    /// <summary>
    /// Gets current metric values for monitoring.
    /// </summary>
    /// <returns>Dictionary of metric names to current values</returns>
    Task<IReadOnlyDictionary<string, double>> GetCurrentMetricsAsync();

    /// <summary>
    /// Exports metrics in Prometheus text format.
    /// </summary>
    /// <returns>Prometheus-formatted metrics text</returns>
    Task<string> ExportPrometheusMetricsAsync();

    /// <summary>
    /// Gets metrics statistics for the specified time window.
    /// </summary>
    /// <param name="since">Start of time window</param>
    /// <param name="until">End of time window (null for current time)</param>
    /// <returns>Metrics statistics</returns>
    Task<MetricsStatistics> GetStatisticsAsync(DateTime since, DateTime? until = null);
}

/// <summary>
/// Timer scope that automatically records duration when disposed.
/// </summary>
public interface ITimerScope : IDisposable
{
    /// <summary>
    /// Name of the timer being tracked.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Operation type being timed.
    /// </summary>
    OperationType OperationType { get; }

    /// <summary>
    /// When the timer was started.
    /// </summary>
    DateTime StartTime { get; }

    /// <summary>
    /// Current elapsed time.
    /// </summary>
    TimeSpan Elapsed { get; }

    /// <summary>
    /// Labels applied to this timer.
    /// </summary>
    IReadOnlyDictionary<string, string> Labels { get; }

    /// <summary>
    /// Marks the operation as successful (affects success rate metrics).
    /// </summary>
    void MarkSuccess();

    /// <summary>
    /// Marks the operation as failed (affects success rate metrics).
    /// </summary>
    /// <param name="exception">Optional exception that caused the failure</param>
    void MarkFailure(Exception? exception = null);

    /// <summary>
    /// Adds additional labels to the timer.
    /// </summary>
    /// <param name="key">Label key</param>
    /// <param name="value">Label value</param>
    void AddLabel(string key, string value);
}

/// <summary>
/// File processing metrics specific to medical imaging workflows.
/// </summary>
public sealed class FileProcessingMetrics
{
    /// <summary>
    /// Source file path.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSizeBytes { get; }

    /// <summary>
    /// File processing operation type.
    /// </summary>
    public OperationType OperationType { get; }

    /// <summary>
    /// Processing duration.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Processing throughput in bytes per second.
    /// </summary>
    public double ThroughputBytesPerSecond { get; }

    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Error message if operation failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Number of retry attempts made.
    /// </summary>
    public int RetryAttempts { get; }

    /// <summary>
    /// Correlation ID for operation tracking.
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    /// When the processing was completed.
    /// </summary>
    public DateTime CompletedAt { get; }

    /// <summary>
    /// File format/extension.
    /// </summary>
    public string FileFormat { get; }

    /// <summary>
    /// Target identifiers if applicable.
    /// </summary>
    public IReadOnlyList<string> Targets { get; }

    public FileProcessingMetrics(string sourcePath, long fileSizeBytes, OperationType operationType,
        TimeSpan duration, bool success, string correlationId, DateTime completedAt,
        string? errorMessage = null, int retryAttempts = 0, IEnumerable<string>? targets = null)
    {
        SourcePath = ValidateSourcePath(sourcePath);
        FileSizeBytes = ValidateFileSizeBytes(fileSizeBytes);
        OperationType = operationType;
        Duration = duration;
        ThroughputBytesPerSecond = duration.TotalSeconds > 0 ? fileSizeBytes / duration.TotalSeconds : 0;
        Success = success;
        ErrorMessage = errorMessage;
        RetryAttempts = ValidateRetryAttempts(retryAttempts);
        CorrelationId = ValidateCorrelationId(correlationId);
        CompletedAt = completedAt;
        FileFormat = Path.GetExtension(sourcePath)?.TrimStart('.').ToLowerInvariant() ?? "unknown";
        Targets = targets?.ToList().AsReadOnly() ?? Array.Empty<string>().ToList().AsReadOnly();
    }

    private static string ValidateSourcePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path cannot be null, empty, or whitespace.", nameof(sourcePath));
        return sourcePath.Trim();
    }

    private static long ValidateFileSizeBytes(long fileSizeBytes)
    {
        if (fileSizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(fileSizeBytes), fileSizeBytes, "File size cannot be negative");
        return fileSizeBytes;
    }

    private static int ValidateRetryAttempts(int retryAttempts)
    {
        if (retryAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(retryAttempts), retryAttempts, "Retry attempts cannot be negative");
        return retryAttempts;
    }

    private static string ValidateCorrelationId(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Correlation ID cannot be null, empty, or whitespace.", nameof(correlationId));
        return correlationId.Trim();
    }
}

/// <summary>
/// Statistics about collected metrics over a time window.
/// </summary>
public sealed class MetricsStatistics
{
    /// <summary>
    /// Total number of metrics recorded.
    /// </summary>
    public long TotalMetrics { get; }

    /// <summary>
    /// Number of unique metric names.
    /// </summary>
    public int UniqueMetricNames { get; }

    /// <summary>
    /// File processing statistics by operation type.
    /// </summary>
    public IReadOnlyDictionary<OperationType, FileProcessingStats> ProcessingStatsByType { get; }

    /// <summary>
    /// Performance statistics by file format.
    /// </summary>
    public IReadOnlyDictionary<string, PerformanceStats> PerformanceStatsByFormat { get; }

    /// <summary>
    /// Error rates by operation type.
    /// </summary>
    public IReadOnlyDictionary<OperationType, double> ErrorRatesByType { get; }

    /// <summary>
    /// When these statistics were calculated.
    /// </summary>
    public DateTime CalculatedAt { get; }

    /// <summary>
    /// Statistics time window start.
    /// </summary>
    public DateTime WindowStart { get; }

    /// <summary>
    /// Statistics time window end.
    /// </summary>
    public DateTime WindowEnd { get; }

    public MetricsStatistics(long totalMetrics, int uniqueMetricNames,
        IDictionary<OperationType, FileProcessingStats>? processingStatsByType,
        IDictionary<string, PerformanceStats>? performanceStatsByFormat,
        IDictionary<OperationType, double>? errorRatesByType,
        DateTime calculatedAt, DateTime windowStart, DateTime windowEnd)
    {
        TotalMetrics = ValidateTotalMetrics(totalMetrics);
        UniqueMetricNames = ValidateUniqueMetricNames(uniqueMetricNames);
        ProcessingStatsByType = processingStatsByType?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                               ?? new Dictionary<OperationType, FileProcessingStats>().AsReadOnly();
        PerformanceStatsByFormat = performanceStatsByFormat?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                                  ?? new Dictionary<string, PerformanceStats>().AsReadOnly();
        ErrorRatesByType = errorRatesByType?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                          ?? new Dictionary<OperationType, double>().AsReadOnly();
        CalculatedAt = calculatedAt;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
    }

    private static long ValidateTotalMetrics(long totalMetrics)
    {
        if (totalMetrics < 0)
            throw new ArgumentOutOfRangeException(nameof(totalMetrics), totalMetrics, "Total metrics cannot be negative");
        return totalMetrics;
    }

    private static int ValidateUniqueMetricNames(int uniqueMetricNames)
    {
        if (uniqueMetricNames < 0)
            throw new ArgumentOutOfRangeException(nameof(uniqueMetricNames), uniqueMetricNames, "Unique metric names cannot be negative");
        return uniqueMetricNames;
    }
}

/// <summary>
/// File processing statistics for a specific operation type.
/// </summary>
public sealed class FileProcessingStats
{
    /// <summary>
    /// Total number of files processed.
    /// </summary>
    public long FilesProcessed { get; }

    /// <summary>
    /// Total bytes processed.
    /// </summary>
    public long TotalBytesProcessed { get; }

    /// <summary>
    /// Average processing time.
    /// </summary>
    public TimeSpan AverageProcessingTime { get; }

    /// <summary>
    /// Average throughput in bytes per second.
    /// </summary>
    public double AverageThroughputBytesPerSecond { get; }

    /// <summary>
    /// Success rate (0.0 to 1.0).
    /// </summary>
    public double SuccessRate { get; }

    public FileProcessingStats(long filesProcessed, long totalBytesProcessed, TimeSpan averageProcessingTime,
        double averageThroughputBytesPerSecond, double successRate)
    {
        FilesProcessed = ValidateFilesProcessed(filesProcessed);
        TotalBytesProcessed = ValidateTotalBytesProcessed(totalBytesProcessed);
        AverageProcessingTime = averageProcessingTime;
        AverageThroughputBytesPerSecond = ValidateAverageThroughput(averageThroughputBytesPerSecond);
        SuccessRate = ValidateSuccessRate(successRate);
    }

    private static long ValidateFilesProcessed(long filesProcessed)
    {
        if (filesProcessed < 0)
            throw new ArgumentOutOfRangeException(nameof(filesProcessed), filesProcessed, "Files processed cannot be negative");
        return filesProcessed;
    }

    private static long ValidateTotalBytesProcessed(long totalBytesProcessed)
    {
        if (totalBytesProcessed < 0)
            throw new ArgumentOutOfRangeException(nameof(totalBytesProcessed), totalBytesProcessed, "Total bytes processed cannot be negative");
        return totalBytesProcessed;
    }

    private static double ValidateAverageThroughput(double averageThroughputBytesPerSecond)
    {
        if (averageThroughputBytesPerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(averageThroughputBytesPerSecond), averageThroughputBytesPerSecond, "Average throughput cannot be negative");
        return averageThroughputBytesPerSecond;
    }

    private static double ValidateSuccessRate(double successRate)
    {
        if (successRate < 0.0 || successRate > 1.0)
            throw new ArgumentOutOfRangeException(nameof(successRate), successRate, "Success rate must be between 0.0 and 1.0");
        return successRate;
    }
}

/// <summary>
/// Performance statistics for a specific file format.
/// </summary>
public sealed class PerformanceStats
{
    /// <summary>
    /// Average file size in bytes.
    /// </summary>
    public double AverageFileSizeBytes { get; }

    /// <summary>
    /// Median processing time.
    /// </summary>
    public TimeSpan MedianProcessingTime { get; }

    /// <summary>
    /// 95th percentile processing time.
    /// </summary>
    public TimeSpan P95ProcessingTime { get; }

    /// <summary>
    /// Maximum processing time observed.
    /// </summary>
    public TimeSpan MaxProcessingTime { get; }

    public PerformanceStats(double averageFileSizeBytes, TimeSpan medianProcessingTime,
        TimeSpan p95ProcessingTime, TimeSpan maxProcessingTime)
    {
        AverageFileSizeBytes = ValidateAverageFileSize(averageFileSizeBytes);
        MedianProcessingTime = medianProcessingTime;
        P95ProcessingTime = p95ProcessingTime;
        MaxProcessingTime = maxProcessingTime;
    }

    private static double ValidateAverageFileSize(double averageFileSizeBytes)
    {
        if (averageFileSizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(averageFileSizeBytes), averageFileSizeBytes, "Average file size cannot be negative");
        return averageFileSizeBytes;
    }
}