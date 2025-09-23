using System.Collections.Concurrent;
using System.Diagnostics;
using Forker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Implementation of observability service for medical imaging file processing workflows.
/// Provides correlation ID management, structured logging, and operation tracking.
/// </summary>
public sealed class ObservabilityService : IObservabilityService, IDisposable
{
    private readonly ILogger<ObservabilityService> _logger;
    private readonly IMetricsCollector _metricsCollector;
    private readonly ConcurrentDictionary<string, IOperationScope> _activeOperations = new();
    private readonly ConcurrentDictionary<string, object> _logContext = new();
    private readonly ThreadLocal<string?> _currentCorrelationId = new();

    private long _totalOperations;
    private long _successfulOperations;
    private long _failedOperations;
    private long _metricsRecorded;
    private long _correlationIdsGenerated;
    private bool _disposed;

    public string CurrentCorrelationId => _currentCorrelationId.Value ?? GenerateCorrelationId();

    public ObservabilityService(ILogger<ObservabilityService> logger, IMetricsCollector metricsCollector)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));

        _logger.LogInformation("Observability service initialized");
    }

    public IOperationScope StartOperation(string operationName, OperationType operationType,
        string? parentCorrelationId = null, IDictionary<string, object>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(operationName))
            throw new ArgumentException("Operation name cannot be null, empty, or whitespace.", nameof(operationName));

        var correlationId = GenerateCorrelationId();
        var scope = new OperationScopeImpl(correlationId, operationName, operationType, parentCorrelationId,
            metadata, this, _logger, _metricsCollector);

        _activeOperations[correlationId] = scope;
        _currentCorrelationId.Value = correlationId;

        Interlocked.Increment(ref _totalOperations);

        _logger.LogInformation("Started operation {OperationName} [{CorrelationId}] of type {OperationType}",
            operationName, correlationId, operationType);

        // Record operation start metric
        _metricsCollector.Counter("forker_operations_started_total", 1,
            "Total number of operations started",
            ("operation_name", operationName),
            ("operation_type", operationType.ToString()),
            ("has_parent", parentCorrelationId != null ? "true" : "false"));

        return scope;
    }

    public void RecordMetric(string metricName, double value, IDictionary<string, string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(metricName))
            throw new ArgumentException("Metric name cannot be null, empty, or whitespace.", nameof(metricName));

        var labels = tags?.Select(kvp => (kvp.Key, kvp.Value)).ToArray() ?? Array.Empty<(string, string)>();
        _metricsCollector.Gauge(metricName, value, null, labels);

        Interlocked.Increment(ref _metricsRecorded);

        _logger.LogTrace("Recorded metric {MetricName} with value {Value} [{CorrelationId}]",
            metricName, value, CurrentCorrelationId);
    }

    public void IncrementCounter(string counterName, long increment = 1, IDictionary<string, string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(counterName))
            throw new ArgumentException("Counter name cannot be null, empty, or whitespace.", nameof(counterName));

        var labels = tags?.Select(kvp => (kvp.Key, kvp.Value)).ToArray() ?? Array.Empty<(string, string)>();
        _metricsCollector.Counter(counterName, increment, null, labels);

        Interlocked.Increment(ref _metricsRecorded);

        _logger.LogTrace("Incremented counter {CounterName} by {Increment} [{CorrelationId}]",
            counterName, increment, CurrentCorrelationId);
    }

    public void RecordHistogram(string histogramName, double value, IDictionary<string, string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(histogramName))
            throw new ArgumentException("Histogram name cannot be null, empty, or whitespace.", nameof(histogramName));

        var labels = tags?.Select(kvp => (kvp.Key, kvp.Value)).ToArray() ?? Array.Empty<(string, string)>();
        _metricsCollector.Histogram(histogramName, value, null, labels);

        Interlocked.Increment(ref _metricsRecorded);

        _logger.LogTrace("Recorded histogram {HistogramName} with value {Value} [{CorrelationId}]",
            histogramName, value, CurrentCorrelationId);
    }

    public void AddLogContext(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null, empty, or whitespace.", nameof(key));

        _logContext[key] = value;

        _logger.LogTrace("Added log context {Key}={Value} [{CorrelationId}]",
            key, value, CurrentCorrelationId);
    }

    public void RemoveLogContext(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null, empty, or whitespace.", nameof(key));

        _logContext.TryRemove(key, out _);

        _logger.LogTrace("Removed log context {Key} [{CorrelationId}]",
            key, CurrentCorrelationId);
    }

    public async Task<ObservabilityStatistics> GetStatisticsAsync(DateTime? since = null)
    {
        var windowStart = since ?? DateTime.UtcNow.AddHours(-1);
        var activeOperations = _activeOperations.Count;
        var totalOperations = Interlocked.Read(ref _totalOperations);
        var successfulOperations = Interlocked.Read(ref _successfulOperations);
        var failedOperations = Interlocked.Read(ref _failedOperations);
        var metricsRecorded = Interlocked.Read(ref _metricsRecorded);
        var correlationIdsGenerated = Interlocked.Read(ref _correlationIdsGenerated);

        // Get operation durations and success rates by type
        var operationData = _activeOperations.Values
            .GroupBy(op => op.OperationType)
            .ToDictionary(
                g => g.Key,
                g => new { Count = g.Count(), Durations = g.Select(op => op.Elapsed).ToList() }
            );

        var averageDurations = operationData.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Durations.Count > 0
                ? TimeSpan.FromMilliseconds(kvp.Value.Durations.Average(d => d.TotalMilliseconds))
                : TimeSpan.Zero);

        // For success rates, we'd need to track completed operations
        // For now, provide a basic implementation
        var successRates = Enum.GetValues<OperationType>().ToDictionary(
            op => op,
            op => totalOperations > 0 ? successfulOperations / (double)totalOperations : 0.0);

        await Task.CompletedTask; // Placeholder for async operation

        return new ObservabilityStatistics(
            totalOperations,
            activeOperations,
            successfulOperations,
            failedOperations,
            averageDurations,
            successRates,
            metricsRecorded,
            correlationIdsGenerated,
            DateTime.UtcNow,
            windowStart);
    }

    internal void CompleteOperation(string correlationId, bool success)
    {
        _activeOperations.TryRemove(correlationId, out _);

        if (success)
        {
            Interlocked.Increment(ref _successfulOperations);
        }
        else
        {
            Interlocked.Increment(ref _failedOperations);
        }

        _logger.LogDebug("Completed operation [{CorrelationId}] with success={Success}",
            correlationId, success);
    }

    private string GenerateCorrelationId()
    {
        var correlationId = $"forker-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[0..32];
        Interlocked.Increment(ref _correlationIdsGenerated);
        return correlationId;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _currentCorrelationId.Dispose();
        _disposed = true;

        _logger.LogInformation("Observability service disposed");
    }
}

