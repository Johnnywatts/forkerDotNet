namespace Forker.Domain.Services;

/// <summary>
/// Interface for adaptive concurrency control that dynamically adjusts parallelism
/// based on system performance and resource utilization for medical imaging workflows.
/// </summary>
public interface IConcurrencyController
{
    /// <summary>
    /// Gets the current maximum concurrency level for the specified operation type.
    /// </summary>
    /// <param name="operationType">Type of operation to get concurrency for</param>
    /// <returns>Current maximum concurrent operations allowed</returns>
    int GetCurrentConcurrency(OperationType operationType);

    /// <summary>
    /// Attempts to acquire a concurrency slot for the specified operation.
    /// </summary>
    /// <param name="operationType">Type of operation requesting concurrency</param>
    /// <param name="estimatedDuration">Estimated duration of the operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Concurrency slot that must be disposed when operation completes</returns>
    Task<IConcurrencySlot> AcquireSlotAsync(OperationType operationType, TimeSpan estimatedDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports the completion of an operation to help adjust future concurrency decisions.
    /// </summary>
    /// <param name="operationType">Type of operation that completed</param>
    /// <param name="actualDuration">Actual duration the operation took</param>
    /// <param name="success">Whether the operation completed successfully</param>
    /// <param name="resourceUsage">Resource usage metrics during the operation</param>
    Task ReportOperationCompletionAsync(OperationType operationType, TimeSpan actualDuration,
        bool success, ResourceUsageMetrics resourceUsage);

    /// <summary>
    /// Gets current system resource utilization metrics.
    /// </summary>
    /// <returns>Current resource utilization</returns>
    Task<ResourceUtilization> GetCurrentResourceUtilizationAsync();

    /// <summary>
    /// Gets performance statistics for adaptive concurrency decisions.
    /// </summary>
    /// <param name="operationType">Optional operation type to filter statistics</param>
    /// <param name="since">Optional start time for statistics window</param>
    /// <returns>Performance statistics</returns>
    Task<ConcurrencyStatistics> GetStatisticsAsync(OperationType? operationType = null,
        DateTime? since = null);

    /// <summary>
    /// Manually adjusts the concurrency limits (for administrative overrides).
    /// </summary>
    /// <param name="operationType">Operation type to adjust</param>
    /// <param name="newLimit">New concurrency limit</param>
    /// <param name="reason">Reason for manual adjustment</param>
    /// <param name="adjustedBy">Identity of person/system making adjustment</param>
    Task SetConcurrencyLimitAsync(OperationType operationType, int newLimit, string reason, string adjustedBy);
}

/// <summary>
/// Represents a concurrency slot that tracks resource usage during an operation.
/// Must be disposed when the operation completes to release the slot.
/// </summary>
public interface IConcurrencySlot : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Unique identifier for this concurrency slot.
    /// </summary>
    Guid SlotId { get; }

    /// <summary>
    /// Operation type this slot is allocated for.
    /// </summary>
    OperationType OperationType { get; }

    /// <summary>
    /// When this slot was acquired.
    /// </summary>
    DateTime AcquiredAt { get; }

    /// <summary>
    /// Estimated duration when the slot was acquired.
    /// </summary>
    TimeSpan EstimatedDuration { get; }

    /// <summary>
    /// Updates the progress of the operation for monitoring.
    /// </summary>
    /// <param name="percentComplete">Percentage complete (0.0 to 1.0)</param>
    /// <param name="currentResourceUsage">Current resource usage</param>
    void UpdateProgress(double percentComplete, ResourceUsageMetrics currentResourceUsage);

    /// <summary>
    /// Marks the operation as completed successfully.
    /// </summary>
    /// <param name="finalResourceUsage">Final resource usage metrics</param>
    void MarkCompleted(ResourceUsageMetrics finalResourceUsage);

    /// <summary>
    /// Marks the operation as failed.
    /// </summary>
    /// <param name="exception">Exception that caused the failure</param>
    /// <param name="finalResourceUsage">Final resource usage metrics</param>
    void MarkFailed(Exception exception, ResourceUsageMetrics finalResourceUsage);
}

