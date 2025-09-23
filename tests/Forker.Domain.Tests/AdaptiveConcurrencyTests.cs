using Forker.Domain.Services;
using Xunit;

namespace Forker.Domain.Tests;

/// <summary>
/// Unit tests for adaptive concurrency control domain types and validation.
/// </summary>
public class AdaptiveConcurrencyTests
{
    [Fact]
    public void ResourceUsageMetrics_WithValidValues_ShouldCreateSuccessfully()
    {
        // Arrange
        var cpuUsage = 0.5;
        var memoryUsage = 1024L * 1024 * 1024; // 1GB
        var diskIops = 100.0;
        var diskThroughput = 50L * 1024 * 1024; // 50MB/s
        var networkThroughput = 10L * 1024 * 1024; // 10MB/s
        var availableDiskSpace = 100L * 1024 * 1024 * 1024; // 100GB
        var collectedAt = DateTime.UtcNow;

        // Act
        var metrics = new ResourceUsageMetrics(cpuUsage, memoryUsage, diskIops,
            diskThroughput, networkThroughput, availableDiskSpace, collectedAt);

        // Assert
        Assert.Equal(cpuUsage, metrics.CpuUsage);
        Assert.Equal(memoryUsage, metrics.MemoryUsageBytes);
        Assert.Equal(diskIops, metrics.DiskIopsPerSecond);
        Assert.Equal(diskThroughput, metrics.DiskThroughputBytesPerSecond);
        Assert.Equal(networkThroughput, metrics.NetworkThroughputBytesPerSecond);
        Assert.Equal(availableDiskSpace, metrics.AvailableDiskSpaceBytes);
        Assert.Equal(collectedAt, metrics.CollectedAt);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ResourceUsageMetrics_WithInvalidCpuUsage_ShouldThrowArgumentOutOfRangeException(double invalidCpuUsage)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ResourceUsageMetrics(invalidCpuUsage, 1024, 100, 1024, 1024, 1024, DateTime.UtcNow));
        Assert.Contains("CPU usage must be between 0.0 and 1.0", exception.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void ResourceUsageMetrics_WithNegativeMemoryUsage_ShouldThrowArgumentOutOfRangeException(long invalidMemoryUsage)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ResourceUsageMetrics(0.5, invalidMemoryUsage, 100, 1024, 1024, 1024, DateTime.UtcNow));
        Assert.Contains("Memory usage cannot be negative", exception.Message);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(-100.0)]
    public void ResourceUsageMetrics_WithNegativeDiskIops_ShouldThrowArgumentOutOfRangeException(double invalidDiskIops)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ResourceUsageMetrics(0.5, 1024, invalidDiskIops, 1024, 1024, 1024, DateTime.UtcNow));
        Assert.Contains("Disk IOPS cannot be negative", exception.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void ResourceUsageMetrics_WithNegativeThroughput_ShouldThrowArgumentOutOfRangeException(long invalidThroughput)
    {
        // Act & Assert - Test disk throughput
        var diskException = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ResourceUsageMetrics(0.5, 1024, 100, invalidThroughput, 1024, 1024, DateTime.UtcNow));
        Assert.Contains("Throughput cannot be negative", diskException.Message);

        // Act & Assert - Test network throughput
        var networkException = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ResourceUsageMetrics(0.5, 1024, 100, 1024, invalidThroughput, 1024, DateTime.UtcNow));
        Assert.Contains("Throughput cannot be negative", networkException.Message);
    }

    [Fact]
    public void ResourceUtilization_WithValidValues_ShouldCreateSuccessfully()
    {
        // Arrange
        var systemMetrics = CreateValidResourceMetrics();
        var processMetrics = CreateValidResourceMetrics();
        var utilizationLevel = UtilizationLevel.Normal;
        var utilizationDetails = "System is operating normally";

        // Act
        var utilization = new ResourceUtilization(systemMetrics, processMetrics,
            utilizationLevel, utilizationDetails);

        // Assert
        Assert.Equal(systemMetrics, utilization.SystemMetrics);
        Assert.Equal(processMetrics, utilization.ProcessMetrics);
        Assert.Equal(utilizationLevel, utilization.UtilizationLevel);
        Assert.Equal(utilizationDetails, utilization.UtilizationDetails);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResourceUtilization_WithInvalidUtilizationDetails_ShouldThrowArgumentException(string? invalidDetails)
    {
        // Arrange
        var systemMetrics = CreateValidResourceMetrics();
        var processMetrics = CreateValidResourceMetrics();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new ResourceUtilization(systemMetrics, processMetrics, UtilizationLevel.Normal, invalidDetails!));
        Assert.Contains("Utilization details cannot be null, empty, or whitespace", exception.Message);
    }

    [Fact]
    public void ResourceUtilization_WithNullMetrics_ShouldThrowArgumentNullException()
    {
        // Arrange
        var validMetrics = CreateValidResourceMetrics();

        // Act & Assert - System metrics null
        var systemException = Assert.Throws<ArgumentNullException>(() =>
            new ResourceUtilization(null!, validMetrics, UtilizationLevel.Normal, "Details"));

        // Act & Assert - Process metrics null
        var processException = Assert.Throws<ArgumentNullException>(() =>
            new ResourceUtilization(validMetrics, null!, UtilizationLevel.Normal, "Details"));
    }

    [Fact]
    public void ConcurrencyStatistics_WithValidValues_ShouldCreateSuccessfully()
    {
        // Arrange
        var currentLimits = new Dictionary<OperationType, int>
        {
            [OperationType.FileCopy] = 3,
            [OperationType.FileVerification] = 5
        };
        var averageDurations = new Dictionary<OperationType, TimeSpan>
        {
            [OperationType.FileCopy] = TimeSpan.FromMinutes(2),
            [OperationType.FileVerification] = TimeSpan.FromSeconds(30)
        };
        var successRates = new Dictionary<OperationType, double>
        {
            [OperationType.FileCopy] = 0.95,
            [OperationType.FileVerification] = 0.98
        };
        var operationsCompleted = 100;
        var operationsInProgress = 5;
        var averageResourceUsage = CreateValidResourceMetrics();
        var calculatedAt = DateTime.UtcNow;
        var windowStart = calculatedAt.AddHours(-1);

        // Act
        var statistics = new ConcurrencyStatistics(currentLimits, averageDurations, successRates,
            operationsCompleted, operationsInProgress, averageResourceUsage, calculatedAt, windowStart);

        // Assert
        Assert.Equal(2, statistics.CurrentLimits.Count);
        Assert.Equal(3, statistics.CurrentLimits[OperationType.FileCopy]);
        Assert.Equal(2, statistics.AverageDurations.Count);
        Assert.Equal(TimeSpan.FromMinutes(2), statistics.AverageDurations[OperationType.FileCopy]);
        Assert.Equal(2, statistics.SuccessRates.Count);
        Assert.Equal(0.95, statistics.SuccessRates[OperationType.FileCopy]);
        Assert.Equal(operationsCompleted, statistics.OperationsCompleted);
        Assert.Equal(operationsInProgress, statistics.OperationsInProgress);
        Assert.Equal(averageResourceUsage, statistics.AverageResourceUsage);
        Assert.Equal(calculatedAt, statistics.CalculatedAt);
        Assert.Equal(windowStart, statistics.WindowStart);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ConcurrencyStatistics_WithNegativeOperationCounts_ShouldThrowArgumentOutOfRangeException(int invalidCount)
    {
        // Arrange
        var validMetrics = CreateValidResourceMetrics();
        var calculatedAt = DateTime.UtcNow;

        // Act & Assert - Negative completed operations
        var emptyLimits = new Dictionary<OperationType, int>();
        var emptyDurations = new Dictionary<OperationType, TimeSpan>();
        var emptyRates = new Dictionary<OperationType, double>();
        var completedException = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ConcurrencyStatistics(emptyLimits, emptyDurations, emptyRates, invalidCount, 0, validMetrics, calculatedAt, calculatedAt));
        Assert.Contains("Operations completed cannot be negative", completedException.Message);

        // Act & Assert - Negative in-progress operations
        var inProgressException = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ConcurrencyStatistics(emptyLimits, emptyDurations, emptyRates, 0, invalidCount, validMetrics, calculatedAt, calculatedAt));
        Assert.Contains("Operations in progress cannot be negative", inProgressException.Message);
    }

    [Fact]
    public void FileOperationMetrics_WithValidValues_ShouldCreateSuccessfully()
    {
        // Arrange
        var copyThroughput = 100L * 1024 * 1024; // 100MB/s
        var verificationThroughput = 50L * 1024 * 1024; // 50MB/s
        var queueDepth = 2.5;
        var responseTime = 15.0;
        var cacheHitRatio = 0.85;
        var networkBandwidth = 1000L * 1024 * 1024; // 1GB/s
        var collectedAt = DateTime.UtcNow;

        // Act
        var metrics = new FileOperationMetrics(copyThroughput, verificationThroughput,
            queueDepth, responseTime, cacheHitRatio, networkBandwidth, collectedAt);

        // Assert
        Assert.Equal(copyThroughput, metrics.AverageCopyThroughputBytesPerSecond);
        Assert.Equal(verificationThroughput, metrics.AverageVerificationThroughputBytesPerSecond);
        Assert.Equal(queueDepth, metrics.AverageDiskQueueDepth);
        Assert.Equal(responseTime, metrics.AverageDiskResponseTimeMs);
        Assert.Equal(cacheHitRatio, metrics.FileSystemCacheHitRatio);
        Assert.Equal(networkBandwidth, metrics.AvailableNetworkBandwidthBytesPerSecond);
        Assert.Equal(collectedAt, metrics.CollectedAt);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void FileOperationMetrics_WithInvalidCacheHitRatio_ShouldThrowArgumentOutOfRangeException(double invalidRatio)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FileOperationMetrics(1024, 1024, 1.0, 10.0, invalidRatio, 1024, DateTime.UtcNow));
        Assert.Contains("Cache hit ratio must be between 0.0 and 1.0", exception.Message);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(-10.0)]
    public void FileOperationMetrics_WithNegativeQueueDepth_ShouldThrowArgumentOutOfRangeException(double invalidQueueDepth)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FileOperationMetrics(1024, 1024, invalidQueueDepth, 10.0, 0.8, 1024, DateTime.UtcNow));
        Assert.Contains("Queue depth cannot be negative", exception.Message);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(-100.0)]
    public void FileOperationMetrics_WithNegativeResponseTime_ShouldThrowArgumentOutOfRangeException(double invalidResponseTime)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FileOperationMetrics(1024, 1024, 1.0, invalidResponseTime, 0.8, 1024, DateTime.UtcNow));
        Assert.Contains("Response time cannot be negative", exception.Message);
    }

    [Fact]
    public void ResourceUsageSnapshot_WithValidValues_ShouldCreateSuccessfully()
    {
        // Arrange
        var metrics = CreateValidResourceMetrics();
        var utilizationLevel = UtilizationLevel.High;
        var activeOperations = 10;
        var activeOperationsByType = new Dictionary<OperationType, int>
        {
            [OperationType.FileCopy] = 3,
            [OperationType.FileVerification] = 7
        };

        // Act
        var snapshot = new ResourceUsageSnapshot(metrics, utilizationLevel,
            activeOperations, activeOperationsByType);

        // Assert
        Assert.Equal(metrics, snapshot.Metrics);
        Assert.Equal(utilizationLevel, snapshot.UtilizationLevel);
        Assert.Equal(activeOperations, snapshot.ActiveOperations);
        Assert.Equal(2, snapshot.ActiveOperationsByType.Count);
        Assert.Equal(3, snapshot.ActiveOperationsByType[OperationType.FileCopy]);
        Assert.Equal(7, snapshot.ActiveOperationsByType[OperationType.FileVerification]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ResourceUsageSnapshot_WithNegativeActiveOperations_ShouldThrowArgumentOutOfRangeException(int invalidActiveOperations)
    {
        // Arrange
        var metrics = CreateValidResourceMetrics();

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ResourceUsageSnapshot(metrics, UtilizationLevel.Normal, invalidActiveOperations));
        Assert.Contains("Active operations cannot be negative", exception.Message);
    }

    [Fact]
    public void UtilizationLevel_EnumValues_ShouldHaveExpectedValues()
    {
        // Assert - Verify enum values exist
        Assert.True(Enum.IsDefined(typeof(UtilizationLevel), UtilizationLevel.Low));
        Assert.True(Enum.IsDefined(typeof(UtilizationLevel), UtilizationLevel.Normal));
        Assert.True(Enum.IsDefined(typeof(UtilizationLevel), UtilizationLevel.High));
        Assert.True(Enum.IsDefined(typeof(UtilizationLevel), UtilizationLevel.Critical));

        // Assert - Verify enum ordering for logical comparison
        Assert.True(UtilizationLevel.Low < UtilizationLevel.Normal);
        Assert.True(UtilizationLevel.Normal < UtilizationLevel.High);
        Assert.True(UtilizationLevel.High < UtilizationLevel.Critical);
    }

    private static ResourceUsageMetrics CreateValidResourceMetrics()
    {
        return new ResourceUsageMetrics(
            0.5, // 50% CPU
            1024L * 1024 * 1024, // 1GB memory
            100.0, // 100 IOPS
            50L * 1024 * 1024, // 50MB/s disk
            10L * 1024 * 1024, // 10MB/s network
            100L * 1024 * 1024 * 1024, // 100GB available space
            DateTime.UtcNow);
    }
}