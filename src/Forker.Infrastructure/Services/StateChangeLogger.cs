using System.Globalization;
using Forker.Domain.Services;
using Forker.Infrastructure.Configuration;
using Forker.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Implementation of state change logging service.
/// Writes state transitions to StateChangeLog table for audit trail.
/// </summary>
public sealed class StateChangeLogger : IStateChangeLogger
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly StateChangeLoggingConfig _config;
    private readonly ILogger<StateChangeLogger> _logger;

    public StateChangeLogger(
        ISqliteConnectionFactory connectionFactory,
        IOptions<StateChangeLoggingConfig> config,
        ILogger<StateChangeLogger> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _config.Validate();
    }

    /// <inheritdoc />
    public async Task LogJobStateChangeAsync(
        string jobId,
        string? oldState,
        string newState,
        string? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
            return;

        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newState);

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = """
                INSERT INTO StateChangeLog
                    (JobId, EntityType, EntityId, OldState, NewState, Timestamp, DurationMs, AdditionalContext)
                VALUES
                    (@jobId, 'Job', NULL, @oldState, @newState, @timestamp, @durationMs, @additionalContext)
                """;

            command.Parameters.AddWithValue("@jobId", jobId);
            command.Parameters.AddWithValue("@oldState", (object?)oldState ?? DBNull.Value);
            command.Parameters.AddWithValue("@newState", newState);
            command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("o"));

            var durationMs = await GetDurationSinceLastChangeAsync(connection, jobId, "Job", null, cancellationToken);
            command.Parameters.AddWithValue("@durationMs", (object?)durationMs ?? DBNull.Value);

            command.Parameters.AddWithValue("@additionalContext",
                _config.IncludeAdditionalContext && !string.IsNullOrWhiteSpace(additionalContext)
                    ? additionalContext
                    : DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);

            if (_config.LogToFile)
            {
                _logger.LogInformation(
                    "Job state change: JobId={JobId}, {OldState} -> {NewState}, Duration={DurationMs}ms",
                    jobId, oldState ?? "NULL", newState, durationMs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log job state change for JobId={JobId}", jobId);
            // Don't throw - state logging failures should not break the main workflow
        }
    }

    /// <inheritdoc />
    public async Task LogTargetStateChangeAsync(
        string jobId,
        string targetId,
        string? oldState,
        string newState,
        string? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
            return;

        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newState);

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = """
                INSERT INTO StateChangeLog
                    (JobId, EntityType, EntityId, OldState, NewState, Timestamp, DurationMs, AdditionalContext)
                VALUES
                    (@jobId, 'Target', @targetId, @oldState, @newState, @timestamp, @durationMs, @additionalContext)
                """;

            command.Parameters.AddWithValue("@jobId", jobId);
            command.Parameters.AddWithValue("@targetId", targetId);
            command.Parameters.AddWithValue("@oldState", (object?)oldState ?? DBNull.Value);
            command.Parameters.AddWithValue("@newState", newState);
            command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("o"));

            var durationMs = await GetDurationSinceLastChangeAsync(connection, jobId, "Target", targetId, cancellationToken);
            command.Parameters.AddWithValue("@durationMs", (object?)durationMs ?? DBNull.Value);

            command.Parameters.AddWithValue("@additionalContext",
                _config.IncludeAdditionalContext && !string.IsNullOrWhiteSpace(additionalContext)
                    ? additionalContext
                    : DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);

            if (_config.LogToFile)
            {
                _logger.LogInformation(
                    "Target state change: JobId={JobId}, TargetId={TargetId}, {OldState} -> {NewState}, Duration={DurationMs}ms",
                    jobId, targetId, oldState ?? "NULL", newState, durationMs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log target state change for JobId={JobId}, TargetId={TargetId}",
                jobId, targetId);
            // Don't throw - state logging failures should not break the main workflow
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StateChangeLogEntry>> GetJobHistoryAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT Id, JobId, EntityType, EntityId, OldState, NewState, Timestamp, DurationMs, AdditionalContext
                FROM StateChangeLog
                WHERE JobId = @jobId
                ORDER BY Timestamp ASC
                """;

            command.Parameters.AddWithValue("@jobId", jobId);

            var entries = new List<StateChangeLogEntry>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                entries.Add(new StateChangeLogEntry
                {
                    Id = reader.GetInt64(0),
                    JobId = reader.GetString(1),
                    EntityType = reader.GetString(2),
                    EntityId = reader.IsDBNull(3) ? null : reader.GetString(3),
                    OldState = reader.IsDBNull(4) ? null : reader.GetString(4),
                    NewState = reader.GetString(5),
                    Timestamp = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                    DurationMs = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    AdditionalContext = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }

            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve job history for JobId={JobId}", jobId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupOldEntriesAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.AutoCleanupEnabled)
            return 0;

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            // Delete old records beyond retention period
            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.CommandText = """
                DELETE FROM StateChangeLog
                WHERE Timestamp < datetime('now', '-' || @retentionDays || ' days')
                """;
            deleteCommand.Parameters.AddWithValue("@retentionDays", _config.RetentionDays);

            var deletedCount = await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

            if (deletedCount > 0)
            {
                _logger.LogInformation(
                    "Cleaned up {DeletedCount} old state change log entries (retention: {RetentionDays} days)",
                    deletedCount, _config.RetentionDays);
            }
            else
            {
                _logger.LogDebug("No old state change log entries to clean up (retention: {RetentionDays} days)",
                    _config.RetentionDays);
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old state change log entries");
            throw;
        }
    }

    /// <summary>
    /// Gets the duration in milliseconds since the last state change for the same entity.
    /// Returns null if this is the first state change.
    /// </summary>
    private static async Task<int?> GetDurationSinceLastChangeAsync(
        SqliteConnection connection,
        string jobId,
        string entityType,
        string? entityId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Timestamp
            FROM StateChangeLog
            WHERE JobId = @jobId
              AND EntityType = @entityType
              AND (@entityId IS NULL AND EntityId IS NULL OR EntityId = @entityId)
            ORDER BY Timestamp DESC
            LIMIT 1
            """;

        command.Parameters.AddWithValue("@jobId", jobId);
        command.Parameters.AddWithValue("@entityType", entityType);
        command.Parameters.AddWithValue("@entityId", (object?)entityId ?? DBNull.Value);

        var lastTimestamp = await command.ExecuteScalarAsync(cancellationToken);
        if (lastTimestamp == null || lastTimestamp == DBNull.Value)
            return null;

        var lastTime = DateTime.Parse(lastTimestamp.ToString()!, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        var duration = (DateTime.UtcNow - lastTime).TotalMilliseconds;
        return (int)Math.Round(duration);
    }
}
