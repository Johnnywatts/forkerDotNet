namespace Forker.Infrastructure.Database;

/// <summary>
/// Configuration options for SQLite database connection.
/// </summary>
public sealed class DatabaseConfiguration
{
    /// <summary>
    /// SQLite connection string or file path.
    /// Default: "Data Source=forker.db"
    /// </summary>
    public string ConnectionString { get; init; } = "Data Source=forker.db";

    /// <summary>
    /// Enable WAL (Write-Ahead Logging) mode for better concurrency.
    /// Default: true
    /// </summary>
    public bool EnableWalMode { get; init; } = true;

    /// <summary>
    /// Connection timeout in seconds.
    /// Default: 30 seconds
    /// </summary>
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// SQLite cache size (number of pages).
    /// Default: 10000 pages (~40MB for 4KB pages)
    /// </summary>
    public int CacheSize { get; init; } = 10000;

    /// <summary>
    /// Enable foreign key constraints.
    /// Default: true
    /// </summary>
    public bool EnableForeignKeys { get; init; } = true;

    /// <summary>
    /// Validate the configuration settings.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException("ConnectionString cannot be null or empty.");

        if (CommandTimeoutSeconds <= 0)
            throw new InvalidOperationException("CommandTimeoutSeconds must be positive.");

        if (CacheSize <= 0)
            throw new InvalidOperationException("CacheSize must be positive.");
    }
}