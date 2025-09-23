using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Forker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Prometheus-compatible metrics collector for medical imaging file processing workflows.
/// Thread-safe implementation supporting counters, gauges, histograms, and timers.
/// </summary>
public sealed class PrometheusMetricsCollector : IMetricsCollector, IDisposable
{
    private readonly ILogger<PrometheusMetricsCollector> _logger;
    private readonly ConcurrentDictionary<string, MetricFamily> _metrics = new();
    private readonly ConcurrentDictionary<string, TimerScopeImpl> _activeTimers = new();
    private readonly object _lockObject = new();
    private bool _disposed;

    public PrometheusMetricsCollector(ILogger<PrometheusMetricsCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize core system metrics
        InitializeCoreMetrics();

        _logger.LogInformation("Prometheus metrics collector initialized");
    }

    public void Counter(string name, double value = 1, string? help = null, params (string Key, string Value)[] labels)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Metric name cannot be null, empty, or whitespace.", nameof(name));

        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Counter values must be non-negative");

        var metricFamily = GetOrCreateMetricFamily(name, MetricType.Counter, help);
        var labelDict = labels.ToDictionary(l => l.Key, l => l.Value);

        lock (_lockObject)
        {
            var metric = metricFamily.GetOrCreateMetric(labelDict);
            metric.Value += value;
            metric.LastUpdated = DateTime.UtcNow;
        }

