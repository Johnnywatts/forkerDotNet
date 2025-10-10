namespace Forker.Domain.Services;

/// <summary>
/// Service interface for logging state changes to audit trail.
/// Kept in Domain layer as interface, implemented in Infrastructure.
/// </summary>
public interface IStateChangeLogger
{
    /// <summary>
    /// Logs a FileJob state change to the audit trail.
    /// </summary>
    /// <param name="jobId">The job identifier</param>
    /// <param name="oldState">The previous JobState (null for initial state)</param>
    /// <param name="newState">The new JobState</param>
    /// <param name="additionalContext">Optional JSON object with additional context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LogJobStateChangeAsync(
        string jobId,
        string? oldState,
        string newState,
        string? additionalContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a TargetOutcome state change to the audit trail.
    /// </summary>
    /// <param name="jobId">The job identifier</param>
    /// <param name="targetId">The target identifier (e.g., "TargetA")</param>
    /// <param name="oldState">The previous TargetCopyState (null for initial state)</param>
    /// <param name="newState">The new TargetCopyState</param>
    /// <param name="additionalContext">Optional JSON object with additional context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LogTargetStateChangeAsync(
        string jobId,
        string targetId,
        string? oldState,
        string newState,
        string? additionalContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves state change history for a specific job.
    /// </summary>
    /// <param name="jobId">The job identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of state change log entries ordered by timestamp</returns>
    Task<IReadOnlyList<StateChangeLogEntry>> GetJobHistoryAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs cleanup of old state change log entries based on retention policy.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of records deleted</returns>
    Task<int> CleanupOldEntriesAsync(CancellationToken cancellationToken = default);
}
