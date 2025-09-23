using Forker.Domain.Services;
using Forker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Forker.Infrastructure.Tests;

/// <summary>
/// Integration tests for observability service functionality.
/// Tests correlation ID management, operation tracking, and metrics collection.
/// </summary>
public sealed class ObservabilityServiceTests : IDisposable
{
    private readonly ILogger<ObservabilityService> _logger;
    private readonly TestMetricsCollector _metricsCollector;
    private readonly ObservabilityService _observabilityService;

    public ObservabilityServiceTests()
    {
        _logger = NullLogger<ObservabilityService>.Instance;
        _metricsCollector = new TestMetricsCollector();
        _observabilityService = new ObservabilityService(_logger, _metricsCollector);
    }

    [Fact]
    public void CurrentCorrelationId_ShouldGenerateUniqueCorrelationId()
    {
        // Act
        var correlationId1 = _observabilityService.CurrentCorrelationId;
        var correlationId2 = _observabilityService.CurrentCorrelationId;

        // Assert
        Assert.NotNull(correlationId1);
        Assert.NotNull(correlationId2);
        Assert.NotEqual(correlationId1, correlationId2);
        Assert.StartsWith("forker-", correlationId1);
        Assert.StartsWith("forker-", correlationId2);
    }

    [Fact]
    public void StartOperation_ShouldReturnOperationScope_WithCorrectProperties()
    {
        // Arrange
        var operationName = "TestOperation";
        var operationType = OperationType.FileCopy;
        var metadata = new Dictionary<string, object> { { "testKey", "testValue" } };

        // Act
        using var scope = _observabilityService.StartOperation(operationName, operationType, metadata: metadata);

        // Assert
        Assert.NotNull(scope);
        Assert.Equal(operationName, scope.OperationName);
        Assert.Equal(operationType, scope.OperationType);
        Assert.NotNull(scope.CorrelationId);
        Assert.True(scope.Metadata.ContainsKey("testKey"));
        Assert.Equal("testValue", scope.Metadata["testKey"]);
        Assert.True(scope.Elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public void StartOperation_WithInvalidOperationName_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _observabilityService.StartOperation("", OperationType.FileCopy));
        Assert.Throws<ArgumentException>(() =>
            _observabilityService.StartOperation(null!, OperationType.FileCopy));
        Assert.Throws<ArgumentException>(() =>
            _observabilityService.StartOperation("   ", OperationType.FileCopy));
    }

    [Fact]
    public void RecordMetric_ShouldCallMetricsCollector()
    {
        // Arrange
        var metricName = "test_metric";
        var value = 42.5;
        var tags = new Dictionary<string, string> { { "tag1", "value1" } };

        // Act
        _observabilityService.RecordMetric(metricName, value, tags);

        // Assert
        Assert.True(_metricsCollector.GaugeCalls.Count > 0);
        var gaugeCall = _metricsCollector.GaugeCalls.First(c => c.Name == metricName);
        Assert.Equal(value, gaugeCall.Value);
        Assert.Contains(("tag1", "value1"), gaugeCall.Labels);
    }

