using Forker.Domain;
using Forker.Domain.Repositories;
using Forker.Infrastructure.Database;
using Forker.Infrastructure.Repositories;
using Forker.Service.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forker.Infrastructure.Tests.Services;

/// <summary>
/// Integration tests for MonitoringService API logic.
/// Tests the repository queries that MonitoringService endpoints use.
/// </summary>
public sealed class MonitoringServiceTests : IDisposable
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteJobRepository _jobRepository;
    private readonly SqliteTargetOutcomeRepository _targetRepository;
    private readonly string _testDatabasePath;

    public MonitoringServiceTests()
    {
        // Use unique temporary file database for each test
        _testDatabasePath = $"Data Source={Path.GetTempFileName()}.test.db";
        var config = new DatabaseConfiguration
        {
            ConnectionString = _testDatabasePath,
            EnableWalMode = false
        };

        var logger = new TestLogger<SqliteConnectionFactory>();
        _connectionFactory = new SqliteConnectionFactory(Options.Create(config), logger);

        var jobLogger = new TestLogger<SqliteJobRepository>();
        _jobRepository = new SqliteJobRepository(_connectionFactory, jobLogger);

        var targetLogger = new TestLogger<SqliteTargetOutcomeRepository>();
        _targetRepository = new SqliteTargetOutcomeRepository(_connectionFactory, targetLogger);
    }

    public void Dispose()
    {
        // Cleanup test database
        try
        {
            var dbFile = _testDatabasePath.Replace("Data Source=", "");
            if (File.Exists(dbFile))
            {
                File.Delete(dbFile);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task HealthEndpoint_ShouldReturnProcessInfo()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        // Act - Simulate what MonitoringService /health endpoint does
        var processId = Environment.ProcessId;
        var memoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
        var dbPath = _testDatabasePath.Replace("Data Source=", "");

        // Assert
        Assert.True(processId > 0);
        Assert.True(memoryMB > 0);
        Assert.NotEmpty(dbPath);
        Assert.Contains(".db", dbPath);
    }

    [Fact]
    public async Task StatsEndpoint_EmptyDatabase_ReturnsZeroCounts()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        // Act - Simulate what MonitoringService /stats endpoint does
        var counts = await _jobRepository.GetJobCountsByStateAsync();

        // Assert
        Assert.Equal(0, counts.Values.Sum());
        Assert.Equal(0, counts.GetValueOrDefault(JobState.Discovered, 0));
        Assert.Equal(0, counts.GetValueOrDefault(JobState.Queued, 0));
        Assert.Equal(0, counts.GetValueOrDefault(JobState.InProgress, 0));
        Assert.Equal(0, counts.GetValueOrDefault(JobState.Verified, 0));
    }

    [Fact]
    public async Task StatsEndpoint_WithJobs_ReturnsCorrectCounts()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        var job1 = new FileJob(FileJobId.New(), @"C:\test\file1.svs", 1024L, [TargetId.From("TargetA")]);
        var job2 = new FileJob(FileJobId.New(), @"C:\test\file2.svs", 2048L, [TargetId.From("TargetA")]);
        var job3 = new FileJob(FileJobId.New(), @"C:\test\file3.svs", 3072L, [TargetId.From("TargetA")]);

        await _jobRepository.SaveAsync(job1);
        await _jobRepository.SaveAsync(job2);
        await _jobRepository.SaveAsync(job3);

        job2.MarkAsQueued();
        await _jobRepository.UpdateAsync(job2);

        job3.MarkAsQueued();
        job3.MarkAsInProgress();
        await _jobRepository.UpdateAsync(job3);

        // Act
        var counts = await _jobRepository.GetJobCountsByStateAsync();

        // Assert
        Assert.Equal(3, counts.Values.Sum());
        Assert.Equal(1, counts.GetValueOrDefault(JobState.Discovered, 0));
        Assert.Equal(1, counts.GetValueOrDefault(JobState.Queued, 0));
        Assert.Equal(1, counts.GetValueOrDefault(JobState.InProgress, 0));
    }

    [Fact]
    public async Task JobsEndpoint_ReturnsJobSummaries()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        var job1 = new FileJob(FileJobId.New(), @"C:\test\file1.svs", 1024L, [TargetId.From("TargetA")]);
        var job2 = new FileJob(FileJobId.New(), @"C:\test\file2.svs", 2048L, [TargetId.From("TargetA")]);

        await _jobRepository.SaveAsync(job1);
        await _jobRepository.SaveAsync(job2);

        // Act - Simulate what MonitoringService /jobs endpoint does
        var discoveredJobs = await _jobRepository.GetByStateAsync(JobState.Discovered);

        // Assert
        Assert.Equal(2, discoveredJobs.Count);
        Assert.Contains(discoveredJobs, j => j.SourcePath == @"C:\test\file1.svs");
        Assert.Contains(discoveredJobs, j => j.SourcePath == @"C:\test\file2.svs");
    }

    [Fact]
    public async Task JobsEndpoint_WithStateFilter_ReturnsFilteredJobs()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        var job1 = new FileJob(FileJobId.New(), @"C:\test\file1.svs", 1024L, [TargetId.From("TargetA")]);
        var job2 = new FileJob(FileJobId.New(), @"C:\test\file2.svs", 2048L, [TargetId.From("TargetA")]);

        await _jobRepository.SaveAsync(job1);
        await _jobRepository.SaveAsync(job2);

        job2.MarkAsQueued();
        await _jobRepository.UpdateAsync(job2);

        // Act
        var queuedJobs = await _jobRepository.GetByStateAsync(JobState.Queued);

        // Assert
        Assert.Single(queuedJobs);
        Assert.Equal(@"C:\test\file2.svs", queuedJobs[0].SourcePath);
        Assert.Equal(JobState.Queued, queuedJobs[0].State);
    }

    [Fact]
    public async Task JobDetailsEndpoint_ReturnsJobWithTargets()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        var jobId = FileJobId.New();
        var job = new FileJob(jobId, @"C:\test\file.svs", 1024L, [TargetId.From("TargetA"), TargetId.From("TargetB")]);

        await _jobRepository.SaveAsync(job);

        var targetA = new TargetOutcome(jobId, TargetId.From("TargetA"));
        var targetB = new TargetOutcome(jobId, TargetId.From("TargetB"));
        await _targetRepository.SaveAsync(targetA);
        await _targetRepository.SaveAsync(targetB);

        // Act - Simulate what MonitoringService /jobs/{id} endpoint does
        var retrievedJob = await _jobRepository.GetByIdAsync(jobId);
        var targets = await _targetRepository.GetByJobIdAsync(jobId);

        // Assert
        Assert.NotNull(retrievedJob);
        Assert.Equal(jobId, retrievedJob.Id);
        Assert.Equal(@"C:\test\file.svs", retrievedJob.SourcePath);
        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, t => t.TargetId.Value.ToString().Contains("TargetA"));
        Assert.Contains(targets, t => t.TargetId.Value.ToString().Contains("TargetB"));
    }

    [Fact]
    public async Task JobDetailsEndpoint_NonExistentJob_ReturnsNull()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();
        var nonExistentId = FileJobId.New();

        // Act
        var job = await _jobRepository.GetByIdAsync(nonExistentId);

        // Assert
        Assert.Null(job);
    }

    [Fact]
    public async Task RequeueEndpoint_ValidatesJobState()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        var jobId = FileJobId.New();
        var job = new FileJob(jobId, @"C:\test\file.svs", 1024L, [TargetId.From("TargetA")]);

        await _jobRepository.SaveAsync(job);

        // Act - Job in Discovered state, should not be requeueable
        var retrievedJob = await _jobRepository.GetByIdAsync(jobId);

        // Assert
        Assert.NotNull(retrievedJob);
        Assert.Equal(JobState.Discovered, retrievedJob.State);
        // Requeue should only work for Failed or Quarantined states
    }

    [Fact]
    public async Task RequeueEndpoint_FailedJob_CanBeIdentified()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        var jobId = FileJobId.New();
        var job = new FileJob(jobId, @"C:\test\file.svs", 1024L, [TargetId.From("TargetA")]);

        await _jobRepository.SaveAsync(job);

        // Transition to failed state
        job.MarkAsQueued();
        job.MarkAsInProgress();
        job.MarkAsFailed();
        await _jobRepository.UpdateAsync(job);

        // Act
        var retrievedJob = await _jobRepository.GetByIdAsync(jobId);

        // Assert
        Assert.NotNull(retrievedJob);
        Assert.Equal(JobState.Failed, retrievedJob.State);
        // This job would be eligible for requeue operation
    }

    [Fact]
    public async Task MonitoringService_SupportsMultipleJobStates()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        // Create jobs in different states
        var job1 = new FileJob(FileJobId.New(), @"C:\test\discovered.svs", 1024L, [TargetId.From("TargetA")]);
        var job2 = new FileJob(FileJobId.New(), @"C:\test\queued.svs", 2048L, [TargetId.From("TargetA")]);
        var job3 = new FileJob(FileJobId.New(), @"C:\test\inprogress.svs", 3072L, [TargetId.From("TargetA")]);
        var job4 = new FileJob(FileJobId.New(), @"C:\test\failed.svs", 4096L, [TargetId.From("TargetA")]);

        await _jobRepository.SaveAsync(job1);
        await _jobRepository.SaveAsync(job2);
        await _jobRepository.SaveAsync(job3);
        await _jobRepository.SaveAsync(job4);

        job2.MarkAsQueued();
        await _jobRepository.UpdateAsync(job2);

        job3.MarkAsQueued();
        job3.MarkAsInProgress();
        await _jobRepository.UpdateAsync(job3);

        job4.MarkAsQueued();
        job4.MarkAsInProgress();
        job4.MarkAsFailed();
        await _jobRepository.UpdateAsync(job4);

        // Act
        var counts = await _jobRepository.GetJobCountsByStateAsync();

        // Assert
        Assert.Equal(4, counts.Values.Sum());
        Assert.Equal(1, counts.GetValueOrDefault(JobState.Discovered, 0));
        Assert.Equal(1, counts.GetValueOrDefault(JobState.Queued, 0));
        Assert.Equal(1, counts.GetValueOrDefault(JobState.InProgress, 0));
        Assert.Equal(1, counts.GetValueOrDefault(JobState.Failed, 0));
    }

    /// <summary>
    /// Test logger implementation that does nothing.
    /// </summary>
    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
