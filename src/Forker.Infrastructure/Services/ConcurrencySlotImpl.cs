using System.Diagnostics;
using Forker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Implementation of concurrency slot that tracks resource usage during an operation.
/// Automatically releases the slot when disposed and reports metrics to the concurrency controller.
/// </summary>
internal sealed class ConcurrencySlotImpl : IConcurrencySlot
{
    private readonly AdaptiveConcurrencyController _controller;
    private readonly ILogger _logger;
    private readonly Stopwatch _stopwatch;

    private bool _disposed;
    private bool _completed;
    private double _percentComplete;
    private ResourceUsageMetrics? _currentResourceUsage;
    private ResourceUsageMetrics? _finalResourceUsage;
    private Exception? _failureException;

    public Guid SlotId { get; } = Guid.NewGuid();
    public OperationType OperationType { get; }
    public DateTime AcquiredAt { get; }
    public TimeSpan EstimatedDuration { get; }

    internal ConcurrencySlotImpl(OperationType operationType, TimeSpan estimatedDuration,
        AdaptiveConcurrencyController controller, ILogger logger)
    {
        OperationType = operationType;
        EstimatedDuration = estimatedDuration;
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        AcquiredAt = DateTime.UtcNow;
        _stopwatch = Stopwatch.StartNew();

        _logger.LogDebug("Concurrency slot {SlotId} created for {OperationType}, estimated duration {EstimatedDuration}",
            SlotId, OperationType, EstimatedDuration);
    }

    public void UpdateProgress(double percentComplete, ResourceUsageMetrics currentResourceUsage)
    {
        if (_disposed || _completed)
            return;

        if (percentComplete < 0.0 || percentComplete > 1.0)
            throw new ArgumentOutOfRangeException(nameof(percentComplete), percentComplete,
                "Percent complete must be between 0.0 and 1.0");

        ArgumentNullException.ThrowIfNull(currentResourceUsage);

        _percentComplete = percentComplete;
        _currentResourceUsage = currentResourceUsage;

        _logger.LogTrace("Concurrency slot {SlotId} progress: {PercentComplete:P1}, " +
                        "CPU: {CpuUsage:P1}, Memory: {MemoryMB}MB",
            SlotId, percentComplete, currentResourceUsage.CpuUsage,
            currentResourceUsage.MemoryUsageBytes / (1024 * 1024));
    }

    public void MarkCompleted(ResourceUsageMetrics finalResourceUsage)
    {
        if (_disposed || _completed)
            return;

        ArgumentNullException.ThrowIfNull(finalResourceUsage);

        _completed = true;
        _finalResourceUsage = finalResourceUsage;
        _stopwatch.Stop();

        _logger.LogDebug("Concurrency slot {SlotId} completed successfully in {ActualDuration}, " +
                        "CPU: {CpuUsage:P1}, Memory: {MemoryMB}MB",
            SlotId, _stopwatch.Elapsed, finalResourceUsage.CpuUsage,
            finalResourceUsage.MemoryUsageBytes / (1024 * 1024));

        // Report completion to controller asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                await _controller.ReportOperationCompletionAsync(OperationType, _stopwatch.Elapsed,
                    true, finalResourceUsage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reporting operation completion for slot {SlotId}", SlotId);
            }
        });
    }

    public void MarkFailed(Exception exception, ResourceUsageMetrics finalResourceUsage)
    {
        if (_disposed || _completed)
            return;

        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(finalResourceUsage);

        _completed = true;
        _failureException = exception;
        _finalResourceUsage = finalResourceUsage;
        _stopwatch.Stop();

        _logger.LogWarning("Concurrency slot {SlotId} failed after {ActualDuration}: {Exception}",
            SlotId, _stopwatch.Elapsed, exception.Message);

        // Report failure to controller asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                await _controller.ReportOperationCompletionAsync(OperationType, _stopwatch.Elapsed,
                    false, finalResourceUsage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reporting operation failure for slot {SlotId}", SlotId);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // If not explicitly completed or failed, mark as completed with current metrics
        if (!_completed)
        {
            _stopwatch.Stop();
            var fallbackMetrics = _currentResourceUsage ?? CreateFallbackMetrics();

            _logger.LogDebug("Concurrency slot {SlotId} disposed without explicit completion after {ActualDuration}",
                SlotId, _stopwatch.Elapsed);

            // Report completion to controller
            _ = Task.Run(async () =>
            {
                try
                {
                    await _controller.ReportOperationCompletionAsync(OperationType, _stopwatch.Elapsed,
                        true, fallbackMetrics);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reporting operation completion on dispose for slot {SlotId}", SlotId);
                }
            });
        }

        // Release the slot from the controller
        _controller.ReleaseSlot(SlotId, OperationType);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;

        // If not explicitly completed or failed, mark as completed with current metrics
        if (!_completed)
        {
            _stopwatch.Stop();
            var fallbackMetrics = _currentResourceUsage ?? CreateFallbackMetrics();

            _logger.LogDebug("Concurrency slot {SlotId} disposed async without explicit completion after {ActualDuration}",
                SlotId, _stopwatch.Elapsed);

            // Report completion to controller
            try
            {
                await _controller.ReportOperationCompletionAsync(OperationType, _stopwatch.Elapsed,
                    true, fallbackMetrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reporting operation completion on async dispose for slot {SlotId}", SlotId);
            }
        }

        // Release the slot from the controller
        _controller.ReleaseSlot(SlotId, OperationType);
    }

    private static ResourceUsageMetrics CreateFallbackMetrics()
    {
        // Create fallback metrics if no resource usage was reported
        return new ResourceUsageMetrics(
            0.1, // Low CPU usage
            50L * 1024 * 1024, // 50MB memory usage
            10, // 10 IOPS
            10L * 1024 * 1024, // 10MB/s disk throughput
            1L * 1024 * 1024, // 1MB/s network throughput
            100L * 1024 * 1024 * 1024, // 100GB available disk space
            DateTime.UtcNow);
    }
}