/// <summary>
/// Implementation of operation scope for tracking operation lifecycle and metrics.
/// </summary>
internal sealed class OperationScopeImpl : IOperationScope
{
    private readonly ObservabilityService _observabilityService;
    private readonly ILogger _logger;
    private readonly IMetricsCollector _metricsCollector;
    private readonly Stopwatch _stopwatch;
    private readonly Dictionary<string, object> _metadata;
    private readonly List<OperationCheckpoint> _checkpoints = new();
    private readonly List<OperationProgress> _progressHistory = new();

    private bool _disposed;
    private bool _completed;
    private bool _success;

    public string CorrelationId { get; }
    public string OperationName { get; }
    public OperationType OperationType { get; }
    public DateTime StartTime { get; }
    public string? ParentCorrelationId { get; }
    public IReadOnlyDictionary<string, object> Metadata => _metadata.AsReadOnly();
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    internal OperationScopeImpl(string correlationId, string operationName, OperationType operationType,
        string? parentCorrelationId, IDictionary<string, object>? metadata,
        ObservabilityService observabilityService, ILogger logger, IMetricsCollector metricsCollector)
    {
        CorrelationId = correlationId;
        OperationName = operationName;
        OperationType = operationType;
        ParentCorrelationId = parentCorrelationId;
        StartTime = DateTime.UtcNow;
        _metadata = metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, object>();
        _observabilityService = observabilityService;
        _logger = logger;
        _metricsCollector = metricsCollector;
        _stopwatch = Stopwatch.StartNew();
    }