/// <summary>
/// Resource usage metrics for a specific operation or system-wide.
/// </summary>
public sealed class ResourceUsageMetrics
{
    /// <summary>
    /// CPU usage percentage (0.0 to 1.0).
    /// </summary>
    public double CpuUsage { get; }

    /// <summary>
    /// Memory usage in bytes.
    /// </summary>
    public long MemoryUsageBytes { get; }

    /// <summary>
    /// Disk I/O operations per second.
    /// </summary>
    public double DiskIopsPerSecond { get; }

    /// <summary>
    /// Disk I/O throughput in bytes per second.
    /// </summary>
    public long DiskThroughputBytesPerSecond { get; }

    /// <summary>
    /// Network I/O throughput in bytes per second.
    /// </summary>
    public long NetworkThroughputBytesPerSecond { get; }

    /// <summary>
    /// Available disk space in bytes.
    /// </summary>
    public long AvailableDiskSpaceBytes { get; }

    /// <summary>
    /// When these metrics were collected.
    /// </summary>
    public DateTime CollectedAt { get; }

    public ResourceUsageMetrics(double cpuUsage, long memoryUsageBytes, double diskIopsPerSecond,
        long diskThroughputBytesPerSecond, long networkThroughputBytesPerSecond,
        long availableDiskSpaceBytes, DateTime collectedAt)
    {
        CpuUsage = ValidateCpuUsage(cpuUsage);
        MemoryUsageBytes = ValidateMemoryUsage(memoryUsageBytes);
        DiskIopsPerSecond = ValidateDiskIops(diskIopsPerSecond);
        DiskThroughputBytesPerSecond = ValidateThroughput(diskThroughputBytesPerSecond, nameof(diskThroughputBytesPerSecond));
        NetworkThroughputBytesPerSecond = ValidateThroughput(networkThroughputBytesPerSecond, nameof(networkThroughputBytesPerSecond));
        AvailableDiskSpaceBytes = ValidateAvailableSpace(availableDiskSpaceBytes);
        CollectedAt = collectedAt;
    }

    private static double ValidateCpuUsage(double cpuUsage)
    {
        if (cpuUsage < 0.0 || cpuUsage > 1.0)
            throw new ArgumentOutOfRangeException(nameof(cpuUsage), cpuUsage, "CPU usage must be between 0.0 and 1.0");
        return cpuUsage;
    }

    private static long ValidateMemoryUsage(long memoryUsageBytes)
    {
        if (memoryUsageBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(memoryUsageBytes), memoryUsageBytes, "Memory usage cannot be negative");
        return memoryUsageBytes;
    }

    private static double ValidateDiskIops(double diskIopsPerSecond)
    {
        if (diskIopsPerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(diskIopsPerSecond), diskIopsPerSecond, "Disk IOPS cannot be negative");
        return diskIopsPerSecond;
    }

    private static long ValidateThroughput(long throughputBytesPerSecond, string paramName)
    {
        if (throughputBytesPerSecond < 0)
            throw new ArgumentOutOfRangeException(paramName, throughputBytesPerSecond, "Throughput cannot be negative");
        return throughputBytesPerSecond;
    }

    private static long ValidateAvailableSpace(long availableDiskSpaceBytes)
    {
        if (availableDiskSpaceBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(availableDiskSpaceBytes), availableDiskSpaceBytes, "Available disk space cannot be negative");
        return availableDiskSpaceBytes;
    }
}

/// <summary>
/// Current system resource utilization levels.
/// </summary>
public sealed class ResourceUtilization
{
    /// <summary>
    /// Overall system resource usage.
    /// </summary>
    public ResourceUsageMetrics SystemMetrics { get; }

    /// <summary>
    /// Resource usage specific to ForkerDotNet processes.
    /// </summary>
    public ResourceUsageMetrics ProcessMetrics { get; }

    /// <summary>
    /// Resource utilization level (Low, Normal, High, Critical).
    /// </summary>
    public UtilizationLevel UtilizationLevel { get; }

    /// <summary>
    /// Detailed breakdown of what's driving the utilization level.
    /// </summary>
    public string UtilizationDetails { get; }

