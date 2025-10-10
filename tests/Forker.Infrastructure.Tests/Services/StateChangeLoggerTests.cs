using Forker.Domain.Services;
using Forker.Infrastructure.Configuration;
using Forker.Infrastructure.Database;
using Forker.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Forker.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for StateChangeLogger service.
/// Tests state change logging, history retrieval, and cleanup operations.
/// </summary>
[Collection("StateChangeLogger")] // Disable parallel execution
public sealed class StateChangeLoggerTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly SqliteConnection _connection;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<StateChangeLogger> _logger;
    private readonly StateChangeLoggingConfig _config;

    public StateChangeLoggerTests(ITestOutputHelper output)
    {
        _output = output;

        // Create in-memory SQLite database for testing
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Create test connection factory
        _connectionFactory = new TestConnectionFactory(_connection);

        // Create test logger
        _logger = new TestLogger<StateChangeLogger>(output);

        // Default configuration for tests
        _config = new StateChangeLoggingConfig
        {
            Enabled = true,
            MaxRecords = 100000,
            AutoCleanupEnabled = true,
            RetentionDays = 90,
            IncludeAdditionalContext = true,
            LogToFile = false,
            LogFilePath = "C:\\test\\state-changes.txt"
        };

        // Initialize database schema
        InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private async Task InitializeDatabaseAsync()
    {
        // Create StateChangeLog table
        var createTableSql = """
            CREATE TABLE IF NOT EXISTS StateChangeLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                JobId TEXT NOT NULL,
                EntityType TEXT NOT NULL,
                EntityId TEXT,
                OldState TEXT,
                NewState TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                DurationMs INTEGER,
                AdditionalContext TEXT,
                CONSTRAINT chk_entity_type CHECK (EntityType IN ('Job', 'Target'))
            );

            CREATE INDEX IF NOT EXISTS idx_statelog_jobid ON StateChangeLog(JobId);
            CREATE INDEX IF NOT EXISTS idx_statelog_timestamp ON StateChangeLog(Timestamp);
            CREATE INDEX IF NOT EXISTS ix_statelog_entity ON StateChangeLog(EntityType, EntityId);
            CREATE INDEX IF NOT EXISTS idx_statelog_newstate ON StateChangeLog(NewState);
            """;

        await using var command = _connection.CreateCommand();
        command.CommandText = createTableSql;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task LogJobStateChangeAsync_LogsSuccessfully()
    {
        // Arrange
        var logger = CreateLogger();
        var jobId = "test-job-123";

        // Act
        await logger.LogJobStateChangeAsync(jobId, null, "Discovered", "{\"sourceFile\": \"test.svs\"}");

        // Assert
        var history = await logger.GetJobHistoryAsync(jobId);
        Assert.Single(history);
        Assert.Equal("Discovered", history[0].NewState);
        Assert.Null(history[0].OldState);
        Assert.Equal("Job", history[0].EntityType);
        Assert.Null(history[0].EntityId);
        Assert.Contains("sourceFile", history[0].AdditionalContext ?? "");
    }

    [Fact]
    public async Task LogJobStateChangeAsync_CalculatesDuration()
    {
        // Arrange
        var logger = CreateLogger();
        var jobId = "test-job-456";

        // Act
        await logger.LogJobStateChangeAsync(jobId, null, "Discovered");
        await Task.Delay(100); // Wait 100ms
        await logger.LogJobStateChangeAsync(jobId, "Discovered", "Queued");

        // Assert
        var history = await logger.GetJobHistoryAsync(jobId);
        Assert.Equal(2, history.Count);
        Assert.Null(history[0].DurationMs); // First state has no duration
        Assert.NotNull(history[1].DurationMs);
        Assert.True(history[1].DurationMs >= 100, $"Expected duration >= 100ms, got {history[1].DurationMs}ms");
    }

    [Fact]
    public async Task LogTargetStateChangeAsync_LogsSuccessfully()
    {
        // Arrange
        var logger = CreateLogger();
        var jobId = "test-job-789";
        var targetId = "TargetA";

        // Act
        await logger.LogTargetStateChangeAsync(jobId, targetId, null, "Pending");
        await logger.LogTargetStateChangeAsync(jobId, targetId, "Pending", "Copying");
        await logger.LogTargetStateChangeAsync(jobId, targetId, "Copying", "Copied");

        // Assert
        var history = await logger.GetJobHistoryAsync(jobId);
        Assert.Equal(3, history.Count);
        Assert.All(history, entry => Assert.Equal("Target", entry.EntityType));
        Assert.All(history, entry => Assert.Equal(targetId, entry.EntityId));
        Assert.Equal("Pending", history[0].NewState);
        Assert.Equal("Copying", history[1].NewState);
        Assert.Equal("Copied", history[2].NewState);
    }

    [Fact]
    public async Task LogTargetStateChangeAsync_CalculatesDurationPerTarget()
    {
        // Arrange
        var logger = CreateLogger();
        var jobId = "test-job-multi";

        // Act
        await logger.LogTargetStateChangeAsync(jobId, "TargetA", null, "Pending");
        await Task.Delay(50);
        await logger.LogTargetStateChangeAsync(jobId, "TargetB", null, "Pending");
        await Task.Delay(50);
        await logger.LogTargetStateChangeAsync(jobId, "TargetA", "Pending", "Copying");

        // Assert
        var history = await logger.GetJobHistoryAsync(jobId);
        Assert.Equal(3, history.Count);

        // TargetA first state should have no duration
        Assert.Null(history[0].DurationMs);

        // TargetB first state should have no duration
        Assert.Null(history[1].DurationMs);

        // TargetA second state should have duration >= 100ms (initial + delay before TargetB + delay before this)
        Assert.NotNull(history[2].DurationMs);
        Assert.True(history[2].DurationMs >= 100, $"Expected duration >= 100ms, got {history[2].DurationMs}ms");
    }

    [Fact]
    public async Task GetJobHistoryAsync_ReturnsInChronologicalOrder()
    {
        // Arrange
        var logger = CreateLogger();
        var jobId = "test-job-order";

        // Act
        await logger.LogJobStateChangeAsync(jobId, null, "Discovered");
        await Task.Delay(10);
        await logger.LogTargetStateChangeAsync(jobId, "TargetA", null, "Pending");
        await Task.Delay(10);
        await logger.LogJobStateChangeAsync(jobId, "Discovered", "Queued");
        await Task.Delay(10);
        await logger.LogTargetStateChangeAsync(jobId, "TargetA", "Pending", "Copying");

        // Assert
        var history = await logger.GetJobHistoryAsync(jobId);
        Assert.Equal(4, history.Count);

        // Verify chronological order
        for (int i = 1; i < history.Count; i++)
        {
            Assert.True(history[i].Timestamp >= history[i - 1].Timestamp,
                $"Entry {i} timestamp should be >= entry {i - 1} timestamp");
        }
    }

    [Fact]
    public async Task LogJobStateChangeAsync_WhenDisabled_DoesNotLog()
    {
        // Arrange
        var disabledConfig = new StateChangeLoggingConfig { Enabled = false };
        var logger = CreateLogger(disabledConfig);
        var jobId = "test-job-disabled";

        // Act
        await logger.LogJobStateChangeAsync(jobId, null, "Discovered");

        // Assert
        var history = await logger.GetJobHistoryAsync(jobId);
        Assert.Empty(history);
    }

    [Fact]
    public async Task LogTargetStateChangeAsync_WhenDisabled_DoesNotLog()
    {
        // Arrange
        var disabledConfig = new StateChangeLoggingConfig { Enabled = false };
        var logger = CreateLogger(disabledConfig);
        var jobId = "test-job-disabled-target";

        // Act
        await logger.LogTargetStateChangeAsync(jobId, "TargetA", null, "Pending");

        // Assert
        var history = await logger.GetJobHistoryAsync(jobId);
        Assert.Empty(history);
    }

    [Fact]
    public async Task LogJobStateChangeAsync_WithoutAdditionalContext_DoesNotStoreContext()
    {
        // Arrange
        var noContextConfig = new StateChangeLoggingConfig { IncludeAdditionalContext = false };
        var logger = CreateLogger(noContextConfig);
        var jobId = "test-job-no-context";

        // Act
        await logger.LogJobStateChangeAsync(jobId, null, "Discovered", "{\"test\": \"data\"}");

        // Assert
        var history = await logger.GetJobHistoryAsync(jobId);
        Assert.Single(history);
        Assert.Null(history[0].AdditionalContext);
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_WhenAutoCleanupDisabled_ReturnsZero()
    {
        // Arrange
        var noCleanupConfig = new StateChangeLoggingConfig { AutoCleanupEnabled = false };
        var logger = CreateLogger(noCleanupConfig);

        // Act
        var deletedCount = await logger.CleanupOldEntriesAsync();

        // Assert
        Assert.Equal(0, deletedCount);
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_WhenBelowMaxRecords_ReturnsZero()
    {
        // Arrange
        var logger = CreateLogger();
        await logger.LogJobStateChangeAsync("job-1", null, "Discovered");
        await logger.LogJobStateChangeAsync("job-2", null, "Discovered");

        // Act
        var deletedCount = await logger.CleanupOldEntriesAsync();

        // Assert
        Assert.Equal(0, deletedCount);
    }

    [Fact]
    public async Task CleanupOldEntriesAsync_DeletesOldRecords()
    {
        // Arrange
        var logger = CreateLogger();
        var jobId = "test-job-cleanup";

        // Insert an old record manually (91 days ago)
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StateChangeLog (JobId, EntityType, EntityId, OldState, NewState, Timestamp, DurationMs, AdditionalContext)
            VALUES (@jobId, 'Job', NULL, NULL, 'Discovered', datetime('now', '-91 days'), NULL, NULL)
            """;
        command.Parameters.AddWithValue("@jobId", jobId);
        await command.ExecuteNonQueryAsync();

        // Insert a recent record
        await logger.LogJobStateChangeAsync(jobId, "Discovered", "Queued");

        // Act
        var deletedCount = await logger.CleanupOldEntriesAsync();

        // Assert
        Assert.Equal(1, deletedCount);
        var history = await logger.GetJobHistoryAsync(jobId);
        Assert.Single(history); // Only the recent record should remain
        Assert.Equal("Queued", history[0].NewState);
    }

    [Fact]
    public async Task GetJobHistoryAsync_WithNonExistentJob_ReturnsEmpty()
    {
        // Arrange
        var logger = CreateLogger();

        // Act
        var history = await logger.GetJobHistoryAsync("non-existent-job");

        // Assert
        Assert.Empty(history);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task LogJobStateChangeAsync_WithInvalidJobId_ThrowsArgumentException(string? invalidJobId)
    {
        // Arrange
        var logger = CreateLogger();

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await logger.LogJobStateChangeAsync(invalidJobId!, null, "Discovered"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task LogJobStateChangeAsync_WithInvalidNewState_ThrowsArgumentException(string? invalidState)
    {
        // Arrange
        var logger = CreateLogger();

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await logger.LogJobStateChangeAsync("job-123", null, invalidState!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task LogTargetStateChangeAsync_WithInvalidTargetId_ThrowsArgumentException(string? invalidTargetId)
    {
        // Arrange
        var logger = CreateLogger();

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await logger.LogTargetStateChangeAsync("job-123", invalidTargetId!, null, "Pending"));
    }

    [Fact]
    public async Task LogJobStateChangeAsync_CapturesTimestampWithMillisecondPrecision()
    {
        // Arrange
        var logger = CreateLogger();
        var jobId = "test-job-timestamp";

        // Act
        var beforeLog = DateTime.UtcNow;
        await logger.LogJobStateChangeAsync(jobId, null, "Discovered");
        var afterLog = DateTime.UtcNow;

        // Assert
        var history = await logger.GetJobHistoryAsync(jobId);
        Assert.Single(history);
        Assert.True(history[0].Timestamp >= beforeLog.AddMilliseconds(-100));
        Assert.True(history[0].Timestamp <= afterLog.AddMilliseconds(100));
    }

    private StateChangeLogger CreateLogger(StateChangeLoggingConfig? config = null)
    {
        var options = Options.Create(config ?? _config);
        return new StateChangeLogger(_connectionFactory, options, _logger);
    }

    /// <summary>
    /// Clears all data from the StateChangeLog table for test isolation.
    /// </summary>
    private async Task ClearStateChangeLogAsync()
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM StateChangeLog";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Test connection factory that returns a shared in-memory test connection.
    /// Creates a new non-closing wrapper for each request to prevent the underlying
    /// connection from being disposed while tests are running.
    /// </summary>
    private sealed class TestConnectionFactory : ISqliteConnectionFactory
    {
        private readonly SqliteConnection _connection;

        public TestConnectionFactory(SqliteConnection connection)
        {
            _connection = connection;
        }

        public SqliteConnection CreateConnection()
        {
            return new SharedConnectionWrapper(_connection);
        }

        public Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<SqliteConnection>(new SharedConnectionWrapper(_connection));
        }

        public Task InitializeDatabaseAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Wrapper that prevents the underlying shared connection from being disposed.
    /// Delegates all operations to the inner connection but ignores Dispose calls.
    /// </summary>
    private sealed class SharedConnectionWrapper : SqliteConnection
    {
        private readonly SqliteConnection _inner;

        public SharedConnectionWrapper(SqliteConnection inner) : base(inner.ConnectionString)
        {
            _inner = inner;
        }

        protected override void Dispose(bool disposing)
        {
            // Don't dispose the shared connection - just clean up this wrapper
        }

        public override ValueTask DisposeAsync()
        {
            // Don't dispose the shared connection
            return ValueTask.CompletedTask;
        }

        public override SqliteCommand CreateCommand() => _inner.CreateCommand();
        public override string ConnectionString { get => _inner.ConnectionString; set { } } // Ignore setter
        public override string Database => _inner.Database;
        public override System.Data.ConnectionState State => _inner.State;
        public override string DataSource => _inner.DataSource;
        public override string ServerVersion => _inner.ServerVersion;
        public override void Open() { } // Already open
        public override Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask; // Already open
        public override void Close() { } // Don't close shared connection
        protected override SqliteCommand CreateDbCommand() => _inner.CreateCommand();
    }

    /// <summary>
    /// Test logger that writes to xUnit test output.
    /// </summary>
    private sealed class TestLogger<T> : ILogger<T>
    {
        private readonly ITestOutputHelper _output;

        public TestLogger(ITestOutputHelper output)
        {
            _output = output;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
            if (exception != null)
            {
                _output.WriteLine(exception.ToString());
            }
        }
    }
}
