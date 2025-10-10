using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Reflection;

namespace Forker.Infrastructure.Database;

/// <summary>
/// SQLite connection factory with WAL mode and crash-safe configuration.
/// Ensures consistent database setup across all connections.
/// </summary>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory, IDisposable
{
    private readonly DatabaseConfiguration _config;
    private readonly ILogger<SqliteConnectionFactory> _logger;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private volatile bool _isInitialized;

    public SqliteConnectionFactory(IOptions<DatabaseConfiguration> config, ILogger<SqliteConnectionFactory> logger)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _config.Validate();
    }

    /// <summary>
    /// Creates a new SQLite connection with optimal settings.
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_config.ConnectionString)
        {
            DefaultTimeout = _config.CommandTimeoutSeconds
        };

        return connection;
    }

    /// <summary>
    /// Creates and opens a SQLite connection with WAL mode configuration.
    /// </summary>
    public async Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection();

        try
        {
            await connection.OpenAsync(cancellationToken);
            await ConfigureConnectionAsync(connection, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Initializes the database with schema and optimal settings.
    /// Thread-safe and idempotent.
    /// </summary>
    public async Task InitializeDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        await _initSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
                return;

            _logger.LogInformation("Initializing SQLite database...");

            using var connection = await CreateOpenConnectionAsync(cancellationToken);
            await ApplySchemaAsync(connection, cancellationToken);

            _isInitialized = true;
            _logger.LogInformation("SQLite database initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SQLite database");
            throw;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    /// <summary>
    /// Configures connection-specific SQLite settings.
    /// </summary>
    private async Task ConfigureConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (_config.EnableWalMode)
        {
            await ExecutePragmaAsync(connection, "journal_mode", "WAL", cancellationToken);
            await ExecutePragmaAsync(connection, "synchronous", "NORMAL", cancellationToken);
        }

        if (_config.EnableForeignKeys)
        {
            await ExecutePragmaAsync(connection, "foreign_keys", "ON", cancellationToken);
        }

        await ExecutePragmaAsync(connection, "cache_size", _config.CacheSize.ToString(CultureInfo.InvariantCulture), cancellationToken);
    }

    /// <summary>
    /// Applies the database schema from embedded SQL scripts.
    /// Runs migrations in order based on current schema version.
    /// </summary>
    private async Task ApplySchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Get current schema version
        int currentVersion = await GetCurrentSchemaVersionAsync(connection, cancellationToken);
        _logger.LogInformation("Current database schema version: {Version}", currentVersion);

        // Define migration scripts in order
        var migrations = new[]
        {
            ("Forker.Infrastructure.Database.Scripts.001_CreateTables.sql", 1),
            ("Forker.Infrastructure.Database.Scripts.002_AddStateChangeLog.sql", 2)
        };

        // Apply migrations that haven't been run yet
        foreach (var (resourceName, version) in migrations)
        {
            if (version <= currentVersion)
            {
                _logger.LogDebug("Skipping migration {Version} - already applied", version);
                continue;
            }

            _logger.LogInformation("Applying migration {Version}: {ResourceName}", version, resourceName);

            string schemaScript;
            try
            {
                schemaScript = await ReadEmbeddedResourceAsync(assembly, resourceName);
            }
            catch (Exception ex)
            {
                if (version == 1)
                {
                    _logger.LogWarning(ex, "Failed to read embedded schema script {ResourceName}, using fallback", resourceName);
                    schemaScript = GetFallbackSchemaScript();
                }
                else
                {
                    _logger.LogError(ex, "Failed to read migration script {ResourceName}", resourceName);
                    throw;
                }
            }

            if (string.IsNullOrEmpty(schemaScript))
            {
                throw new InvalidOperationException($"Schema script not found or empty: {resourceName}");
            }

            using var command = connection.CreateCommand();
            command.CommandText = schemaScript;
            command.CommandTimeout = _config.CommandTimeoutSeconds;

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migration {Version} executed successfully. Rows affected: {RowsAffected}", version, rowsAffected);
        }
    }

    /// <summary>
    /// Gets the current schema version from the database.
    /// Returns 0 if DatabaseMetadata table doesn't exist yet.
    /// </summary>
    private static async Task<int> GetCurrentSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM DatabaseMetadata WHERE Key = 'SchemaVersion'";
            var result = await command.ExecuteScalarAsync(cancellationToken);

            if (result == null || result == DBNull.Value)
                return 0;

            return int.TryParse(result.ToString(), out var version) ? version : 0;
        }
        catch (SqliteException)
        {
            // Table doesn't exist yet
            return 0;
        }
    }

    /// <summary>
    /// Executes a SQLite PRAGMA command.
    /// </summary>
    private static async Task ExecutePragmaAsync(SqliteConnection connection, string pragma, string value, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma} = {value}";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Reads an embedded resource as a string.
    /// </summary>
    private static async Task<string> ReadEmbeddedResourceAsync(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded resource '{resourceName}' not found");
        }

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// Fallback schema script for when embedded resource is not available.
    /// </summary>
    private static string GetFallbackSchemaScript()
    {
        return """
            -- ForkerDotNet Database Schema v1 (Fallback)
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA cache_size = 10000;
            PRAGMA foreign_keys = ON;

            -- FileJobs table
            CREATE TABLE IF NOT EXISTS FileJobs (
                Id TEXT PRIMARY KEY,
                SourcePath TEXT NOT NULL,
                InitialSize INTEGER NOT NULL CHECK (InitialSize >= 0),
                SourceHash TEXT,
                State TEXT NOT NULL,
                RequiredTargets TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                VersionToken INTEGER NOT NULL DEFAULT 1 CHECK (VersionToken > 0),
                CONSTRAINT chk_state CHECK (State IN ('Discovered', 'Queued', 'InProgress', 'Partial', 'Verified', 'Failed', 'Quarantined'))
            );

            -- TargetOutcomes table
            CREATE TABLE IF NOT EXISTS TargetOutcomes (
                JobId TEXT NOT NULL,
                TargetId TEXT NOT NULL,
                CopyState TEXT NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0 CHECK (Attempts >= 0),
                Hash TEXT,
                TempPath TEXT,
                FinalPath TEXT,
                LastError TEXT,
                LastTransitionAt TEXT NOT NULL,
                PRIMARY KEY (JobId, TargetId),
                FOREIGN KEY (JobId) REFERENCES FileJobs(Id) ON DELETE CASCADE,
                CONSTRAINT chk_copy_state CHECK (CopyState IN ('Pending', 'Copying', 'Copied', 'Verifying', 'Verified', 'FailedRetryable', 'FailedPermanent'))
            );

            -- Indexes
            CREATE INDEX IF NOT EXISTS ix_filejobs_state ON FileJobs(State);
            CREATE INDEX IF NOT EXISTS ix_filejobs_created_at ON FileJobs(CreatedAt);
            CREATE INDEX IF NOT EXISTS ix_filejobs_source_path ON FileJobs(SourcePath);
            CREATE INDEX IF NOT EXISTS ix_targetoutcomes_copy_state ON TargetOutcomes(CopyState);
            CREATE INDEX IF NOT EXISTS ix_targetoutcomes_last_transition ON TargetOutcomes(LastTransitionAt);

            -- Database metadata
            CREATE TABLE IF NOT EXISTS DatabaseMetadata (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            INSERT OR IGNORE INTO DatabaseMetadata (Key, Value, UpdatedAt)
            VALUES ('SchemaVersion', '1', datetime('now'));
            """;
    }

    public void Dispose()
    {
        _initSemaphore.Dispose();
    }
}