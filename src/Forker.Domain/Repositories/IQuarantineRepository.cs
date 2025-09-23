using Forker.Domain.Services;

namespace Forker.Domain.Repositories;

/// <summary>
/// Repository for managing quarantine entries and related operations.
/// Provides persistence for files that have failed verification or have integrity issues.
/// </summary>
public interface IQuarantineRepository
{
    /// <summary>
    /// Adds a new quarantine entry to the repository.
    /// </summary>
    /// <param name="quarantineEntry">The quarantine entry to add</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Task representing the async operation</returns>
    Task AddAsync(QuarantineEntry quarantineEntry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing quarantine entry (e.g., when released or purged).
    /// </summary>
    /// <param name="quarantineEntry">The quarantine entry to update</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Task representing the async operation</returns>
    Task UpdateAsync(QuarantineEntry quarantineEntry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a quarantine entry by its unique identifier.
    /// </summary>
    /// <param name="id">The quarantine entry ID</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>The quarantine entry if found, null otherwise</returns>
    Task<QuarantineEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves quarantine entries based on filter criteria.
    /// </summary>
    /// <param name="filter">Optional filter criteria</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Collection of quarantine entries matching the filter</returns>
    Task<IReadOnlyList<QuarantineEntry>> GetEntriesAsync(QuarantineFilter? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves quarantine entries for a specific file job.
    /// </summary>
    /// <param name="jobId">The file job ID</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Collection of quarantine entries for the specified job</returns>
    Task<IReadOnlyList<QuarantineEntry>> GetByJobIdAsync(FileJobId jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregate statistics about quarantine entries.
    /// </summary>
    /// <param name="since">Optional date to get statistics since</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Quarantine statistics</returns>
    Task<QuarantineStatistics> GetStatisticsAsync(DateTime? since = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes old quarantine entries that have been purged.
    /// Used for periodic cleanup of the quarantine audit trail.
    /// </summary>
    /// <param name="purgedBefore">Delete entries purged before this date</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Number of entries deleted</returns>
    Task<int> DeletePurgedEntriesAsync(DateTime purgedBefore, CancellationToken cancellationToken = default);
}