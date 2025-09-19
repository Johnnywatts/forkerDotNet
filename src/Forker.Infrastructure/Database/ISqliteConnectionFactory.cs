using Microsoft.Data.Sqlite;

namespace Forker.Infrastructure.Database;

/// <summary>
/// Factory interface for creating SQLite database connections.
/// Ensures consistent connection configuration across the application.
/// </summary>
public interface ISqliteConnectionFactory
{
    /// <summary>
    /// Creates a new SQLite connection.
    /// Connection is closed and must be opened by the caller.
    /// </summary>
    /// <returns>A new SqliteConnection instance</returns>
    SqliteConnection CreateConnection();

    /// <summary>
    /// Creates and opens a new SQLite connection.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An opened SqliteConnection instance</returns>
    Task<SqliteConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes the database schema and applies any pending migrations.
    /// Should be called during application startup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InitializeDatabaseAsync(CancellationToken cancellationToken = default);
}