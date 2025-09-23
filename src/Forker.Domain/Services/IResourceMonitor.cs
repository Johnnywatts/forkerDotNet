namespace Forker.Domain.Services;

/// <summary>
/// Interface for monitoring system resource utilization to support adaptive concurrency control.
/// Provides real-time metrics for CPU, memory, disk I/O, and network usage.
/// </summary>
public interface IResourceMonitor
{
    /// <summary>
    /// Gets current system-wide resource utilization metrics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current system resource metrics</returns>
    Task<ResourceUsageMetrics> GetSystemResourceUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current resource utilization metrics for the ForkerDotNet process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current process resource metrics</returns>
    Task<ResourceUsageMetrics> GetProcessResourceUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current resource utilization assessment for adaptive concurrency decisions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource utilization assessment</returns>
    Task<ResourceUtilization> GetResourceUtilizationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts continuous monitoring of resource usage with the specified interval.
    /// </summary>
    /// <param name="monitoringInterval">How frequently to collect metrics</param>
    /// <param name="cancellationToken">Cancellation token to stop monitoring</param>
    /// <returns>Task that completes when monitoring stops</returns>
    Task StartMonitoringAsync(TimeSpan monitoringInterval, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops continuous resource monitoring.
    /// </summary>
    Task StopMonitoringAsync();

    /// <summary>
    /// Gets historical resource usage metrics for trend analysis.
    /// </summary>
    /// <param name="since">Start time for historical data</param>
    /// <param name="until">End time for historical data (null for current time)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Historical resource usage data</returns>
    Task<IReadOnlyList<ResourceUsageSnapshot>> GetHistoricalUsageAsync(DateTime since,
        DateTime? until = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a callback to be notified when resource utilization changes significantly.
    /// </summary>
    /// <param name="callback">Callback to invoke when utilization changes</param>
    /// <param name="thresholdChange">Minimum change in utilization level to trigger callback</param>
    /// <returns>Subscription that can be disposed to unregister the callback</returns>
    IDisposable RegisterUtilizationChangeCallback(Action<ResourceUtilization> callback,
        UtilizationLevel thresholdChange = UtilizationLevel.Normal);

    /// <summary>
    /// Gets performance counters specific to file operations for medical imaging workflows.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>File operation performance metrics</returns>
    Task<FileOperationMetrics> GetFileOperationMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Historical snapshot of resource usage at a specific point in time.
/// </summary>
public sealed class ResourceUsageSnapshot
{
    /// <summary>
    /// Resource usage metrics at the snapshot time.
    /// </summary>
    public ResourceUsageMetrics Metrics { get; }

    /// <summary>
    /// Utilization level at the snapshot time.
    /// </summary>
    public UtilizationLevel UtilizationLevel { get; }

    /// <summary>
    /// Number of active operations at snapshot time.
    /// </summary>
    public int ActiveOperations { get; }

    /// <summary>
    /// Breakdown of active operations by type.
    /// </summary>
    public IReadOnlyDictionary<OperationType, int> ActiveOperationsByType { get; }

    public ResourceUsageSnapshot(ResourceUsageMetrics metrics, UtilizationLevel utilizationLevel,
        int activeOperations, IDictionary<OperationType, int>? activeOperationsByType = null)
    {
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        UtilizationLevel = utilizationLevel;
        ActiveOperations = ValidateActiveOperations(activeOperations);
        ActiveOperationsByType = activeOperationsByType?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                                ?? new Dictionary<OperationType, int>().AsReadOnly();
    }

    private static int ValidateActiveOperations(int activeOperations)
    {
        if (activeOperations < 0)
            throw new ArgumentOutOfRangeException(nameof(activeOperations), activeOperations, "Active operations cannot be negative");
        return activeOperations;
    }
}

/// <summary>
/// Performance metrics specific to file operations in medical imaging workflows.
/// </summary>
public sealed class FileOperationMetrics
{
    /// <summary>
    /// Average file copy throughput in bytes per second.
    /// </summary>
    public long AverageCopyThroughputBytesPerSecond { get; }

    /// <summary>
    /// Average file verification (hash) throughput in bytes per second.
    /// </summary>
    public long AverageVerificationThroughputBytesPerSecond { get; }

    /// <summary>
    /// Average queue depth for disk operations.
    /// </summary>
    public double AverageDiskQueueDepth { get; }

    /// <summary>
    /// Average disk response time in milliseconds.
    /// </summary>
    public double AverageDiskResponseTimeMs { get; }

    /// <summary>
    /// Cache hit ratio for file system operations.
    /// </summary>
    public double FileSystemCacheHitRatio { get; }

    /// <summary>
    /// Available bandwidth for network operations (bytes per second).
    /// </summary>
    public long AvailableNetworkBandwidthBytesPerSecond { get; }

    /// <summary>
    /// When these metrics were collected.
    /// </summary>
    public DateTime CollectedAt { get; }

    public FileOperationMetrics(long averageCopyThroughputBytesPerSecond,
        long averageVerificationThroughputBytesPerSecond, double averageDiskQueueDepth,
        double averageDiskResponseTimeMs, double fileSystemCacheHitRatio,
        long availableNetworkBandwidthBytesPerSecond, DateTime collectedAt)
    {
        AverageCopyThroughputBytesPerSecond = ValidateThroughput(averageCopyThroughputBytesPerSecond, nameof(averageCopyThroughputBytesPerSecond));
        AverageVerificationThroughputBytesPerSecond = ValidateThroughput(averageVerificationThroughputBytesPerSecond, nameof(averageVerificationThroughputBytesPerSecond));
        AverageDiskQueueDepth = ValidateQueueDepth(averageDiskQueueDepth);
        AverageDiskResponseTimeMs = ValidateResponseTime(averageDiskResponseTimeMs);
        FileSystemCacheHitRatio = ValidateCacheHitRatio(fileSystemCacheHitRatio);
        AvailableNetworkBandwidthBytesPerSecond = ValidateThroughput(availableNetworkBandwidthBytesPerSecond, nameof(availableNetworkBandwidthBytesPerSecond));
        CollectedAt = collectedAt;
    }

    private static long ValidateThroughput(long throughputBytesPerSecond, string paramName)
    {
        if (throughputBytesPerSecond < 0)
            throw new ArgumentOutOfRangeException(paramName, throughputBytesPerSecond, "Throughput cannot be negative");
        return throughputBytesPerSecond;
    }

    private static double ValidateQueueDepth(double queueDepth)
    {
        if (queueDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(queueDepth), queueDepth, "Queue depth cannot be negative");
        return queueDepth;
    }

    private static double ValidateResponseTime(double responseTimeMs)
    {
        if (responseTimeMs < 0)
            throw new ArgumentOutOfRangeException(nameof(responseTimeMs), responseTimeMs, "Response time cannot be negative");
        return responseTimeMs;
    }

    private static double ValidateCacheHitRatio(double cacheHitRatio)
    {
        if (cacheHitRatio < 0.0 || cacheHitRatio > 1.0)
            throw new ArgumentOutOfRangeException(nameof(cacheHitRatio), cacheHitRatio, "Cache hit ratio must be between 0.0 and 1.0");
        return cacheHitRatio;
    }
}