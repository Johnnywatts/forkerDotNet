namespace Forker.Domain.Repositories;

/// <summary>
/// Repository interface for TargetOutcome entity persistence.
/// Manages target-specific copy operation outcomes.
/// </summary>
public interface ITargetOutcomeRepository
{
    /// <summary>
    /// Saves a new TargetOutcome to the repository.
    /// </summary>
    /// <param name="outcome">The TargetOutcome to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ArgumentNullException">When outcome is null</exception>
    /// <exception cref="InvalidOperationException">When outcome already exists</exception>
    Task SaveAsync(TargetOutcome outcome, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing TargetOutcome.
    /// </summary>
    /// <param name="outcome">The TargetOutcome to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ArgumentNullException">When outcome is null</exception>
    /// <exception cref="InvalidOperationException">When outcome doesn't exist</exception>
    Task UpdateAsync(TargetOutcome outcome, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a TargetOutcome by job and target identifiers.
    /// </summary>
    /// <param name="jobId">The job identifier</param>
    /// <param name="targetId">The target identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The TargetOutcome if found, null otherwise</returns>
    Task<TargetOutcome?> GetByIdAsync(FileJobId jobId, TargetId targetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a TargetOutcome by job and target identifiers (alias for verification compatibility).
    /// </summary>
    /// <param name="jobId">The job identifier</param>
    /// <param name="targetId">The target identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The TargetOutcome if found, null otherwise</returns>
    Task<TargetOutcome?> GetByJobIdAndTargetIdAsync(FileJobId jobId, TargetId targetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all TargetOutcomes for a specific job.
    /// </summary>
    /// <param name="jobId">The job identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of TargetOutcomes for the job</returns>
    Task<IReadOnlyList<TargetOutcome>> GetByJobIdAsync(FileJobId jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves TargetOutcomes by copy state across all jobs.
    /// </summary>
    /// <param name="copyState">The copy state to filter by</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of TargetOutcomes in the specified state</returns>
    Task<IReadOnlyList<TargetOutcome>> GetByCopyStateAsync(TargetCopyState copyState, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves TargetOutcomes for a specific target across all jobs.
    /// </summary>
    /// <param name="targetId">The target identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of TargetOutcomes for the target</returns>
    Task<IReadOnlyList<TargetOutcome>> GetByTargetIdAsync(TargetId targetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves failed TargetOutcomes that can be retried.
    /// </summary>
    /// <param name="maxAttempts">Maximum number of attempts before considering permanently failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of retryable TargetOutcomes</returns>
    Task<IReadOnlyList<TargetOutcome>> GetRetryableFailed(int maxAttempts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all TargetOutcomes for a specific job (used with cascade delete).
    /// </summary>
    /// <param name="jobId">The job identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of outcomes deleted</returns>
    Task<int> DeleteByJobIdAsync(FileJobId jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of outcomes in each state for monitoring.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping TargetCopyState to count</returns>
    Task<IReadOnlyDictionary<TargetCopyState, int>> GetOutcomeCountsByStateAsync(CancellationToken cancellationToken = default);
}