    public ResourceUtilization(ResourceUsageMetrics systemMetrics, ResourceUsageMetrics processMetrics,
        UtilizationLevel utilizationLevel, string utilizationDetails)
    {
        SystemMetrics = systemMetrics ?? throw new ArgumentNullException(nameof(systemMetrics));
        ProcessMetrics = processMetrics ?? throw new ArgumentNullException(nameof(processMetrics));
        UtilizationLevel = utilizationLevel;
        UtilizationDetails = ValidateUtilizationDetails(utilizationDetails);
    }

    private static string ValidateUtilizationDetails(string utilizationDetails)
    {
        if (string.IsNullOrWhiteSpace(utilizationDetails))
            throw new ArgumentException("Utilization details cannot be null, empty, or whitespace.", nameof(utilizationDetails));
        return utilizationDetails.Trim();
    }
}

/// <summary>
/// Resource utilization levels for adaptive concurrency decisions.
/// </summary>
public enum UtilizationLevel
{
    /// <summary>
    /// Low resource utilization - can increase concurrency.
    /// </summary>
    Low,

    /// <summary>
    /// Normal resource utilization - maintain current concurrency.
    /// </summary>
    Normal,

    /// <summary>
    /// High resource utilization - consider reducing concurrency.
    /// </summary>
    High,

    /// <summary>
    /// Critical resource utilization - must reduce concurrency immediately.
    /// </summary>
    Critical
}

/// <summary>
/// Performance statistics for adaptive concurrency decisions.
/// </summary>
public sealed class ConcurrencyStatistics
{
    /// <summary>
    /// Current concurrency limits by operation type.
    /// </summary>
    public IReadOnlyDictionary<OperationType, int> CurrentLimits { get; }

    /// <summary>
    /// Average operation duration by type.
    /// </summary>
    public IReadOnlyDictionary<OperationType, TimeSpan> AverageDurations { get; }

    /// <summary>
    /// Success rates by operation type.
    /// </summary>
    public IReadOnlyDictionary<OperationType, double> SuccessRates { get; }

    /// <summary>
    /// Operations completed in the statistics window.
    /// </summary>
    public int OperationsCompleted { get; }

    /// <summary>
    /// Operations currently in progress.
    /// </summary>
    public int OperationsInProgress { get; }

    /// <summary>
    /// Average resource utilization during operations.
    /// </summary>
    public ResourceUsageMetrics AverageResourceUsage { get; }

    /// <summary>
    /// When these statistics were calculated.
    /// </summary>
    public DateTime CalculatedAt { get; }

    /// <summary>
    /// Statistics calculation window start time.
    /// </summary>
    public DateTime WindowStart { get; }

    public ConcurrencyStatistics(IDictionary<OperationType, int> currentLimits,
        IDictionary<OperationType, TimeSpan> averageDurations, IDictionary<OperationType, double> successRates,
        int operationsCompleted, int operationsInProgress, ResourceUsageMetrics averageResourceUsage,
        DateTime calculatedAt, DateTime windowStart)
    {
        CurrentLimits = currentLimits?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                       ?? new Dictionary<OperationType, int>().AsReadOnly();
        AverageDurations = averageDurations?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                          ?? new Dictionary<OperationType, TimeSpan>().AsReadOnly();
        SuccessRates = successRates?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value).AsReadOnly()
                      ?? new Dictionary<OperationType, double>().AsReadOnly();
        OperationsCompleted = ValidateOperationsCompleted(operationsCompleted);
        OperationsInProgress = ValidateOperationsInProgress(operationsInProgress);
        AverageResourceUsage = averageResourceUsage ?? throw new ArgumentNullException(nameof(averageResourceUsage));
        CalculatedAt = calculatedAt;
        WindowStart = windowStart;
    }

    private static int ValidateOperationsCompleted(int operationsCompleted)
    {
        if (operationsCompleted < 0)
            throw new ArgumentOutOfRangeException(nameof(operationsCompleted), operationsCompleted, "Operations completed cannot be negative");
        return operationsCompleted;
    }

    private static int ValidateOperationsInProgress(int operationsInProgress)
    {
        if (operationsInProgress < 0)
            throw new ArgumentOutOfRangeException(nameof(operationsInProgress), operationsInProgress, "Operations in progress cannot be negative");
        return operationsInProgress;
    }
}