    public void AddMetadata(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null, empty, or whitespace.", nameof(key));

        _metadata[key] = value;

        _logger.LogTrace("Added metadata {Key}={Value} to operation {OperationName} [{CorrelationId}]",
            key, value, OperationName, CorrelationId);
    }

    public void RecordCheckpoint(string checkpointName, IDictionary<string, object>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(checkpointName))
            throw new ArgumentException("Checkpoint name cannot be null, empty, or whitespace.", nameof(checkpointName));

        var checkpoint = new OperationCheckpoint(checkpointName, DateTime.UtcNow, _stopwatch.Elapsed, metadata);
        _checkpoints.Add(checkpoint);

        _logger.LogDebug("Checkpoint {CheckpointName} recorded for operation {OperationName} [{CorrelationId}] at {ElapsedTime}",
            checkpointName, OperationName, CorrelationId, _stopwatch.Elapsed);

        // Record checkpoint timing metric
        _metricsCollector.Histogram("forker_operation_checkpoint_duration_seconds", _stopwatch.Elapsed.TotalSeconds,
            "Duration to reach operation checkpoint",
            ("operation_name", OperationName),
            ("operation_type", OperationType.ToString()),
            ("checkpoint_name", checkpointName));
    }

    public void MarkSuccess(object? result = null)
    {
        if (_completed) return;

        _success = true;
        _completed = true;

        if (result != null)
        {
            AddMetadata("result", result);
        }

        _logger.LogInformation("Operation {OperationName} [{CorrelationId}] completed successfully in {Duration}",
            OperationName, CorrelationId, _stopwatch.Elapsed);
    }

    public void MarkFailure(Exception exception, IDictionary<string, object>? additionalContext = null)
    {
        if (_completed) return;

        _success = false;
        _completed = true;

        AddMetadata("exception", exception.GetType().Name);
        AddMetadata("error_message", exception.Message);

        if (additionalContext != null)
        {
            foreach (var kvp in additionalContext)
            {
                AddMetadata($"error_context_{kvp.Key}", kvp.Value);
            }
        }

        _logger.LogError(exception, "Operation {OperationName} [{CorrelationId}] failed after {Duration}",
            OperationName, CorrelationId, _stopwatch.Elapsed);
    }

    public void RecordProgress(double percentComplete, string currentStep, TimeSpan? estimatedTimeRemaining = null)
    {
        if (percentComplete < 0.0 || percentComplete > 1.0)
            throw new ArgumentOutOfRangeException(nameof(percentComplete), percentComplete,
                "Percent complete must be between 0.0 and 1.0");

        var progress = new OperationProgress(percentComplete, currentStep, DateTime.UtcNow,
            _stopwatch.Elapsed, estimatedTimeRemaining);
        _progressHistory.Add(progress);

        _logger.LogDebug("Progress {PercentComplete:P1} recorded for operation {OperationName} [{CorrelationId}]: {CurrentStep}",
            percentComplete, OperationName, CorrelationId, currentStep);

        // Record progress metric
        _metricsCollector.Gauge("forker_operation_progress_percent", percentComplete * 100,
            "Current progress percentage of operation",
            ("operation_name", OperationName),
            ("operation_type", OperationType.ToString()),
            ("current_step", currentStep));
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _stopwatch.Stop();

        // If not explicitly completed, mark as success
        if (!_completed)
        {
            MarkSuccess();
        }

        // Record final timing metrics
        _metricsCollector.Timer("forker_operation_duration", _stopwatch.Elapsed, OperationType, _success,
            ("operation_name", OperationName));

        // Complete the operation in the observability service
        _observabilityService.CompleteOperation(CorrelationId, _success);

        _logger.LogInformation("Operation scope disposed for {OperationName} [{CorrelationId}] after {Duration}",
            OperationName, CorrelationId, _stopwatch.Elapsed);
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }
}