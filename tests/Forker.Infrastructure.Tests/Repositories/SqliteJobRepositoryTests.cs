using Forker.Domain;
using Forker.Infrastructure.Database;
using Forker.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forker.Infrastructure.Tests.Repositories;

/// <summary>
/// Integration tests for SqliteJobRepository.
/// Tests actual database operations with in-memory SQLite.
/// </summary>
public sealed class SqliteJobRepositoryTests : IDisposable
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteJobRepository _repository;
    private readonly string _testDatabasePath;

    public SqliteJobRepositoryTests()
    {
        // Use unique temporary file database for each test
        _testDatabasePath = $"Data Source={Path.GetTempFileName()}.test.db";
        var config = new DatabaseConfiguration
        {
            ConnectionString = _testDatabasePath,
            EnableWalMode = false // Disable WAL for test databases
        };

        var logger = new TestLogger<SqliteConnectionFactory>();
        _connectionFactory = new SqliteConnectionFactory(Options.Create(config), logger);

        var repoLogger = new TestLogger<SqliteJobRepository>();
        _repository = new SqliteJobRepository(_connectionFactory, repoLogger);
    }

    [Fact]
    public async Task SaveAsync_ValidJob_SavesSuccessfully()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        var job = new FileJob(
            FileJobId.New(),
            @"C:\test\file.svs",
            1024L,
            [TargetId.From("TargetA"), TargetId.From("TargetB")]
        );

        // Act
        await _repository.SaveAsync(job);

        // Assert
        var retrieved = await _repository.GetByIdAsync(job.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(job.Id, retrieved.Id);
        Assert.Equal(job.SourcePath, retrieved.SourcePath);
        Assert.Equal(job.InitialSize, retrieved.InitialSize);
        Assert.Equal(JobState.Discovered, retrieved.State);
        Assert.Equal(2, retrieved.RequiredTargets.Count);
    }

    [Fact]
    public async Task SaveAsync_DuplicateId_ThrowsInvalidOperationException()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        var jobId = FileJobId.New();
        var job1 = new FileJob(jobId, @"C:\test\file1.svs", 1024L, [TargetId.From("TargetA")]);
        var job2 = new FileJob(jobId, @"C:\test\file2.svs", 2048L, [TargetId.From("TargetB")]);

        await _repository.SaveAsync(job1);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.SaveAsync(job2));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentJob_ReturnsNull()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        // Act
        var result = await _repository.GetByIdAsync(FileJobId.New());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByStateAsync_FiltersByState_ReturnsCorrectJobs()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        var job1 = new FileJob(FileJobId.New(), @"C:\test\file1.svs", 1024L, [TargetId.From("TargetA")]);
        var job2 = new FileJob(FileJobId.New(), @"C:\test\file2.svs", 2048L, [TargetId.From("TargetB")]);

        await _repository.SaveAsync(job1);
        await _repository.SaveAsync(job2);

        // Act
        var discoveredJobs = await _repository.GetByStateAsync(JobState.Discovered);

        // Assert
        Assert.Equal(2, discoveredJobs.Count);
        Assert.All(discoveredJobs, job => Assert.Equal(JobState.Discovered, job.State));
    }

    [Fact]
    public async Task DeleteAsync_ExistingJob_ReturnsTrue()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        var job = new FileJob(FileJobId.New(), @"C:\test\file.svs", 1024L, [TargetId.From("TargetA")]);
        await _repository.SaveAsync(job);

        // Act
        var deleted = await _repository.DeleteAsync(job.Id);

        // Assert
        Assert.True(deleted);

        var retrieved = await _repository.GetByIdAsync(job.Id);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentJob_ReturnsFalse()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        // Act
        var deleted = await _repository.DeleteAsync(FileJobId.New());

        // Assert
        Assert.False(deleted);
    }

    [Fact]
    public async Task GetJobCountsByStateAsync_ReturnsCorrectCounts()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        var job1 = new FileJob(FileJobId.New(), @"C:\test\file1.svs", 1024L, [TargetId.From("TargetA")]);
        var job2 = new FileJob(FileJobId.New(), @"C:\test\file2.svs", 2048L, [TargetId.From("TargetB")]);

        await _repository.SaveAsync(job1);
        await _repository.SaveAsync(job2);

        // Act
        var counts = await _repository.GetJobCountsByStateAsync();

        // Assert
        Assert.True(counts.ContainsKey(JobState.Discovered));
        Assert.Equal(2, counts[JobState.Discovered]);
    }

    public void Dispose()
    {
        _connectionFactory.Dispose();

        // Clean up test database file
        try
        {
            var dbPath = _testDatabasePath.Replace("Data Source=", "");
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
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