    [Fact]
    public void RecordMetric_WithInvalidMetricName_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _observabilityService.RecordMetric("", 42.0));
        Assert.Throws<ArgumentException>(() =>
            _observabilityService.RecordMetric(null!, 42.0));
        Assert.Throws<ArgumentException>(() =>
            _observabilityService.RecordMetric("   ", 42.0));
    }

    [Fact]
    public void IncrementCounter_ShouldCallMetricsCollector()
    {
        // Arrange
        var counterName = "test_counter";
        var increment = 5L;
        var tags = new Dictionary<string, string> { { "environment", "test" } };

        // Act
        _observabilityService.IncrementCounter(counterName, increment, tags);

        // Assert
        Assert.True(_metricsCollector.CounterCalls.Count > 0);
        var counterCall = _metricsCollector.CounterCalls.First(c => c.Name == counterName);
        Assert.Equal(increment, counterCall.Value);
        Assert.Contains(("environment", "test"), counterCall.Labels);
    }

    [Fact]
    public void RecordHistogram_ShouldCallMetricsCollector()
    {
        // Arrange
        var histogramName = "test_histogram";
        var value = 123.45;

        // Act
        _observabilityService.RecordHistogram(histogramName, value);

        // Assert
        Assert.True(_metricsCollector.HistogramCalls.Count > 0);
        var histogramCall = _metricsCollector.HistogramCalls.First(c => c.Name == histogramName);
        Assert.Equal(value, histogramCall.Value);
    }

    [Fact]
    public void AddLogContext_ShouldNotThrow()
    {
        // Arrange
        var key = "contextKey";
        var value = new { Property = "value" };

        // Act & Assert
        _observabilityService.AddLogContext(key, value);

        // Should not throw
    }

    [Fact]
    public void AddLogContext_WithInvalidKey_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _observabilityService.AddLogContext("", "value"));
        Assert.Throws<ArgumentException>(() =>
            _observabilityService.AddLogContext(null!, "value"));
        Assert.Throws<ArgumentException>(() =>
            _observabilityService.AddLogContext("   ", "value"));
    }

    [Fact]
    public void RemoveLogContext_ShouldNotThrow()
    {
        // Arrange
        var key = "contextKey";
        _observabilityService.AddLogContext(key, "value");

        // Act & Assert
        _observabilityService.RemoveLogContext(key);

        // Should not throw
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldReturnValidStatistics()
    {
        // Arrange
        var since = DateTime.UtcNow.AddHours(-1);

        // Create and complete some operations to get statistics for
        using (var scope1 = _observabilityService.StartOperation("Operation1", OperationType.FileCopy))
        {
            scope1.MarkSuccess();
        }

        using (var scope2 = _observabilityService.StartOperation("Operation2", OperationType.FileVerification))
        {
            scope2.MarkFailure(new InvalidOperationException("Test failure"));
        }

        // Act
        var statistics = await _observabilityService.GetStatisticsAsync(since);

        // Assert
        Assert.NotNull(statistics);
        Assert.True(statistics.TotalOperations >= 2);
        Assert.Equal(0, statistics.ActiveOperations); // Both scopes have been disposed
        Assert.True(statistics.SuccessfulOperations >= 1);
        Assert.True(statistics.FailedOperations >= 1);
        Assert.True(statistics.CorrelationIdsGenerated >= 2);
        Assert.True(statistics.CalculatedAt <= DateTime.UtcNow);
        Assert.Equal(since.Date, statistics.WindowStart.Date);
    }

    [Fact]
    public void OperationScope_AddMetadata_ShouldAddToMetadata()
    {
        // Arrange
        using var scope = _observabilityService.StartOperation("TestOp", OperationType.FileCopy);

        // Act
        scope.AddMetadata("newKey", "newValue");

        // Assert
        Assert.True(scope.Metadata.ContainsKey("newKey"));
        Assert.Equal("newValue", scope.Metadata["newKey"]);
    }

    [Fact]
    public void OperationScope_RecordCheckpoint_ShouldNotThrow()
    {
        // Arrange
        using var scope = _observabilityService.StartOperation("TestOp", OperationType.FileCopy);

        // Act & Assert
        scope.RecordCheckpoint("checkpoint1");
        scope.RecordCheckpoint("checkpoint2", new Dictionary<string, object> { { "data", 123 } });

        // Should not throw
    }

    [Fact]
    public void OperationScope_RecordProgress_ShouldNotThrow()
    {
        // Arrange
        using var scope = _observabilityService.StartOperation("TestOp", OperationType.FileCopy);

        // Act & Assert
        scope.RecordProgress(0.5, "Halfway complete", TimeSpan.FromMinutes(2));

        // Should not throw
    }

    [Fact]
    public void OperationScope_RecordProgress_WithInvalidPercent_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        using var scope = _observabilityService.StartOperation("TestOp", OperationType.FileCopy);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scope.RecordProgress(-0.1, "Invalid"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scope.RecordProgress(1.1, "Invalid"));
    }

    [Fact]
    public void OperationScope_MarkSuccess_ShouldCompleteSuccessfully()
    {
        // Arrange & Act
        using (var scope = _observabilityService.StartOperation("TestOp", OperationType.FileCopy))
        {
            scope.MarkSuccess("operation result");
        } // Dispose is called here, which triggers the timer

        // Assert
        // Operation should be marked as successful (verified through metrics calls)
        Assert.True(_metricsCollector.TimerCalls.Count > 0);
        var timerCall = _metricsCollector.TimerCalls.First(c => c.Name == "forker_operation_duration");
        Assert.Equal(OperationType.FileCopy, timerCall.OperationType);
        Assert.True(timerCall.Success);
    }

    [Fact]
    public void OperationScope_MarkFailure_ShouldCompleteWithFailure()
    {
        // Arrange & Act
        using (var scope = _observabilityService.StartOperation("TestOp", OperationType.FileCopy))
        {
            var exception = new InvalidOperationException("Test failure");
            scope.MarkFailure(exception);
        } // Dispose is called here, which triggers the timer

        // Assert
        // Operation should be marked as failed (verified through metrics calls)
        Assert.True(_metricsCollector.TimerCalls.Count > 0);
        var timerCall = _metricsCollector.TimerCalls.First(c => c.Name == "forker_operation_duration");
        Assert.Equal(OperationType.FileCopy, timerCall.OperationType);
        Assert.False(timerCall.Success);
    }

    [Fact]
    public void MultipleOperations_ShouldHaveUniqueCorrelationIds()
    {
        // Arrange & Act
        using var scope1 = _observabilityService.StartOperation("Op1", OperationType.FileCopy);
        using var scope2 = _observabilityService.StartOperation("Op2", OperationType.FileVerification);
        using var scope3 = _observabilityService.StartOperation("Op3", OperationType.FileDiscovery);

        // Assert
        Assert.NotEqual(scope1.CorrelationId, scope2.CorrelationId);
        Assert.NotEqual(scope2.CorrelationId, scope3.CorrelationId);
        Assert.NotEqual(scope1.CorrelationId, scope3.CorrelationId);
    }

    [Fact]
    public void NestedOperations_ShouldTrackParentCorrelationId()
    {
        // Arrange & Act
        using var parentScope = _observabilityService.StartOperation("ParentOp", OperationType.FileCopy);
        using var childScope = _observabilityService.StartOperation("ChildOp", OperationType.FileVerification,
            parentScope.CorrelationId);

        // Assert
        Assert.Equal(parentScope.CorrelationId, childScope.ParentCorrelationId);
        Assert.NotEqual(parentScope.CorrelationId, childScope.CorrelationId);
    }

    public void Dispose()
    {
        _observabilityService.Dispose();
    }
}