        _logger.LogTrace("Incremented counter {MetricName} by {Value} with labels {@Labels}",
            name, value, labelDict);
    }

    public void Gauge(string name, double value, string? help = null, params (string Key, string Value)[] labels)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Metric name cannot be null, empty, or whitespace.", nameof(name));

        var metricFamily = GetOrCreateMetricFamily(name, MetricType.Gauge, help);
        var labelDict = labels.ToDictionary(l => l.Key, l => l.Value);

        lock (_lockObject)
        {
            var metric = metricFamily.GetOrCreateMetric(labelDict);
            metric.Value = value;
            metric.LastUpdated = DateTime.UtcNow;
        }

        _logger.LogTrace("Set gauge {MetricName} to {Value} with labels {@Labels}",
            name, value, labelDict);
    }

    public void Histogram(string name, double value, string? help = null, params (string Key, string Value)[] labels)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Metric name cannot be null, empty, or whitespace.", nameof(name));

        var metricFamily = GetOrCreateMetricFamily(name, MetricType.Histogram, help);
        var labelDict = labels.ToDictionary(l => l.Key, l => l.Value);

        lock (_lockObject)
        {
            var metric = metricFamily.GetOrCreateMetric(labelDict);
            metric.Observations.Add(value);
            metric.LastUpdated = DateTime.UtcNow;
        }

        _logger.LogTrace("Recorded histogram observation {MetricName} value {Value} with labels {@Labels}",
            name, value, labelDict);
    }

    public void Summary(string name, double value, string? help = null, params (string Key, string Value)[] labels)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Metric name cannot be null, empty, or whitespace.", nameof(name));

        var metricFamily = GetOrCreateMetricFamily(name, MetricType.Summary, help);
        var labelDict = labels.ToDictionary(l => l.Key, l => l.Value);

        lock (_lockObject)
        {
            var metric = metricFamily.GetOrCreateMetric(labelDict);
            metric.Observations.Add(value);
            metric.LastUpdated = DateTime.UtcNow;
        }

        _logger.LogTrace("Recorded summary observation {MetricName} value {Value} with labels {@Labels}",
            name, value, labelDict);
    }

    public void Timer(string name, TimeSpan duration, OperationType operationType, bool success,
        params (string Key, string Value)[] labels)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Timer name cannot be null, empty, or whitespace.", nameof(name));

        var durationSeconds = duration.TotalSeconds;
        var allLabels = labels.Concat(new[]
        {
            ("operation_type", operationType.ToString()),
            ("success", success.ToString().ToLowerInvariant())
        }).ToArray();

        // Record as histogram for percentile calculation
        Histogram($"{name}_duration_seconds", durationSeconds,
            $"Duration of {name} operations in seconds", allLabels);

        // Also record as counter for rate calculation
        Counter($"{name}_total", 1, $"Total number of {name} operations", allLabels);

        _logger.LogTrace("Recorded timer {TimerName} duration {Duration} for {OperationType} success={Success}",
            name, duration, operationType, success);
    }

    public ITimerScope StartTimer(string name, OperationType operationType, params (string Key, string Value)[] labels)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Timer name cannot be null, empty, or whitespace.", nameof(name));

        var timerId = Guid.NewGuid().ToString("N");
        var timerScope = new TimerScopeImpl(timerId, name, operationType, labels, this, _logger);

        _activeTimers[timerId] = timerScope;

        _logger.LogTrace("Started timer scope {TimerName} [{TimerId}] for {OperationType}",
            name, timerId, operationType);

        return timerScope;
    }

    public void RecordFileProcessingMetrics(FileProcessingMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var labels = new[]
        {
            ("source_path", Path.GetFileName(metrics.SourcePath)),
            ("operation_type", metrics.OperationType.ToString()),
            ("file_format", metrics.FileFormat),
            ("success", metrics.Success.ToString().ToLowerInvariant())
        };

        // File size distribution
        Histogram("forker_file_size_bytes", metrics.FileSizeBytes,
            "Distribution of processed file sizes in bytes", labels);

        // Processing duration
        Histogram("forker_processing_duration_seconds", metrics.Duration.TotalSeconds,
            "Distribution of file processing durations in seconds", labels);

        // Throughput
        Gauge("forker_throughput_bytes_per_second", metrics.ThroughputBytesPerSecond,
            "Current file processing throughput in bytes per second", labels);

        // Operation counters
        Counter("forker_files_processed_total", 1,
            "Total number of files processed", labels);

        if (!metrics.Success)
        {
            Counter("forker_processing_errors_total", 1,
                "Total number of file processing errors",
                labels.Concat(new[] { ("error_type", metrics.ErrorMessage ?? "unknown") }).ToArray());
        }

        if (metrics.RetryAttempts > 0)
        {
            Histogram("forker_retry_attempts", metrics.RetryAttempts,
                "Distribution of retry attempts per file", labels);
        }

        _logger.LogDebug("Recorded file processing metrics for {SourcePath} [{CorrelationId}] - " +
                        "Size: {FileSizeBytes}B, Duration: {Duration}, Success: {Success}",
            metrics.SourcePath, metrics.CorrelationId, metrics.FileSizeBytes,
            metrics.Duration, metrics.Success);
    }

    public void RecordResourceUtilization(ResourceUtilization utilization)
    {
        ArgumentNullException.ThrowIfNull(utilization);

        var timestamp = DateTime.UtcNow;

        // CPU utilization
        Gauge("forker_cpu_utilization_percent", utilization.CpuUtilizationPercent,
            "Current CPU utilization percentage");

        // Memory utilization
        Gauge("forker_memory_utilization_percent", utilization.MemoryUtilizationPercent,
            "Current memory utilization percentage");

        // Disk I/O rates
        Gauge("forker_disk_read_bytes_per_second", utilization.DiskReadBytesPerSecond,
            "Current disk read rate in bytes per second");

        Gauge("forker_disk_write_bytes_per_second", utilization.DiskWriteBytesPerSecond,
            "Current disk write rate in bytes per second");

        // Available resources
        Gauge("forker_available_memory_bytes", utilization.AvailableMemoryBytes,
            "Available system memory in bytes");

        Gauge("forker_available_disk_space_bytes", utilization.AvailableDiskSpaceBytes,
            "Available disk space in bytes");

        _logger.LogTrace("Recorded resource utilization - CPU: {CpuPercent}%, Memory: {MemoryPercent}%, " +
                        "Disk Read: {DiskReadRate}B/s, Disk Write: {DiskWriteRate}B/s",
            utilization.CpuUtilizationPercent, utilization.MemoryUtilizationPercent,
            utilization.DiskReadBytesPerSecond, utilization.DiskWriteBytesPerSecond);
    }

    public async Task<IReadOnlyDictionary<string, double>> GetCurrentMetricsAsync()
    {
        await Task.CompletedTask;

        var result = new Dictionary<string, double>();

        lock (_lockObject)
        {
            foreach (var family in _metrics.Values)
            {
                foreach (var metric in family.Metrics.Values)
                {
                    var key = family.Name;
                    if (metric.Labels.Count > 0)
                    {
                        var labelString = string.Join(",", metric.Labels.Select(l => $"{l.Key}={l.Value}"));
                        key = $"{family.Name}{{{labelString}}}";
                    }

                    result[key] = metric.Value;
                }
            }
        }

        return result.AsReadOnly();
    }

    public async Task<string> ExportPrometheusMetricsAsync()
    {
        await Task.CompletedTask;

        var output = new StringBuilder();

        lock (_lockObject)
        {
            foreach (var family in _metrics.Values.OrderBy(f => f.Name))
            {
                // Write help text
                if (!string.IsNullOrEmpty(family.Help))
                {
                    output.AppendLine(CultureInfo.InvariantCulture, $"# HELP {family.Name} {family.Help}");
                }

                // Write type
                output.AppendLine(CultureInfo.InvariantCulture, $"# TYPE {family.Name} {family.Type.ToString().ToLowerInvariant()}");

                // Write metrics
                foreach (var metric in family.Metrics.Values.OrderBy(m => string.Join(",", m.Labels.Select(l => $"{l.Key}={l.Value}"))))
                {
                    var labelString = metric.Labels.Count > 0
                        ? "{" + string.Join(",", metric.Labels.Select(l => $"{l.Key}=\"{l.Value}\"")) + "}"
                        : "";

                    if (family.Type == MetricType.Histogram || family.Type == MetricType.Summary)
                    {
                        // For histograms and summaries, we'd normally output buckets/quantiles
                        // For simplicity, just output count and sum for now
                        output.AppendLine(CultureInfo.InvariantCulture, $"{family.Name}_count{labelString} {metric.Observations.Count}");
                        if (metric.Observations.Count > 0)
                        {
                            output.AppendLine(CultureInfo.InvariantCulture, $"{family.Name}_sum{labelString} {metric.Observations.Sum()}");
                        }
                    }
                    else
                    {
                        output.AppendLine(CultureInfo.InvariantCulture, $"{family.Name}{labelString} {metric.Value}");
                    }
                }

                output.AppendLine();
            }
        }

        return output.ToString();
    }

    public async Task<MetricsStatistics> GetStatisticsAsync(DateTime since, DateTime? until = null)
    {
        var endTime = until ?? DateTime.UtcNow;

        // For this basic implementation, we'll provide simple statistics
        // In a production system, you'd likely store historical data

        await Task.CompletedTask;

        lock (_lockObject)
        {
            var totalMetrics = _metrics.Values.Sum(f => f.Metrics.Count);
            var uniqueMetricNames = _metrics.Count;

            // Basic implementation - in practice you'd calculate these from stored data
            var processingStatsByType = new Dictionary<OperationType, FileProcessingStats>();
            var performanceStatsByFormat = new Dictionary<string, PerformanceStats>();
            var errorRatesByType = new Dictionary<OperationType, double>();

            return new MetricsStatistics(
                totalMetrics,
                uniqueMetricNames,
                processingStatsByType,
                performanceStatsByFormat,
                errorRatesByType,
                DateTime.UtcNow,
                since,
                endTime);
        }
    }

    internal void CompleteTimer(string timerId, bool success)
    {
        if (_activeTimers.TryRemove(timerId, out var timerScope))
        {
            _logger.LogTrace("Completed timer scope {TimerName} [{TimerId}] with success={Success} after {Duration}",
                timerScope.Name, timerId, success, timerScope.Elapsed);
        }
    }

    private void InitializeCoreMetrics()
    {
        // Initialize core system metrics that should always be present
        Counter("forker_operations_started_total", 0, "Total number of operations started");
        Counter("forker_operations_completed_total", 0, "Total number of operations completed");
        Gauge("forker_active_operations", 0, "Number of operations currently in progress");
        Gauge("forker_uptime_seconds", 0, "Service uptime in seconds");
    }

    private MetricFamily GetOrCreateMetricFamily(string name, MetricType type, string? help)
    {
        return _metrics.GetOrAdd(name, _ => new MetricFamily(name, type, help));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Clean up any active timers
        foreach (var timer in _activeTimers.Values)
        {
            timer.Dispose();
        }
        _activeTimers.Clear();

        _logger.LogInformation("Prometheus metrics collector disposed");
    }
}

