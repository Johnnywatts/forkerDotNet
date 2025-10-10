namespace Forker.Domain.Services;

/// <summary>
/// Represents a single state change log entry from the audit trail.
/// Immutable record type for query results.
/// </summary>
public sealed record StateChangeLogEntry
{
    /// <summary>
    /// Unique identifier for this log entry (auto-increment in database).
    /// </summary>
    public required long Id { get; init; }

    /// <summary>
    /// The FileJob identifier this state change belongs to.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Type of entity: "Job" or "Target".
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Target identifier if EntityType is "Target", null if EntityType is "Job".
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    /// Previous state (null for initial state).
    /// </summary>
    public string? OldState { get; init; }

    /// <summary>
    /// New state after transition.
    /// </summary>
    public required string NewState { get; init; }

    /// <summary>
    /// Timestamp of the state change (ISO 8601 format with millisecond precision).
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Duration in milliseconds since last state change (null for initial state).
    /// </summary>
    public int? DurationMs { get; init; }

    /// <summary>
    /// Optional JSON object with additional context (e.g., file size, hash, error details).
    /// </summary>
    public string? AdditionalContext { get; init; }
}
