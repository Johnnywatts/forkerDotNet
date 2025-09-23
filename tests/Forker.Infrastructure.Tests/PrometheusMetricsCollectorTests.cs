using Forker.Domain.Services;
using Forker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Forker.Infrastructure.Tests;

/// <summary>
/// Tests for Prometheus metrics collector functionality.
/// Verifies metrics collection, export formatting, and timer operations.
/// </summary>
public sealed class PrometheusMetricsCollectorTests : IDisposable
{
    private readonly ILogger<PrometheusMetricsCollector> _logger;
    private readonly PrometheusMetricsCollector _metricsCollector;

    public PrometheusMetricsCollectorTests()
    {
        _logger = NullLogger<PrometheusMetricsCollector>.Instance;
        _metricsCollector = new PrometheusMetricsCollector(_logger);
    }

    [Fact]
    public void Constructor_ShouldInitializeCoreMetrics()
    {
        // Act
        var metrics = _metricsCollector.GetCurrentMetricsAsync().Result;

        // Assert
        Assert.True(metrics.Count >= 4); // Should have at least the core metrics
        Assert.Contains("forker_operations_started_total", metrics.Keys);
        Assert.Contains("forker_operations_completed_total", metrics.Keys);
        Assert.Contains("forker_active_operations", metrics.Keys);
        Assert.Contains("forker_uptime_seconds", metrics.Keys);
    }

    [Fact]
    public void Counter_ShouldIncrementMetricValue()
    {
        // Arrange
        var metricName = "test_counter";
        var initialValue = 5.0;
        var increment = 3.0;

        // Act
        _metricsCollector.Counter(metricName, initialValue, "Test counter");
        _metricsCollector.Counter(metricName, increment);

        var metrics = _metricsCollector.GetCurrentMetricsAsync().Result;

        // Assert
        Assert.True(metrics.ContainsKey(metricName));
        Assert.Equal(initialValue + increment, metrics[metricName]);
    }