/// <summary>
/// Represents a family of metrics with the same name and type.
/// </summary>
internal sealed class MetricFamily
{
    public string Name { get; }
    public MetricType Type { get; }
    public string? Help { get; }
    public ConcurrentDictionary<string, Metric> Metrics { get; } = new();

    public MetricFamily(string name, MetricType type, string? help)
    {
        Name = name;
        Type = type;
        Help = help;
    }

    public Metric GetOrCreateMetric(Dictionary<string, string> labels)
    {
        var labelKey = string.Join(",", labels.OrderBy(l => l.Key).Select(l => $"{l.Key}={l.Value}"));
        return Metrics.GetOrAdd(labelKey, _ => new Metric(labels));
    }
}

/// <summary>
/// Represents an individual metric with labels and values.
/// </summary>
internal sealed class Metric
{
    public Dictionary<string, string> Labels { get; }
    public double Value { get; set; }
    public List<double> Observations { get; } = new();
    public DateTime LastUpdated { get; set; }

    public Metric(Dictionary<string, string> labels)
    {
        Labels = new Dictionary<string, string>(labels);
        LastUpdated = DateTime.UtcNow;
    }
}

/// <summary>
/// Metric type enumeration for Prometheus compatibility.
/// </summary>
internal enum MetricType
{
    Counter,
    Gauge,
    Histogram,
    Summary
}

