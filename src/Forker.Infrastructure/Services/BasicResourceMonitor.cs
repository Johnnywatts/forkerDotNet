using System.Diagnostics;
using Forker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Basic resource monitor implementation that works across platforms without Windows-specific dependencies.
/// Provides essential metrics for adaptive concurrency control using standard .NET APIs.
/// </summary>
public sealed class BasicResourceMonitor : IResourceMonitor, IDisposable
{
    private readonly ILogger<BasicResourceMonitor> _logger;
    private readonly Process _currentProcess;
    private readonly List<Action<ResourceUtilization>> _utilizationCallbacks = new();
    private readonly List<ResourceUsageSnapshot> _historicalSnapshots = new();
    private readonly object _lockObject = new();

    private ResourceUtilization? _lastUtilization;
    private bool _isMonitoring;
    private bool _disposed;
    private DateTime _lastCpuMeasurement = DateTime.UtcNow;
    private TimeSpan _lastCpuTime;

    public BasicResourceMonitor(ILogger<BasicResourceMonitor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currentProcess = Process.GetCurrentProcess();
        _lastCpuTime = _currentProcess.TotalProcessorTime;

        _logger.LogInformation("Basic resource monitor initialized");
    }

    public Task<ResourceUsageMetrics> GetSystemResourceUsageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cpuUsage = GetSystemCpuUsage();
            var totalMemory = GetTotalPhysicalMemory();
            var availableMemory = GetAvailableMemory();
            var memoryUsage = totalMemory - availableMemory;
            var availableDiskSpace = GetAvailableDiskSpace();

            // Use conservative estimates for disk and network I/O
            var diskThroughput = 50L * 1024 * 1024; // 50MB/s estimate
            var networkThroughput = 10L * 1024 * 1024; // 10MB/s estimate
            var diskIops = 100.0; // 100 IOPS estimate

            var metrics = new ResourceUsageMetrics(
                cpuUsage,
                memoryUsage,
                diskIops,
                diskThroughput,
                networkThroughput,
                availableDiskSpace,
                DateTime.UtcNow);

