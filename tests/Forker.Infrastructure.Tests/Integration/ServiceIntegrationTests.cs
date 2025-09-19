using Forker.Domain;
using Forker.Domain.Repositories;
using Forker.Infrastructure.Database;
using Forker.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forker.Infrastructure.Tests.Integration;

/// <summary>
/// Integration tests that verify the fundamental components work together.
/// These tests validate that our Phase 1-3 implementation is solid before proceeding.
/// </summary>
public sealed class ServiceIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testDatabasePath;

    public ServiceIntegrationTests()
    {
        _testDatabasePath = $"Data Source={Path.GetTempFileName()}.integration.db";

        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Add our infrastructure with test database
        var testConfig = new DatabaseConfiguration
        {
            ConnectionString = _testDatabasePath,
            EnableWalMode = false // Simpler for tests
        };
        services.AddSingleton<IOptions<DatabaseConfiguration>>(new OptionsWrapper<DatabaseConfiguration>(testConfig));
        services.AddForkerInfrastructure();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task ServiceStartup_WithRealDatabase_InitializesSuccessfully()
    {
        // This test verifies the entire service can start with real database initialization

        // Act - Initialize database through service provider
        await _serviceProvider.InitializeForkerDatabaseAsync();

        // Assert - Verify we can get all required services
        var connectionFactory = _serviceProvider.GetRequiredService<ISqliteConnectionFactory>();
        var jobRepository = _serviceProvider.GetRequiredService<IJobRepository>();
        var outcomeRepository = _serviceProvider.GetRequiredService<ITargetOutcomeRepository>();

        Assert.NotNull(connectionFactory);
        Assert.NotNull(jobRepository);
        Assert.NotNull(outcomeRepository);

        // Verify database is actually working by creating a connection
        using var connection = await connectionFactory.CreateOpenConnectionAsync();
        Assert.NotNull(connection);
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task CompleteFileJobWorkflow_CreateSaveRetrieveUpdate_WorksEndToEnd()
    {
        // This test verifies the complete FileJob workflow through all layers

        // Arrange
        await _serviceProvider.InitializeForkerDatabaseAsync();
        var jobRepository = _serviceProvider.GetRequiredService<IJobRepository>();

        var jobId = FileJobId.New();
        var sourcePath = @"C:\test\large-file.svs";
        var targets = new[] { TargetId.From("TargetA"), TargetId.From("TargetB") };

        // Act 1: Create and save job
        var originalJob = new FileJob(jobId, sourcePath, 1024L * 1024L * 500L, targets); // 500MB
        await jobRepository.SaveAsync(originalJob);

        // Act 2: Retrieve job
        var retrievedJob = await jobRepository.GetByIdAsync(jobId);
        Assert.NotNull(retrievedJob);
        Assert.Equal(jobId, retrievedJob.Id);
        Assert.Equal(sourcePath, retrievedJob.SourcePath);
        Assert.Equal(JobState.Discovered, retrievedJob.State);
        Assert.Equal(2, retrievedJob.RequiredTargets.Count);

        // Act 3: Update job state through domain methods
        retrievedJob.SetSourceHash("sha256:abcd1234efgh5678");
        retrievedJob.MarkAsQueued();
        await jobRepository.UpdateAsync(retrievedJob);

        // Act 4: Retrieve updated job
        var updatedJob = await jobRepository.GetByIdAsync(jobId);
        Assert.NotNull(updatedJob);
        Assert.Equal(JobState.Queued, updatedJob.State);
        Assert.Equal("sha256:abcd1234efgh5678", updatedJob.SourceHash);

        // Verify optimistic concurrency worked
        Assert.True(retrievedJob.VersionToken.Value > originalJob.VersionToken.Value);
    }

    [Fact]
    public async Task CrossRepositoryIntegration_FileJobWithTargetOutcomes_WorksWithForeignKeys()
    {
        // This test verifies FileJob and TargetOutcome repositories work together with foreign keys

        // Arrange
        await _serviceProvider.InitializeForkerDatabaseAsync();
        var jobRepository = _serviceProvider.GetRequiredService<IJobRepository>();
        var outcomeRepository = _serviceProvider.GetRequiredService<ITargetOutcomeRepository>();

        var jobId = FileJobId.New();
        var targetA = TargetId.From("TargetA");
        var targetB = TargetId.From("TargetB");

        // Act 1: Create FileJob
        var job = new FileJob(jobId, @"C:\test\file.svs", 2048L, [targetA, targetB]);
        await jobRepository.SaveAsync(job);

        // Act 2: Create TargetOutcomes for this job
        var outcomeA = new TargetOutcome(jobId, targetA);
        var outcomeB = new TargetOutcome(jobId, targetB);

        await outcomeRepository.SaveAsync(outcomeA);
        await outcomeRepository.SaveAsync(outcomeB);

        // Act 3: Retrieve outcomes by job
        var outcomes = await outcomeRepository.GetByJobIdAsync(jobId);
        Assert.Equal(2, outcomes.Count);
        Assert.Contains(outcomes, o => o.TargetId.Value == "TargetA");
        Assert.Contains(outcomes, o => o.TargetId.Value == "TargetB");

        // Act 4: Test foreign key constraint by deleting job (should cascade)
        var deleted = await jobRepository.DeleteAsync(jobId);
        Assert.True(deleted);

        // Verify outcomes were cascade deleted
        var orphanedOutcomes = await outcomeRepository.GetByJobIdAsync(jobId);
        Assert.Empty(orphanedOutcomes);
    }

    [Fact]
    public async Task DatabaseConstraints_EnforceBusinessRules_PreventInvalidData()
    {
        // This test verifies database constraints enforce our business rules

        // Arrange
        await _serviceProvider.InitializeForkerDatabaseAsync();
        var jobRepository = _serviceProvider.GetRequiredService<IJobRepository>();
        var outcomeRepository = _serviceProvider.GetRequiredService<ITargetOutcomeRepository>();

        var jobId = FileJobId.New();
        var targetId = TargetId.From("TargetA");

        // Test 1: Cannot create TargetOutcome without FileJob (foreign key constraint)
        var orphanOutcome = new TargetOutcome(FileJobId.New(), targetId);
        var ex = await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
            () => outcomeRepository.SaveAsync(orphanOutcome));
        Assert.Contains("FOREIGN KEY constraint failed", ex.Message);

        // Test 2: Cannot create duplicate FileJob (primary key constraint)
        var job1 = new FileJob(jobId, @"C:\test\file1.svs", 1024L, [targetId]);
        var job2 = new FileJob(jobId, @"C:\test\file2.svs", 2048L, [targetId]);

        await jobRepository.SaveAsync(job1);
        var duplicateEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => jobRepository.SaveAsync(job2));
        Assert.Contains("already exists", duplicateEx.Message);

        // Test 3: Cannot create duplicate TargetOutcome (composite primary key)
        var outcome1 = new TargetOutcome(jobId, targetId);
        var outcome2 = new TargetOutcome(jobId, targetId);

        await outcomeRepository.SaveAsync(outcome1);
        var duplicateOutcomeEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => outcomeRepository.SaveAsync(outcome2));
        Assert.Contains("already exists", duplicateOutcomeEx.Message);
    }

    [Fact]
    public async Task RepositoryStateCounts_ReflectActualDatabaseData()
    {
        // This test verifies monitoring/statistics methods work correctly

        // Arrange
        await _serviceProvider.InitializeForkerDatabaseAsync();
        var jobRepository = _serviceProvider.GetRequiredService<IJobRepository>();
        var outcomeRepository = _serviceProvider.GetRequiredService<ITargetOutcomeRepository>();

        // Create test data in various states
        var job1 = new FileJob(FileJobId.New(), @"C:\test\file1.svs", 1024L, [TargetId.From("TargetA")]);
        var job2 = new FileJob(FileJobId.New(), @"C:\test\file2.svs", 2048L, [TargetId.From("TargetB")]);

        await jobRepository.SaveAsync(job1);
        await jobRepository.SaveAsync(job2);

        job1.MarkAsQueued();
        await jobRepository.UpdateAsync(job1);

        var outcome1 = new TargetOutcome(job1.Id, TargetId.From("TargetA"));
        var outcome2 = new TargetOutcome(job2.Id, TargetId.From("TargetB"));
        await outcomeRepository.SaveAsync(outcome1);
        await outcomeRepository.SaveAsync(outcome2);

        // Act & Assert
        var jobCounts = await jobRepository.GetJobCountsByStateAsync();
        Assert.Equal(1, jobCounts[JobState.Discovered]); // job2
        Assert.Equal(1, jobCounts[JobState.Queued]);     // job1

        var outcomeCounts = await outcomeRepository.GetOutcomeCountsByStateAsync();
        Assert.Equal(2, outcomeCounts[TargetCopyState.Pending]); // both outcomes
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();

        // Clean up test database
        try
        {
            var dbPath = _testDatabasePath.Replace("Data Source=", "");
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
            // Also clean up WAL and SHM files if they exist
            var walPath = dbPath + "-wal";
            var shmPath = dbPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}