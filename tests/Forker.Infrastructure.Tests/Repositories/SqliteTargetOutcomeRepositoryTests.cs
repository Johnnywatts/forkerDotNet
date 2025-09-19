using Forker.Domain;
using Forker.Infrastructure.Database;
using Forker.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forker.Infrastructure.Tests.Repositories;

/// <summary>
/// Integration tests for SqliteTargetOutcomeRepository.
/// Tests actual database operations with in-memory SQLite.
/// </summary>
public sealed class SqliteTargetOutcomeRepositoryTests : IDisposable
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SqliteTargetOutcomeRepository _repository;
    private readonly SqliteJobRepository _jobRepository;
    private readonly FileJobId _testJobId;
    private readonly TargetId _testTargetId;

    public SqliteTargetOutcomeRepositoryTests()
    {
        // Use unique temporary file database for each test
        var testDatabasePath = $"Data Source={Path.GetTempFileName()}.test.db";
        var config = new DatabaseConfiguration
        {
            ConnectionString = testDatabasePath,
            EnableWalMode = false // Disable WAL for test databases
        };

        var logger = new TestLogger<SqliteConnectionFactory>();
        _connectionFactory = new SqliteConnectionFactory(Options.Create(config), logger);

        var repoLogger = new TestLogger<SqliteTargetOutcomeRepository>();
        _repository = new SqliteTargetOutcomeRepository(_connectionFactory, repoLogger);

        var jobRepoLogger = new TestLogger<SqliteJobRepository>();
        _jobRepository = new SqliteJobRepository(_connectionFactory, jobRepoLogger);

        _testJobId = FileJobId.New();
        _testTargetId = TargetId.From("TargetA");
    }

    [Fact]
    public async Task SaveAsync_ValidOutcome_SavesSuccessfully()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();
        await CreateRequiredFileJobAsync(_testJobId, _testTargetId);

        var outcome = new TargetOutcome(_testJobId, _testTargetId);

        // Act
        await _repository.SaveAsync(outcome);

        // Assert
        var retrieved = await _repository.GetByIdAsync(_testJobId, _testTargetId);
        Assert.NotNull(retrieved);
        Assert.Equal(_testJobId, retrieved.JobId);
        Assert.Equal(_testTargetId, retrieved.TargetId);
        Assert.Equal(TargetCopyState.Pending, retrieved.CopyState);
        Assert.Equal(0, retrieved.Attempts);
    }

    [Fact]
    public async Task SaveAsync_DuplicateId_ThrowsInvalidOperationException()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();
        await CreateRequiredFileJobAsync(_testJobId, _testTargetId);

        var outcome1 = new TargetOutcome(_testJobId, _testTargetId);
        var outcome2 = new TargetOutcome(_testJobId, _testTargetId);

        await _repository.SaveAsync(outcome1);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.SaveAsync(outcome2));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentOutcome_ReturnsNull()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();

        // Act
        var result = await _repository.GetByIdAsync(FileJobId.New(), TargetId.From("NonExistent"));

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByJobIdAsync_ReturnsAllOutcomesForJob()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();
        var targetA = TargetId.From("TargetA");
        var targetB = TargetId.From("TargetB");
        await CreateRequiredFileJobAsync(_testJobId, targetA);
        await CreateRequiredFileJobAsync(_testJobId, targetB);

        var outcome1 = new TargetOutcome(_testJobId, targetA);
        var outcome2 = new TargetOutcome(_testJobId, targetB);

        await _repository.SaveAsync(outcome1);
        await _repository.SaveAsync(outcome2);

        // Act
        var outcomes = await _repository.GetByJobIdAsync(_testJobId);

        // Assert
        Assert.Equal(2, outcomes.Count);
        Assert.Contains(outcomes, o => o.TargetId.Value == "TargetA");
        Assert.Contains(outcomes, o => o.TargetId.Value == "TargetB");
    }

    [Fact]
    public async Task GetByCopyStateAsync_FiltersByState_ReturnsCorrectOutcomes()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();
        var jobId2 = FileJobId.New();
        var targetA = TargetId.From("TargetA");
        var targetB = TargetId.From("TargetB");
        await CreateRequiredFileJobAsync(_testJobId, targetA);
        await CreateRequiredFileJobAsync(jobId2, targetB);

        var outcome1 = new TargetOutcome(_testJobId, targetA);
        var outcome2 = new TargetOutcome(jobId2, targetB);

        await _repository.SaveAsync(outcome1);
        await _repository.SaveAsync(outcome2);

        // Act
        var pendingOutcomes = await _repository.GetByCopyStateAsync(TargetCopyState.Pending);

        // Assert
        Assert.Equal(2, pendingOutcomes.Count);
        Assert.All(pendingOutcomes, outcome => Assert.Equal(TargetCopyState.Pending, outcome.CopyState));
    }

    [Fact]
    public async Task GetByTargetIdAsync_ReturnsAllOutcomesForTarget()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();
        var jobId1 = FileJobId.New();
        var jobId2 = FileJobId.New();
        await CreateRequiredFileJobAsync(jobId1, _testTargetId);
        await CreateRequiredFileJobAsync(jobId2, _testTargetId);

        var outcome1 = new TargetOutcome(jobId1, _testTargetId);
        var outcome2 = new TargetOutcome(jobId2, _testTargetId);

        await _repository.SaveAsync(outcome1);
        await _repository.SaveAsync(outcome2);

        // Act
        var outcomes = await _repository.GetByTargetIdAsync(_testTargetId);

        // Assert
        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, outcome => Assert.Equal(_testTargetId, outcome.TargetId));
    }

    [Fact]
    public async Task GetOutcomeCountsByStateAsync_ReturnsCorrectCounts()
    {
        // Arrange
        await _connectionFactory.InitializeDatabaseAsync();
        var jobId2 = FileJobId.New();
        var targetA = TargetId.From("TargetA");
        var targetB = TargetId.From("TargetB");
        await CreateRequiredFileJobAsync(_testJobId, targetA);
        await CreateRequiredFileJobAsync(jobId2, targetB);

        var outcome1 = new TargetOutcome(_testJobId, targetA);
        var outcome2 = new TargetOutcome(jobId2, targetB);

        await _repository.SaveAsync(outcome1);
        await _repository.SaveAsync(outcome2);

        // Act
        var counts = await _repository.GetOutcomeCountsByStateAsync();

        // Assert
        Assert.True(counts.ContainsKey(TargetCopyState.Pending));
        Assert.Equal(2, counts[TargetCopyState.Pending]);
    }

    public void Dispose()
    {
        _connectionFactory.Dispose();
    }

    /// <summary>
    /// Helper method to create a FileJob that can be referenced by TargetOutcomes.
    /// </summary>
    private async Task CreateRequiredFileJobAsync(FileJobId jobId, TargetId targetId)
    {
        // Check if job already exists to avoid duplicate key errors
        var existingJob = await _jobRepository.GetByIdAsync(jobId);
        if (existingJob == null)
        {
            var job = new FileJob(jobId, @"C:\test\file.svs", 1024L, [targetId]);
            await _jobRepository.SaveAsync(job);
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