    [Fact]
    public void Counter_WithInvalidName_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _metricsCollector.Counter("", 1.0));
        Assert.Throws<ArgumentException>(() => _metricsCollector.Counter(null!, 1.0));
        Assert.Throws<ArgumentException>(() => _metricsCollector.Counter("   ", 1.0));
    }

    [Fact]
    public void Counter_WithNegativeValue_ShouldThrowArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => _metricsCollector.Counter("test", -1.0));
    }

    [Fact]
    public void Gauge_ShouldSetMetricValue()
    {
        // Arrange
        var metricName = "test_gauge";
        var value1 = 10.0;
        var value2 = 25.0;

        // Act
        _metricsCollector.Gauge(metricName, value1, "Test gauge");
        _metricsCollector.Gauge(metricName, value2);

        var metrics = _metricsCollector.GetCurrentMetricsAsync().Result;

        // Assert
        Assert.True(metrics.ContainsKey(metricName));
        Assert.Equal(value2, metrics[metricName]); // Should be set to latest value
    }

    [Fact]
    public void Histogram_ShouldRecordObservations()
    {
        // Arrange
        var metricName = "test_histogram";
        var values = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };

        // Act
        foreach (var value in values)
        {
            _metricsCollector.Histogram(metricName, value, "Test histogram");
        }

        // Assert - histogram metrics will be exported as _count and _sum
        var prometheusOutput = _metricsCollector.ExportPrometheusMetricsAsync().Result;
        Assert.Contains($"{metricName}_count", prometheusOutput);
        Assert.Contains($"{metricName}_sum", prometheusOutput);
        Assert.Contains("5", prometheusOutput); // count
        Assert.Contains("15", prometheusOutput); // sum (1+2+3+4+5)
    }

    [Fact]
    public void Summary_ShouldRecordObservations()
    {
        // Arrange
        var metricName = "test_summary";
        var values = new[] { 10.0, 20.0, 30.0 };

        // Act
        foreach (var value in values)
        {
            _metricsCollector.Summary(metricName, value, "Test summary");
        }

        // Assert
        var prometheusOutput = _metricsCollector.ExportPrometheusMetricsAsync().Result;
        Assert.Contains($"{metricName}_count", prometheusOutput);
        Assert.Contains($"{metricName}_sum", prometheusOutput);
        Assert.Contains("3", prometheusOutput); // count
        Assert.Contains("60", prometheusOutput); // sum
    }

    [Fact]
    public void Timer_ShouldRecordDurationAndCreateCounterAndHistogram()
    {
        // Arrange
        var timerName = "test_timer";
        var duration = TimeSpan.FromSeconds(2.5);
        var operationType = OperationType.FileCopy;

        // Act
        _metricsCollector.Timer(timerName, duration, operationType, true);

        // Assert
        var prometheusOutput = _metricsCollector.ExportPrometheusMetricsAsync().Result;
        Assert.Contains($"{timerName}_duration_seconds", prometheusOutput);
        Assert.Contains($"{timerName}_total", prometheusOutput);
        Assert.Contains("operation_type=\"FileCopy\"", prometheusOutput);
        Assert.Contains("success=\"true\"", prometheusOutput);
    }

    [Fact]
    public void StartTimer_ShouldReturnValidTimerScope()
    {
        // Arrange
        var timerName = "test_scope_timer";
        var operationType = OperationType.FileVerification;

        // Act
        using var timerScope = _metricsCollector.StartTimer(timerName, operationType);

        // Assert
        Assert.NotNull(timerScope);
        Assert.Equal(timerName, timerScope.Name);
        Assert.Equal(operationType, timerScope.OperationType);
        Assert.True(timerScope.Elapsed >= TimeSpan.Zero);
        Assert.True(timerScope.StartTime <= DateTime.UtcNow);
    }

    [Fact]
    public void TimerScope_WhenDisposed_ShouldRecordDuration()
    {
        // Arrange
        var timerName = "disposal_timer";
        var operationType = OperationType.FileCopy;

        // Act
        using (var timerScope = _metricsCollector.StartTimer(timerName, operationType))
        {
            timerScope.MarkSuccess();
            Thread.Sleep(10); // Small delay to ensure measurable duration
        }

        // Assert
        var prometheusOutput = _metricsCollector.ExportPrometheusMetricsAsync().Result;
        Assert.Contains($"{timerName}_duration_seconds", prometheusOutput);
        Assert.Contains($"{timerName}_total", prometheusOutput);
    }

    [Fact]
    public void TimerScope_AddLabel_ShouldIncludeLabelInOutput()
    {
        // Arrange
        var timerName = "labeled_timer";
        var operationType = OperationType.FileDiscovery;

        // Act
        using (var timerScope = _metricsCollector.StartTimer(timerName, operationType))
        {
            timerScope.AddLabel("file_type", "medical_image");
            timerScope.AddLabel("size_category", "large");
        }

        // Assert
        var prometheusOutput = _metricsCollector.ExportPrometheusMetricsAsync().Result;
        Assert.Contains("file_type=\"medical_image\"", prometheusOutput);
        Assert.Contains("size_category=\"large\"", prometheusOutput);
    }

    [Fact]
    public void RecordFileProcessingMetrics_ShouldCreateMultipleMetrics()
    {
        // Arrange
        var metrics = new FileProcessingMetrics(
            sourcePath: @"C:\test\sample.svs",
            fileSizeBytes: 1_000_000_000L, // 1GB
            operationType: OperationType.FileCopy,
            duration: TimeSpan.FromMinutes(2),
            success: true,
            correlationId: "test-correlation-123",
            completedAt: DateTime.UtcNow,
            retryAttempts: 0,
            targets: new[] { "TargetA", "TargetB" }
        );

        // Act
        _metricsCollector.RecordFileProcessingMetrics(metrics);

        // Assert
        var prometheusOutput = _metricsCollector.ExportPrometheusMetricsAsync().Result;
        Assert.Contains("forker_file_size_bytes", prometheusOutput);
        Assert.Contains("forker_processing_duration_seconds", prometheusOutput);
        Assert.Contains("forker_throughput_bytes_per_second", prometheusOutput);
        Assert.Contains("forker_files_processed_total", prometheusOutput);
        Assert.Contains("file_format=\"svs\"", prometheusOutput);
        Assert.Contains("success=\"true\"", prometheusOutput);
    }

    [Fact]
    public void RecordResourceUtilization_ShouldCreateSystemMetrics()
    {
        // Arrange
        var resourceUsage = new ResourceUsageMetrics(
            cpuUsage: 0.75, // 75%
            memoryUsageBytes: 2_000_000_000L, // 2GB
            diskIopsPerSecond: 150.0,
            diskThroughputBytesPerSecond: 50_000_000L, // 50MB/s
            networkThroughputBytesPerSecond: 10_000_000L, // 10MB/s
            availableDiskSpaceBytes: 500_000_000_000L, // 500GB
            collectedAt: DateTime.UtcNow
        );

        var utilization = new ResourceUtilization(
            resourceUsage,
            resourceUsage,
            UtilizationLevel.Normal,
            "Normal system load"
        );

        // Act
        _metricsCollector.RecordResourceUtilization(utilization);

        // Assert
        var prometheusOutput = _metricsCollector.ExportPrometheusMetricsAsync().Result;
        Assert.Contains("forker_cpu_utilization_percent", prometheusOutput);
        Assert.Contains("forker_memory_utilization_percent", prometheusOutput);
        Assert.Contains("forker_disk_read_bytes_per_second", prometheusOutput);
        Assert.Contains("forker_disk_write_bytes_per_second", prometheusOutput);
        Assert.Contains("forker_available_memory_bytes", prometheusOutput);
        Assert.Contains("forker_available_disk_space_bytes", prometheusOutput);
    }

    [Fact]
    public void MetricsWithLabels_ShouldFormatCorrectlyInPrometheusOutput()
    {
        // Arrange
        var metricName = "test_labeled_metric";
        var labels = new[] { ("environment", "test"), ("service", "forker") };

        // Act
        _metricsCollector.Counter(metricName, 42.0, "Test metric with labels", labels);

        // Assert
        var prometheusOutput = _metricsCollector.ExportPrometheusMetricsAsync().Result;
        Assert.Contains($"{metricName}{{environment=\"test\",service=\"forker\"}} 42", prometheusOutput);
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldReturnBasicStatistics()
    {
        // Arrange
        var since = DateTime.UtcNow.AddHours(-1);
        _metricsCollector.Counter("test1", 1.0);
        _metricsCollector.Gauge("test2", 2.0);

        // Act
        var statistics = await _metricsCollector.GetStatisticsAsync(since);

        // Assert
        Assert.NotNull(statistics);
        Assert.True(statistics.TotalMetrics >= 2);
        Assert.True(statistics.UniqueMetricNames >= 2);
        Assert.True(statistics.CalculatedAt <= DateTime.UtcNow);
        Assert.Equal(since.Date, statistics.WindowStart.Date);
    }

    [Fact]
    public async Task ExportPrometheusMetricsAsync_ShouldIncludeHelpAndTypeComments()
    {
        // Arrange
        _metricsCollector.Counter("help_test_counter", 1.0, "This is a test counter");
        _metricsCollector.Gauge("help_test_gauge", 5.0, "This is a test gauge");

        // Act
        var output = await _metricsCollector.ExportPrometheusMetricsAsync();

        // Assert
        Assert.Contains("# HELP help_test_counter This is a test counter", output);
        Assert.Contains("# TYPE help_test_counter counter", output);
        Assert.Contains("# HELP help_test_gauge This is a test gauge", output);
        Assert.Contains("# TYPE help_test_gauge gauge", output);
    }

    [Fact]
    public void ConcurrentOperations_ShouldBeThreadSafe()
    {
        // Arrange
        var tasks = new Task[10];
        var metricName = "concurrent_test";

        // Act
        for (int i = 0; i < tasks.Length; i++)
        {
            int taskId = i;
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    _metricsCollector.Counter($"{metricName}_{taskId}", 1.0);
                    _metricsCollector.Gauge($"gauge_{taskId}", j);
                }
            });
        }

        Task.WaitAll(tasks);

        // Assert
        var metrics = _metricsCollector.GetCurrentMetricsAsync().Result;
        for (int i = 0; i < tasks.Length; i++)
        {
            Assert.Contains($"{metricName}_{i}", metrics.Keys);
            Assert.Equal(100.0, metrics[$"{metricName}_{i}"]);
        }
    }

    public void Dispose()
    {
        _metricsCollector.Dispose();
    }
}