using System.Collections.Concurrent;
using Forker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Adaptive concurrency controller that dynamically adjusts parallelism based on
/// system performance and resource utilization for medical imaging file operations.
/// </summary>
public sealed class AdaptiveConcurrencyController : IConcurrencyController, IDisposable
{
    private readonly IResourceMonitor _resourceMonitor;
    private readonly ILogger<AdaptiveConcurrencyController> _logger;
    private readonly Timer _adjustmentTimer;
    private readonly ConcurrentDictionary<OperationType, ConcurrencyLimits> _concurrencyLimits = new();
    private readonly ConcurrentDictionary<Guid, ConcurrencySlotImpl> _activeSlots = new();
    private readonly ConcurrentDictionary<OperationType, SemaphoreSlim> _semaphores = new();
    private readonly ConcurrentDictionary<OperationType, OperationStatistics> _operationStats = new();
    private readonly object _adjustmentLock = new();

    private bool _disposed;
    private DateTime _lastAdjustment = DateTime.UtcNow;

    public AdaptiveConcurrencyController(IResourceMonitor resourceMonitor,
        ILogger<AdaptiveConcurrencyController> logger)
    {
        _resourceMonitor = resourceMonitor ?? throw new ArgumentNullException(nameof(resourceMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        InitializeDefaultLimits();

        // Start periodic concurrency adjustments every 30 seconds
        _adjustmentTimer = new Timer(async _ => await AdjustConcurrencyLimitsAsync(),
            null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        _logger.LogInformation("Adaptive concurrency controller initialized with default limits");
    }

    public int GetCurrentConcurrency(OperationType operationType)
    {
        return _concurrencyLimits.TryGetValue(operationType, out var limits)
            ? limits.CurrentLimit
            : GetDefaultConcurrency(operationType);
    }

    public async Task<IConcurrencySlot> AcquireSlotAsync(OperationType operationType, TimeSpan estimatedDuration,
        CancellationToken cancellationToken = default)
    {
        var semaphore = _semaphores.GetOrAdd(operationType, _ =>
            new SemaphoreSlim(GetCurrentConcurrency(operationType), GetCurrentConcurrency(operationType)));

        _logger.LogDebug("Attempting to acquire concurrency slot for {OperationType}, estimated duration {EstimatedDuration}",
            operationType, estimatedDuration);

        await semaphore.WaitAsync(cancellationToken);

        try
        {
            var slot = new ConcurrencySlotImpl(operationType, estimatedDuration, this, _logger);
            _activeSlots[slot.SlotId] = slot;

            _logger.LogDebug("Acquired concurrency slot {SlotId} for {OperationType}",
                slot.SlotId, operationType);

            return slot;
        }
        catch
        {
            semaphore.Release();
            throw;
        }
    }

    public async Task ReportOperationCompletionAsync(OperationType operationType, TimeSpan actualDuration,
        bool success, ResourceUsageMetrics resourceUsage)
    {
        _logger.LogDebug("Operation completed: {OperationType}, duration {ActualDuration}, success {Success}",
            operationType, actualDuration, success);

        // Update operation statistics
        var stats = _operationStats.GetOrAdd(operationType, _ => new OperationStatistics());
        stats.RecordOperation(actualDuration, success, resourceUsage);

        // Trigger immediate adjustment if needed based on completion patterns
        if (ShouldTriggerImmediateAdjustment(operationType, stats))
        {
            await AdjustConcurrencyLimitsAsync();
        }
    }

    public Task<ResourceUtilization> GetCurrentResourceUtilizationAsync()
    {
        return _resourceMonitor.GetResourceUtilizationAsync();
    }

    public async Task<ConcurrencyStatistics> GetStatisticsAsync(OperationType? operationType = null,
        DateTime? since = null)
    {
        var sinceTime = since ?? DateTime.UtcNow.AddHours(-1);
        var currentLimits = new Dictionary<OperationType, int>();
        var averageDurations = new Dictionary<OperationType, TimeSpan>();
        var successRates = new Dictionary<OperationType, double>();

        var operationTypes = operationType.HasValue
            ? new[] { operationType.Value }
            : Enum.GetValues<OperationType>();

        foreach (var opType in operationTypes)
        {
            currentLimits[opType] = GetCurrentConcurrency(opType);

            if (_operationStats.TryGetValue(opType, out var stats))
            {
                averageDurations[opType] = stats.GetAverageDuration(sinceTime);
                successRates[opType] = stats.GetSuccessRate(sinceTime);
            }
        }

        var resourceUsage = await _resourceMonitor.GetSystemResourceUsageAsync();
        var operationsInProgress = _activeSlots.Count;

        return new ConcurrencyStatistics(
            currentLimits,
            averageDurations,
            successRates,
            _operationStats.Values.Sum(s => s.CompletedOperations),
            operationsInProgress,
            resourceUsage,
            DateTime.UtcNow,
            sinceTime);
    }

    public Task SetConcurrencyLimitAsync(OperationType operationType, int newLimit, string reason, string adjustedBy)
    {
        if (newLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(newLimit), newLimit, "Concurrency limit must be >= 1");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be null, empty, or whitespace.", nameof(reason));

        if (string.IsNullOrWhiteSpace(adjustedBy))
            throw new ArgumentException("AdjustedBy cannot be null, empty, or whitespace.", nameof(adjustedBy));

        _logger.LogWarning("Manual concurrency adjustment: {OperationType} limit changed to {NewLimit} by {AdjustedBy}: {Reason}",
            operationType, newLimit, adjustedBy, reason);

        lock (_adjustmentLock)
        {
            var limits = _concurrencyLimits.GetOrAdd(operationType, _ => new ConcurrencyLimits());
            limits.SetManualLimit(newLimit, reason, adjustedBy);

            // Update semaphore to match new limit
            UpdateSemaphoreForOperationType(operationType, newLimit);
        }

        return Task.CompletedTask;
    }

    internal void ReleaseSlot(Guid slotId, OperationType operationType)
    {
        if (_activeSlots.TryRemove(slotId, out _))
        {
            if (_semaphores.TryGetValue(operationType, out var semaphore))
            {
                semaphore.Release();
            }

            _logger.LogDebug("Released concurrency slot {SlotId} for {OperationType}", slotId, operationType);
        }
    }

    private void InitializeDefaultLimits()
    {
        // Initialize with conservative defaults for medical imaging workloads
        var defaultLimits = new Dictionary<OperationType, int>
        {
            [OperationType.FileCopy] = 3, // Conservative for large files
            [OperationType.FileVerification] = 5, // Can do more hash calculations
            [OperationType.FileDiscovery] = 10, // Lightweight directory scanning
            [OperationType.FileStabilityCheck] = 8, // Moderate I/O
            [OperationType.DatabaseOperation] = 20, // SQLite can handle more concurrent reads
            [OperationType.FileSystemOperation] = 5 // File moves, deletes, etc.
        };

        foreach (var kvp in defaultLimits)
        {
            _concurrencyLimits[kvp.Key] = new ConcurrencyLimits { CurrentLimit = kvp.Value };
            _semaphores[kvp.Key] = new SemaphoreSlim(kvp.Value, kvp.Value);
        }
    }

    private static int GetDefaultConcurrency(OperationType operationType) => operationType switch
    {
        OperationType.FileCopy => 3,
        OperationType.FileVerification => 5,
        OperationType.FileDiscovery => 10,
        OperationType.FileStabilityCheck => 8,
        OperationType.DatabaseOperation => 20,
        OperationType.FileSystemOperation => 5,
        _ => 3
    };

    private async Task AdjustConcurrencyLimitsAsync()
    {
        if (_disposed) return;

        try
        {
            lock (_adjustmentLock)
            {
                // Limit adjustments to once per 15 seconds to prevent oscillation
                if (DateTime.UtcNow - _lastAdjustment < TimeSpan.FromSeconds(15))
                    return;

                _lastAdjustment = DateTime.UtcNow;
            }

            var utilization = await _resourceMonitor.GetResourceUtilizationAsync();
            var fileMetrics = await _resourceMonitor.GetFileOperationMetricsAsync();

            _logger.LogDebug("Adjusting concurrency limits based on utilization: {UtilizationLevel}",
                utilization.UtilizationLevel);

            foreach (var operationType in Enum.GetValues<OperationType>())
            {
                await AdjustConcurrencyForOperationType(operationType, utilization, fileMetrics);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during concurrency adjustment");
        }
    }

    private Task AdjustConcurrencyForOperationType(OperationType operationType,
        ResourceUtilization utilization, FileOperationMetrics fileMetrics)
    {
        var limits = _concurrencyLimits.GetOrAdd(operationType, _ => new ConcurrencyLimits());

        // Don't adjust if manually set
        if (limits.IsManuallySet) return Task.CompletedTask;

        var currentLimit = limits.CurrentLimit;
        var newLimit = CalculateNewConcurrencyLimit(operationType, currentLimit, utilization, fileMetrics);

        if (newLimit != currentLimit)
        {
            _logger.LogInformation("Adjusting {OperationType} concurrency from {CurrentLimit} to {NewLimit} " +
                                  "due to {UtilizationLevel} utilization",
                operationType, currentLimit, newLimit, utilization.UtilizationLevel);

            limits.CurrentLimit = newLimit;
            UpdateSemaphoreForOperationType(operationType, newLimit);
        }

        return Task.CompletedTask;
    }

    private int CalculateNewConcurrencyLimit(OperationType operationType, int currentLimit,
        ResourceUtilization utilization, FileOperationMetrics fileMetrics)
    {
        var baseFactor = utilization.UtilizationLevel switch
        {
            UtilizationLevel.Low => 1.5, // Increase by 50%
            UtilizationLevel.Normal => 1.0, // No change
            UtilizationLevel.High => 0.7, // Decrease by 30%
            UtilizationLevel.Critical => 0.5, // Decrease by 50%
            _ => 1.0
        };

        // Operation-specific adjustments
        var operationFactor = operationType switch
        {
            OperationType.FileCopy when utilization.SystemMetrics.DiskThroughputBytesPerSecond > 500_000_000 => 0.8, // High disk I/O
            OperationType.FileVerification when utilization.SystemMetrics.CpuUsage > 0.8 => 0.7, // High CPU for hashing
            OperationType.DatabaseOperation when utilization.UtilizationLevel == UtilizationLevel.Low => 1.3, // Can increase DB ops
            _ => 1.0
        };

        // Performance-based adjustments
        var performanceFactor = 1.0;
        if (_operationStats.TryGetValue(operationType, out var stats))
        {
            var recentSuccessRate = stats.GetSuccessRate(DateTime.UtcNow.AddMinutes(-5));
            if (recentSuccessRate < 0.8) // Less than 80% success rate
            {
                performanceFactor = 0.8; // Reduce concurrency
            }
            else if (recentSuccessRate > 0.95) // Greater than 95% success rate
            {
                performanceFactor = 1.1; // Slightly increase concurrency
            }
        }

        var adjustedLimit = (int)Math.Round(currentLimit * baseFactor * operationFactor * performanceFactor);

        // Apply bounds based on operation type
        var (minLimit, maxLimit) = GetConcurrencyBounds(operationType);
        return Math.Max(minLimit, Math.Min(maxLimit, adjustedLimit));
    }

    private static (int MinLimit, int MaxLimit) GetConcurrencyBounds(OperationType operationType) => operationType switch
    {
        OperationType.FileCopy => (1, 8), // Large files need careful concurrency management
        OperationType.FileVerification => (1, 12), // CPU-bound, can be higher
        OperationType.FileDiscovery => (2, 20), // Lightweight, can be high
        OperationType.FileStabilityCheck => (1, 15),
        OperationType.DatabaseOperation => (5, 50), // SQLite handles many concurrent reads
        OperationType.FileSystemOperation => (1, 10),
        _ => (1, 5)
    };

    private void UpdateSemaphoreForOperationType(OperationType operationType, int newLimit)
    {
        if (_semaphores.TryGetValue(operationType, out var currentSemaphore))
        {
            // Create new semaphore with updated limit
            var newSemaphore = new SemaphoreSlim(newLimit, newLimit);
            _semaphores[operationType] = newSemaphore;

            // Dispose old semaphore after a delay to allow pending operations to complete
            _ = Task.Delay(TimeSpan.FromMinutes(2)).ContinueWith(_ => currentSemaphore.Dispose());
        }
    }

    private static bool ShouldTriggerImmediateAdjustment(OperationType operationType, OperationStatistics stats)
    {
        // Trigger immediate adjustment if success rate drops below 70% in last 10 operations
        var recentSuccessRate = stats.GetRecentSuccessRate(10);
        return recentSuccessRate < 0.7;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _adjustmentTimer?.Dispose();

        foreach (var semaphore in _semaphores.Values)
        {
            semaphore.Dispose();
        }

        _disposed = true;
    }

    private sealed class ConcurrencyLimits
    {
        public int CurrentLimit { get; set; } = 1;
        public bool IsManuallySet { get; private set; }
        public string? ManualSetReason { get; private set; }
        public string? ManualSetBy { get; private set; }
        public DateTime? ManualSetAt { get; private set; }

        public void SetManualLimit(int limit, string reason, string setBy)
        {
            CurrentLimit = limit;
            IsManuallySet = true;
            ManualSetReason = reason;
            ManualSetBy = setBy;
            ManualSetAt = DateTime.UtcNow;
        }
    }

    private sealed class OperationStatistics
    {
        private readonly Queue<OperationRecord> _recentOperations = new();
        private readonly object _lock = new();

        public int CompletedOperations { get; private set; }

        public void RecordOperation(TimeSpan duration, bool success, ResourceUsageMetrics resourceUsage)
        {
            lock (_lock)
            {
                _recentOperations.Enqueue(new OperationRecord(DateTime.UtcNow, duration, success, resourceUsage));
                CompletedOperations++;

                // Keep only last 1000 operations
                while (_recentOperations.Count > 1000)
                {
                    _recentOperations.Dequeue();
                }
            }
        }

        public TimeSpan GetAverageDuration(DateTime since)
        {
            lock (_lock)
            {
                var relevantOps = _recentOperations.Where(op => op.CompletedAt >= since).ToList();
                return relevantOps.Count > 0
                    ? TimeSpan.FromMilliseconds(relevantOps.Average(op => op.Duration.TotalMilliseconds))
                    : TimeSpan.Zero;
            }
        }

        public double GetSuccessRate(DateTime since)
        {
            lock (_lock)
            {
                var relevantOps = _recentOperations.Where(op => op.CompletedAt >= since).ToList();
                return relevantOps.Count > 0
                    ? relevantOps.Count(op => op.Success) / (double)relevantOps.Count
                    : 1.0;
            }
        }

        public double GetRecentSuccessRate(int operationCount)
        {
            lock (_lock)
            {
                var recentOps = _recentOperations.TakeLast(operationCount).ToList();
                return recentOps.Count > 0
                    ? recentOps.Count(op => op.Success) / (double)recentOps.Count
                    : 1.0;
            }
        }

        private sealed record OperationRecord(DateTime CompletedAt, TimeSpan Duration, bool Success, ResourceUsageMetrics ResourceUsage);
    }
}