/// <summary>
/// Test double for IMetricsCollector that records calls for verification.
/// </summary>
public sealed class TestMetricsCollector : IMetricsCollector
{
    public List<CounterCall> CounterCalls { get; } = new();
    public List<GaugeCall> GaugeCalls { get; } = new();
    public List<HistogramCall> HistogramCalls { get; } = new();
    public List<TimerCall> TimerCalls { get; } = new();

    public void Counter(string name, double value = 1, string? help = null, params (string Key, string Value)[] labels)
    {
        CounterCalls.Add(new CounterCall(name, value, help, labels));
    }

    public void Gauge(string name, double value, string? help = null, params (string Key, string Value)[] labels)
    {
        GaugeCalls.Add(new GaugeCall(name, value, help, labels));
    }

    public void Histogram(string name, double value, string? help = null, params (string Key, string Value)[] labels)
    {
        HistogramCalls.Add(new HistogramCall(name, value, help, labels));
    }

    public void Summary(string name, double value, string? help = null, params (string Key, string Value)[] labels)
    {
        // Implementation not needed for these tests
    }

    public void Timer(string name, TimeSpan duration, OperationType operationType, bool success, params (string Key, string Value)[] labels)
    {
        TimerCalls.Add(new TimerCall(name, duration, operationType, success, labels));
    }

    public ITimerScope StartTimer(string name, OperationType operationType, params (string Key, string Value)[] labels)
    {
        return new TestTimerScope(name, operationType, labels, this);
    }

    public void RecordFileProcessingMetrics(FileProcessingMetrics metrics) { }
    public void RecordResourceUtilization(ResourceUtilization utilization) { }
    public Task<IReadOnlyDictionary<string, double>> GetCurrentMetricsAsync() => Task.FromResult<IReadOnlyDictionary<string, double>>(new Dictionary<string, double>());
    public Task<string> ExportPrometheusMetricsAsync() => Task.FromResult("");
    public Task<MetricsStatistics> GetStatisticsAsync(DateTime since, DateTime? until = null) => Task.FromResult(new MetricsStatistics(0, 0, null, null, null, DateTime.UtcNow, since, until ?? DateTime.UtcNow));

    public record CounterCall(string Name, double Value, string? Help, (string Key, string Value)[] Labels);
    public record GaugeCall(string Name, double Value, string? Help, (string Key, string Value)[] Labels);
    public record HistogramCall(string Name, double Value, string? Help, (string Key, string Value)[] Labels);
    public record TimerCall(string Name, TimeSpan Duration, OperationType OperationType, bool Success, (string Key, string Value)[] Labels);
}

public sealed class TestTimerScope : ITimerScope
{
    private readonly TestMetricsCollector _collector;
    private readonly DateTime _startTime;

    public TestTimerScope(string name, OperationType operationType, (string Key, string Value)[] labels, TestMetricsCollector collector)
    {
        Name = name;
        OperationType = operationType;
        Labels = labels.ToDictionary(l => l.Key, l => l.Value);
        _collector = collector;
        _startTime = DateTime.UtcNow;
    }

    public string Name { get; }
    public OperationType OperationType { get; }
    public DateTime StartTime => _startTime;
    public TimeSpan Elapsed => DateTime.UtcNow - _startTime;
    public IReadOnlyDictionary<string, string> Labels { get; }

    public void MarkSuccess() { }
    public void MarkFailure(Exception? exception = null) { }
    public void AddLabel(string key, string value) { }

    public void Dispose()
    {
        _collector.Timer(Name, Elapsed, OperationType, true, Labels.Select(l => (l.Key, l.Value)).ToArray());
    }
}