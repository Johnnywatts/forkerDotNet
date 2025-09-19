using Forker.Domain;
using Forker.Domain.Exceptions;
using Forker.Domain.Repositories;
using Forker.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace Forker.Infrastructure.Repositories;

/// <summary>
/// SQLite implementation of IJobRepository with optimistic concurrency control.
/// Provides crash-safe persistence for FileJob aggregates.
/// Note: This is a simplified implementation for Phase 3 that stores and retrieves
/// basic job data. Complex state reconstruction is deferred to later phases.
/// </summary>
public sealed class SqliteJobRepository : IJobRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteJobRepository> _logger;

    public SqliteJobRepository(ISqliteConnectionFactory connectionFactory, ILogger<SqliteJobRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SaveAsync(FileJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO FileJobs (Id, SourcePath, InitialSize, SourceHash, State, RequiredTargets, CreatedAt, VersionToken)
            VALUES (@Id, @SourcePath, @InitialSize, @SourceHash, @State, @RequiredTargets, @CreatedAt, @VersionToken)
            """;

        AddJobParameters(command, job);

        try
        {
            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (rowsAffected == 0)
            {
                throw new InvalidOperationException($"Failed to insert FileJob {job.Id}");
            }

            _logger.LogDebug("Successfully saved FileJob {JobId} with state {State}", job.Id, job.State);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            if (ex.Message.Contains("UNIQUE constraint failed"))
            {
                throw new InvalidOperationException($"FileJob {job.Id} already exists", ex);
            }
            throw; // Re-throw other constraint errors
        }
    }

    public async Task UpdateAsync(FileJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE FileJobs
            SET SourcePath = @SourcePath,
                InitialSize = @InitialSize,
                SourceHash = @SourceHash,
                State = @State,
                RequiredTargets = @RequiredTargets,
                VersionToken = @NewVersionToken
            WHERE Id = @Id AND VersionToken = @ExpectedVersionToken
            """;

        AddJobParameters(command, job);
        var currentVersionInDatabase = await GetVersionTokenAsync(connection, job.Id, cancellationToken);
        if (currentVersionInDatabase == null)
        {
            throw new InvalidOperationException($"FileJob {job.Id} does not exist");
        }

        command.Parameters.AddWithValue("@ExpectedVersionToken", currentVersionInDatabase.Value); // Current version in database
        command.Parameters.AddWithValue("@NewVersionToken", job.VersionToken.Value);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rowsAffected == 0)
        {
            // Check if job exists to distinguish between missing entity and concurrency conflict
            var currentVersion = await GetVersionTokenAsync(connection, job.Id, cancellationToken);
            if (currentVersion == null)
            {
                throw new InvalidOperationException($"FileJob {job.Id} does not exist");
            }

            throw new ConcurrencyException(job.Id.ToString(), job.VersionToken.Value - 1, currentVersion.Value);
        }

        _logger.LogDebug("Successfully updated FileJob {JobId} to version {Version}", job.Id, job.VersionToken);
    }

    public async Task<FileJob?> GetByIdAsync(FileJobId jobId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, SourcePath, InitialSize, SourceHash, State, RequiredTargets, CreatedAt, VersionToken
            FROM FileJobs
            WHERE Id = @Id
            """;

        command.Parameters.AddWithValue("@Id", jobId.ToString());

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return CreateJobFromReader(reader);
    }

    public async Task<IReadOnlyList<FileJob>> GetByStateAsync(JobState state, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, SourcePath, InitialSize, SourceHash, State, RequiredTargets, CreatedAt, VersionToken
            FROM FileJobs
            WHERE State = @State
            ORDER BY CreatedAt
            """;

        command.Parameters.AddWithValue("@State", state.ToString());

        var jobs = new List<FileJob>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(CreateJobFromReader(reader));
        }

        return jobs.AsReadOnly();
    }

    public async Task<IReadOnlyList<FileJob>> GetBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, SourcePath, InitialSize, SourceHash, State, RequiredTargets, CreatedAt, VersionToken
            FROM FileJobs
            WHERE SourcePath = @SourcePath
            ORDER BY CreatedAt DESC
            """;

        command.Parameters.AddWithValue("@SourcePath", sourcePath);

        var jobs = new List<FileJob>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(CreateJobFromReader(reader));
        }

        return jobs.AsReadOnly();
    }

    public async Task<bool> DeleteAsync(FileJobId jobId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobId);

        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM FileJobs WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", jobId.ToString());

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        var deleted = rowsAffected > 0;

        if (deleted)
        {
            _logger.LogDebug("Successfully deleted FileJob {JobId}", jobId);
        }

        return deleted;
    }

    public async Task<IReadOnlyDictionary<JobState, int>> GetJobCountsByStateAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT State, COUNT(*) as Count
            FROM FileJobs
            GROUP BY State
            """;

        var counts = new Dictionary<JobState, int>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var stateString = reader.GetString(0);
            var count = reader.GetInt32(1);

            if (Enum.TryParse<JobState>(stateString, out var state))
            {
                counts[state] = count;
            }
        }

        return counts.AsReadOnly();
    }

    private static void AddJobParameters(SqliteCommand command, FileJob job)
    {
        command.Parameters.AddWithValue("@Id", job.Id.ToString());
        command.Parameters.AddWithValue("@SourcePath", job.SourcePath);
        command.Parameters.AddWithValue("@InitialSize", job.InitialSize);
        command.Parameters.AddWithValue("@SourceHash", (object?)job.SourceHash ?? DBNull.Value);
        command.Parameters.AddWithValue("@State", job.State.ToString());
        command.Parameters.AddWithValue("@RequiredTargets", JsonSerializer.Serialize(job.RequiredTargets.Select(t => t.Value)));
        command.Parameters.AddWithValue("@CreatedAt", job.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@VersionToken", job.VersionToken.Value);
    }

    private static FileJob CreateJobFromReader(SqliteDataReader reader)
    {
        var id = FileJobId.From(Guid.Parse(reader.GetString(0))); // Id
        var sourcePath = reader.GetString(1); // SourcePath
        var initialSize = reader.GetInt64(2); // InitialSize
        var sourceHash = reader.IsDBNull(3) ? null : reader.GetString(3); // SourceHash
        var stateString = reader.GetString(4); // State
        var requiredTargetsJson = reader.GetString(5); // RequiredTargets
        var createdAtString = reader.GetString(6); // CreatedAt
        var versionTokenValue = reader.GetInt64(7); // VersionToken

        var targetStrings = JsonSerializer.Deserialize<string[]>(requiredTargetsJson) ?? [];
        var requiredTargets = targetStrings.Select(TargetId.From).ToList();

        var state = Enum.Parse<JobState>(stateString);
        var createdAt = DateTime.Parse(createdAtString, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var versionToken = VersionToken.From(versionTokenValue);

        // Use internal constructor to properly reconstruct the FileJob with all its state
        return new FileJob(id, sourcePath, initialSize, requiredTargets, sourceHash, state, createdAt, versionToken);
    }

    private static async Task<long?> GetVersionTokenAsync(SqliteConnection connection, FileJobId jobId, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT VersionToken FROM FileJobs WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", jobId.ToString());

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as long?;
    }
}