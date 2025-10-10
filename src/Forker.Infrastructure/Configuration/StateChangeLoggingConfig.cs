namespace Forker.Infrastructure.Configuration;

/// <summary>
/// Configuration for state change audit logging.
/// Controls whether and how state transitions are logged to the StateChangeLog table.
/// </summary>
public sealed class StateChangeLoggingConfig
{
    /// <summary>
    /// Master switch for state change logging.
    /// When false, no state changes are logged.
    /// Default: true
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Maximum number of records to keep in StateChangeLog table.
    /// When exceeded, triggers automatic cleanup of oldest records.
    /// Default: 100,000 records
    /// </summary>
    public int MaxRecords { get; init; } = 100_000;

    /// <summary>
    /// Enable automatic cleanup of old state change log records.
    /// Default: true
    /// </summary>
    public bool AutoCleanupEnabled { get; init; } = true;

    /// <summary>
    /// Number of days to retain state change log records.
    /// Records older than this are deleted during cleanup.
    /// Default: 90 days
    /// </summary>
    public int RetentionDays { get; init; } = 90;

    /// <summary>
    /// Include additional context (JSON data) in state change logs.
    /// When false, AdditionalContext column is left NULL.
    /// Default: true
    /// </summary>
    public bool IncludeAdditionalContext { get; init; } = true;

    /// <summary>
    /// Also write state changes to a structured log file (in addition to database).
    /// Useful for debugging but creates additional I/O.
    /// Default: false
    /// </summary>
    public bool LogToFile { get; init; }

    /// <summary>
    /// Path to log file when LogToFile is enabled.
    /// Supports rolling file patterns (date placeholders will be replaced).
    /// Default: "C:\\ProgramData\\ForkerDotNet\\Logs\\state-changes-.txt"
    /// </summary>
    public string LogFilePath { get; init; } = "C:\\ProgramData\\ForkerDotNet\\Logs\\state-changes-.txt";

    /// <summary>
    /// Validate the configuration settings.
    /// </summary>
    public void Validate()
    {
        if (MaxRecords <= 0)
            throw new InvalidOperationException("MaxRecords must be positive.");

        if (RetentionDays <= 0)
            throw new InvalidOperationException("RetentionDays must be positive.");

        if (LogToFile && string.IsNullOrWhiteSpace(LogFilePath))
            throw new InvalidOperationException("LogFilePath cannot be null or empty when LogToFile is enabled.");
    }
}