/// <summary>
/// Timer scope implementation that records duration when disposed.
/// </summary>
internal sealed class TimerScopeImpl : ITimerScope
{
    private readonly string _timerId;
    private readonly PrometheusMetricsCollector _collector;
    private readonly ILogger _logger;
    private readonly Stopwatch _stopwatch;
    private readonly Dictionary<string, string> _labels;
    private bool _disposed;
    private bool _success = true;
    private Exception? _failure;

    public string Name { get; }
    public OperationType OperationType { get; }
    public DateTime StartTime { get; }
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public IReadOnlyDictionary<string, string> Labels => _labels.AsReadOnly();

    internal TimerScopeImpl(string timerId, string name, OperationType operationType,
        (string Key, string Value)[] labels, PrometheusMetricsCollector collector, ILogger logger)
    {
        _timerId = timerId;
        Name = name;
        OperationType = operationType;
        StartTime = DateTime.UtcNow;
        _collector = collector;
        _logger = logger;
        _stopwatch = Stopwatch.StartNew();
        _labels = labels.ToDictionary(l => l.Key, l => l.Value);
    }

    public void MarkSuccess()
    {
        _success = true;
        _failure = null;
        _logger.LogTrace("Timer scope {TimerName} [{TimerId}] marked as successful", Name, _timerId);
    }

    public void MarkFailure(Exception? exception = null)
    {
        _success = false;
        _failure = exception;
        _logger.LogTrace("Timer scope {TimerName} [{TimerId}] marked as failed: {Exception}",
            Name, _timerId, exception?.Message);
    }

    public void AddLabel(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Label key cannot be null, empty, or whitespace.", nameof(key));

        _labels[key] = value ?? string.Empty;
        _logger.LogTrace("Added label {Key}={Value} to timer scope {TimerName} [{TimerId}]",
            key, value, Name, _timerId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stopwatch.Stop();

        // Record the timing with all current labels
        var allLabels = _labels.Select(l => (l.Key, l.Value)).ToArray();
        _collector.Timer(Name, Elapsed, OperationType, _success, allLabels);

        // Complete the timer in the collector
        _collector.CompleteTimer(_timerId, _success);
    }
}