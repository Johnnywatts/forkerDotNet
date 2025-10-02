using Forker.Domain;
using Forker.Domain.Repositories;
using Forker.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Forker.Infrastructure.Repositories;

/// <summary>
/// SQLite implementation of ITargetOutcomeRepository.
/// Provides crash-safe persistence for TargetOutcome entities.
/// </summary>
public sealed class SqliteTargetOutcomeRepository : ITargetOutcomeRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteTargetOutcomeRepository> _logger;

    public SqliteTargetOutcomeRepository(ISqliteConnectionFactory connectionFactory, ILogger<SqliteTargetOutcomeRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveAsync(TargetOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO TargetOutcomes (JobId, TargetId, CopyState, Attempts, Hash, TempPath, FinalPath, LastError, LastTransitionAt)
            VALUES (@JobId, @TargetId, @CopyState, @Attempts, @Hash, @TempPath, @FinalPath, @LastError, @LastTransitionAt)
            """;

        AddOutcomeParameters(command, outcome);

        try
        {
            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (rowsAffected == 0)
            {
                throw new InvalidOperationException($"Failed to insert TargetOutcome {outcome.JobId}:{outcome.TargetId}");
            }

            _logger.LogDebug("Successfully saved TargetOutcome {JobId}:{TargetId} with state {State}",
                outcome.JobId, outcome.TargetId, outcome.CopyState);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            if (ex.Message.Contains("UNIQUE constraint failed"))
            {
                throw new InvalidOperationException($"TargetOutcome {outcome.JobId}:{outcome.TargetId} already exists", ex);
            }
            throw; // Re-throw other constraint errors (like foreign key)
        }
    }

    public async Task UpdateAsync(TargetOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE TargetOutcomes
            SET CopyState = @CopyState,
                Attempts = @Attempts,
                Hash = @Hash,
                TempPath = @TempPath,
                FinalPath = @FinalPath,
                LastError = @LastError,
                LastTransitionAt = @LastTransitionAt
            WHERE JobId = @JobId AND TargetId = @TargetId
            """;

        AddOutcomeParameters(command, outcome);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rowsAffected == 0)
        {
            throw new InvalidOperationException($"TargetOutcome {outcome.JobId}:{outcome.TargetId} does not exist");
        }

        _logger.LogDebug("Successfully updated TargetOutcome {JobId}:{TargetId} to state {State}",
            outcome.JobId, outcome.TargetId, outcome.CopyState);
    }

    public async Task<TargetOutcome?> GetByIdAsync(FileJobId jobId, TargetId targetId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(targetId);

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT JobId, TargetId, CopyState, Attempts, Hash, TempPath, FinalPath, LastError, LastTransitionAt
            FROM TargetOutcomes
            WHERE JobId = @JobId AND TargetId = @TargetId
            """;

        command.Parameters.AddWithValue("@JobId", jobId.ToString());
        command.Parameters.AddWithValue("@TargetId", targetId.Value);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return CreateOutcomeFromReader(reader);
    }

    public async Task<TargetOutcome?> GetByJobIdAndTargetIdAsync(FileJobId jobId, TargetId targetId, CancellationToken cancellationToken = default)
    {
        // Alias for GetByIdAsync to support verification workflow
        return await GetByIdAsync(jobId, targetId, cancellationToken);
    }

    public async Task<IReadOnlyList<TargetOutcome>> GetByJobIdAsync(FileJobId jobId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT JobId, TargetId, CopyState, Attempts, Hash, TempPath, FinalPath, LastError, LastTransitionAt
            FROM TargetOutcomes
            WHERE JobId = @JobId
            ORDER BY TargetId
            """;

        command.Parameters.AddWithValue("@JobId", jobId.ToString());

        var outcomes = new List<TargetOutcome>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            outcomes.Add(CreateOutcomeFromReader(reader));
        }

        return outcomes.AsReadOnly();
    }

    public async Task<IReadOnlyList<TargetOutcome>> GetByCopyStateAsync(TargetCopyState copyState, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT JobId, TargetId, CopyState, Attempts, Hash, TempPath, FinalPath, LastError, LastTransitionAt
            FROM TargetOutcomes
            WHERE CopyState = @CopyState
            ORDER BY LastTransitionAt
            """;

        command.Parameters.AddWithValue("@CopyState", copyState.ToString());

        var outcomes = new List<TargetOutcome>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            outcomes.Add(CreateOutcomeFromReader(reader));
        }

        return outcomes.AsReadOnly();
    }

    public async Task<IReadOnlyList<TargetOutcome>> GetByTargetIdAsync(TargetId targetId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetId);

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT JobId, TargetId, CopyState, Attempts, Hash, TempPath, FinalPath, LastError, LastTransitionAt
            FROM TargetOutcomes
            WHERE TargetId = @TargetId
            ORDER BY LastTransitionAt DESC
            """;

        command.Parameters.AddWithValue("@TargetId", targetId.Value);

        var outcomes = new List<TargetOutcome>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            outcomes.Add(CreateOutcomeFromReader(reader));
        }

        return outcomes.AsReadOnly();
    }

    public async Task<IReadOnlyList<TargetOutcome>> GetRetryableFailed(int maxAttempts, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT JobId, TargetId, CopyState, Attempts, Hash, TempPath, FinalPath, LastError, LastTransitionAt
            FROM TargetOutcomes
            WHERE CopyState = 'FailedRetryable' AND Attempts < @MaxAttempts
            ORDER BY LastTransitionAt
            """;

        command.Parameters.AddWithValue("@MaxAttempts", maxAttempts);

        var outcomes = new List<TargetOutcome>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            outcomes.Add(CreateOutcomeFromReader(reader));
        }

        return outcomes.AsReadOnly();
    }

    public async Task<int> DeleteByJobIdAsync(FileJobId jobId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM TargetOutcomes WHERE JobId = @JobId";
        command.Parameters.AddWithValue("@JobId", jobId.ToString());

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

        if (rowsAffected > 0)
        {
            _logger.LogDebug("Successfully deleted {Count} TargetOutcomes for JobId {JobId}", rowsAffected, jobId);
        }

        return rowsAffected;
    }

    public async Task<IReadOnlyDictionary<TargetCopyState, int>> GetOutcomeCountsByStateAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT CopyState, COUNT(*) as Count
            FROM TargetOutcomes
            GROUP BY CopyState
            """;

        var counts = new Dictionary<TargetCopyState, int>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var stateString = reader.GetString(0);
            var count = reader.GetInt32(1);

            if (Enum.TryParse<TargetCopyState>(stateString, out var state))
            {
                counts[state] = count;
            }
        }

        return counts.AsReadOnly();
    }

    private static void AddOutcomeParameters(SqliteCommand command, TargetOutcome outcome)
    {
        command.Parameters.AddWithValue("@JobId", outcome.JobId.ToString());
        command.Parameters.AddWithValue("@TargetId", outcome.TargetId.Value);
        command.Parameters.AddWithValue("@CopyState", outcome.CopyState.ToString());
        command.Parameters.AddWithValue("@Attempts", outcome.Attempts);
        command.Parameters.AddWithValue("@Hash", (object?)outcome.Hash ?? DBNull.Value);
        command.Parameters.AddWithValue("@TempPath", (object?)outcome.TempPath ?? DBNull.Value);
        command.Parameters.AddWithValue("@FinalPath", (object?)outcome.FinalPath ?? DBNull.Value);
        command.Parameters.AddWithValue("@LastError", (object?)outcome.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("@LastTransitionAt", outcome.LastTransitionAt.ToString("O", CultureInfo.InvariantCulture));
    }

    private static TargetOutcome CreateOutcomeFromReader(SqliteDataReader reader)
    {
        var jobId = FileJobId.From(Guid.Parse(reader.GetString(0))); // JobId
        var targetId = TargetId.From(reader.GetString(1)); // TargetId
        var copyState = Enum.Parse<TargetCopyState>(reader.GetString(2)); // CopyState
        var attempts = reader.GetInt32(3); // Attempts
        var hash = reader.IsDBNull(4) ? null : reader.GetString(4); // Hash
        var tempPath = reader.IsDBNull(5) ? null : reader.GetString(5); // TempPath
        var finalPath = reader.IsDBNull(6) ? null : reader.GetString(6); // FinalPath
        var lastError = reader.IsDBNull(7) ? null : reader.GetString(7); // LastError
        var lastTransitionAt = DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); // LastTransitionAt

        // Use reflection to create TargetOutcome and set its internal state
        // This bypasses the state machine validation since we're reconstructing from persisted state
        var outcome = new TargetOutcome(jobId, targetId);

        // Use reflection to set private fields
        var outcomeType = typeof(TargetOutcome);
        outcomeType.GetField("_copyState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(outcome, copyState);
        outcomeType.GetField("_attempts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(outcome, attempts);
        outcomeType.GetField("_hash", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(outcome, hash);

        outcomeType.GetProperty("TempPath")?.SetValue(outcome, tempPath);
        outcomeType.GetProperty("FinalPath")?.SetValue(outcome, finalPath);
        outcomeType.GetProperty("LastError")?.SetValue(outcome, lastError);
        outcomeType.GetProperty("LastTransitionAt")?.SetValue(outcome, lastTransitionAt);

        return outcome;
    }
}