            return Task.FromResult(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system resource usage");
            return Task.FromResult(CreateFallbackSystemMetrics());
        }
    }

    public Task<ResourceUsageMetrics> GetProcessResourceUsageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _currentProcess.Refresh();

            var cpuUsage = GetProcessCpuUsage();
            var memoryUsage = _currentProcess.WorkingSet64;

            // Process-level estimates
            var diskThroughput = Math.Min(10L * 1024 * 1024, memoryUsage / 100); // Conservative estimate
            var networkThroughput = 1L * 1024 * 1024; // 1MB/s for file operations

            var metrics = new ResourceUsageMetrics(
                cpuUsage,
                memoryUsage,
                0, // Process-level IOPS not easily available
                diskThroughput,
                networkThroughput,
                GetAvailableDiskSpace(),
                DateTime.UtcNow);

            return Task.FromResult(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting process resource usage");
            return Task.FromResult(CreateFallbackProcessMetrics());
        }
    }

    public async Task<ResourceUtilization> GetResourceUtilizationAsync(CancellationToken cancellationToken = default)
    {
        var systemMetrics = await GetSystemResourceUsageAsync(cancellationToken);
        var processMetrics = await GetProcessResourceUsageAsync(cancellationToken);

        var utilizationLevel = DetermineUtilizationLevel(systemMetrics, processMetrics);
        var utilizationDetails = GenerateUtilizationDetails(systemMetrics, processMetrics, utilizationLevel);

        var utilization = new ResourceUtilization(systemMetrics, processMetrics, utilizationLevel, utilizationDetails);

        // Store for historical tracking
        lock (_lockObject)
        {
            _historicalSnapshots.Add(new ResourceUsageSnapshot(systemMetrics, utilizationLevel, 0));
            // Keep only last 1000 snapshots to prevent memory growth
            if (_historicalSnapshots.Count > 1000)
            {
                _historicalSnapshots.RemoveAt(0);
            }
        }

        // Notify callbacks if utilization level changed significantly
        NotifyUtilizationCallbacks(utilization);

        return utilization;
    }

    public Task StartMonitoringAsync(TimeSpan monitoringInterval, CancellationToken cancellationToken = default)
    {
        if (_isMonitoring)
        {
            _logger.LogWarning("Resource monitoring is already running");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Starting resource monitoring with interval {MonitoringInterval}", monitoringInterval);
        _isMonitoring = true;

        // Start background monitoring loop
        _ = Task.Run(async () =>
        {
            while (_isMonitoring && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await GetResourceUtilizationAsync(cancellationToken);
                    await Task.Delay(monitoringInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during resource monitoring");
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken); // Brief pause on error
                }
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopMonitoringAsync()
    {
        _logger.LogInformation("Stopping resource monitoring");
        _isMonitoring = false;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ResourceUsageSnapshot>> GetHistoricalUsageAsync(DateTime since,
        DateTime? until = null, CancellationToken cancellationToken = default)
    {
        var endTime = until ?? DateTime.UtcNow;

        lock (_lockObject)
        {
            var filteredSnapshots = _historicalSnapshots
                .Where(s => s.Metrics.CollectedAt >= since && s.Metrics.CollectedAt <= endTime)
                .ToList()
                .AsReadOnly();

            return Task.FromResult<IReadOnlyList<ResourceUsageSnapshot>>(filteredSnapshots);
        }
    }

    public IDisposable RegisterUtilizationChangeCallback(Action<ResourceUtilization> callback,
        UtilizationLevel thresholdChange = UtilizationLevel.Normal)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_lockObject)
        {
            _utilizationCallbacks.Add(callback);
        }

        return new CallbackSubscription(() =>
        {
            lock (_lockObject)
            {
                _utilizationCallbacks.Remove(callback);
            }
        });
    }

    public Task<FileOperationMetrics> GetFileOperationMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Provide reasonable estimates for file operations
            var copyThroughput = 100L * 1024 * 1024; // 100MB/s
            var verificationThroughput = 200L * 1024 * 1024; // 200MB/s (faster than copy)
            var diskQueueDepth = 2.0;
            var diskResponseTime = 10.0; // 10ms
            var cacheHitRatio = 0.8; // 80%
            var networkBandwidth = 1000L * 1024 * 1024; // 1GB/s

            var metrics = new FileOperationMetrics(
                copyThroughput,
                verificationThroughput,
                diskQueueDepth,
                diskResponseTime,
                cacheHitRatio,
                networkBandwidth,
                DateTime.UtcNow);

            return Task.FromResult(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file operation metrics");
            return Task.FromResult(CreateFallbackFileOperationMetrics());
        }
    }

    private double GetSystemCpuUsage()
    {
        try
        {
            // Use process CPU as approximation for system CPU
            // In a real implementation, you'd query system-wide CPU usage
            return Math.Min(1.0, GetProcessCpuUsage() * Environment.ProcessorCount);
        }
        catch
        {
            return 0.3; // 30% fallback
        }
    }

    private double GetProcessCpuUsage()
    {
        try
        {
            _currentProcess.Refresh();
            var currentTime = DateTime.UtcNow;
            var currentCpuTime = _currentProcess.TotalProcessorTime;

            var timeDiff = currentTime - _lastCpuMeasurement;
            var cpuTimeDiff = currentCpuTime - _lastCpuTime;

            var cpuUsage = cpuTimeDiff.TotalMilliseconds / (timeDiff.TotalMilliseconds * Environment.ProcessorCount);

            _lastCpuMeasurement = currentTime;
            _lastCpuTime = currentCpuTime;

            return Math.Max(0.0, Math.Min(1.0, cpuUsage));
        }
        catch
        {
            return 0.1; // 10% fallback
        }
    }

    private static long GetTotalPhysicalMemory()
    {
        try
        {
            // Use GC info as approximation
            return GC.GetTotalMemory(false) * 8; // Rough estimate
        }
        catch
        {
            return 8L * 1024 * 1024 * 1024; // 8GB fallback
        }
    }

    private static long GetAvailableMemory()
    {
        try
        {
            // Conservative estimate
            return GetTotalPhysicalMemory() / 2;
        }
        catch
        {
            return 2L * 1024 * 1024 * 1024; // 2GB fallback
        }
    }

    private static long GetAvailableDiskSpace()
    {
        try
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
            return drives.Sum(d => d.AvailableFreeSpace);
        }
        catch
        {
            return 100L * 1024 * 1024 * 1024; // 100GB fallback
        }
    }

    private static UtilizationLevel DetermineUtilizationLevel(ResourceUsageMetrics systemMetrics, ResourceUsageMetrics processMetrics)
    {
        // Critical thresholds
        if (systemMetrics.CpuUsage > 0.95 ||
            systemMetrics.AvailableDiskSpaceBytes < 1024L * 1024 * 1024 || // Less than 1GB
            processMetrics.MemoryUsageBytes > 2L * 1024 * 1024 * 1024) // More than 2GB
        {
            return UtilizationLevel.Critical;
        }

        // High thresholds
        if (systemMetrics.CpuUsage > 0.80 ||
            systemMetrics.AvailableDiskSpaceBytes < 5L * 1024 * 1024 * 1024 || // Less than 5GB
            processMetrics.MemoryUsageBytes > 1L * 1024 * 1024 * 1024) // More than 1GB
        {
            return UtilizationLevel.High;
        }

        // Low thresholds
        if (systemMetrics.CpuUsage < 0.30 &&
            systemMetrics.AvailableDiskSpaceBytes > 50L * 1024 * 1024 * 1024 && // More than 50GB
            processMetrics.MemoryUsageBytes < 500L * 1024 * 1024) // Less than 500MB
        {
            return UtilizationLevel.Low;
        }

        return UtilizationLevel.Normal;
    }

    private static string GenerateUtilizationDetails(ResourceUsageMetrics systemMetrics,
        ResourceUsageMetrics processMetrics, UtilizationLevel level)
    {
        var details = new List<string>();

        details.Add($"CPU: {systemMetrics.CpuUsage:P1}");
        details.Add($"Memory: {processMetrics.MemoryUsageBytes / (1024 * 1024):N0}MB");
        details.Add($"Disk Space: {systemMetrics.AvailableDiskSpaceBytes / (1024 * 1024 * 1024):N0}GB available");

        var levelDescription = level switch
        {
            UtilizationLevel.Low => "System resources are underutilized, can increase concurrency",
            UtilizationLevel.Normal => "System resources are normally utilized",
            UtilizationLevel.High => "System resources are highly utilized, consider reducing concurrency",
            UtilizationLevel.Critical => "System resources are critically utilized, must reduce concurrency",
            _ => "Unknown utilization level"
        };

        return $"{levelDescription}. {string.Join(", ", details)}";
    }

    private void NotifyUtilizationCallbacks(ResourceUtilization currentUtilization)
    {
        List<Action<ResourceUtilization>> callbacks;
        lock (_lockObject)
        {
            callbacks = new List<Action<ResourceUtilization>>(_utilizationCallbacks);
        }

        // Only notify if utilization level changed significantly
        if (_lastUtilization == null || _lastUtilization.UtilizationLevel != currentUtilization.UtilizationLevel)
        {
            foreach (var callback in callbacks)
            {
                try
                {
                    callback(currentUtilization);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in utilization change callback");
                }
            }
        }

        _lastUtilization = currentUtilization;
    }

    private static ResourceUsageMetrics CreateFallbackSystemMetrics() =>
        new(0.5, 4L * 1024 * 1024 * 1024, 100, 100 * 1024 * 1024, 10 * 1024 * 1024,
            100L * 1024 * 1024 * 1024, DateTime.UtcNow);

    private static ResourceUsageMetrics CreateFallbackProcessMetrics() =>
        new(0.1, 100L * 1024 * 1024, 0, 10 * 1024 * 1024, 1024 * 1024,
            100L * 1024 * 1024 * 1024, DateTime.UtcNow);

    private static FileOperationMetrics CreateFallbackFileOperationMetrics() =>
        new(100L * 1024 * 1024, 50L * 1024 * 1024, 2.0, 10.0, 0.8,
            100L * 1024 * 1024, DateTime.UtcNow);

    public void Dispose()
    {
        if (_disposed) return;

        _isMonitoring = false;
        _currentProcess?.Dispose();
        _disposed = true;
    }

    private sealed class CallbackSubscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private bool _disposed;

        public CallbackSubscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _unsubscribe();
                _disposed = true;
            }
        }
    }
}