namespace Forker.Domain.Repositories;

/// <summary>
/// Repository interface for FileJob aggregate persistence.
/// Supports optimistic concurrency control and crash-safe operations.
/// </summary>
public interface IJobRepository
{
    /// <summary>
    /// Saves a new FileJob to the repository.
    /// </summary>
    /// <param name="job">The FileJob to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ArgumentNullException">When job is null</exception>
    /// <exception cref="InvalidOperationException">When job already exists</exception>
    Task SaveAsync(FileJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing FileJob with optimistic concurrency control.
    /// </summary>
    /// <param name="job">The FileJob to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ArgumentNullException">When job is null</exception>
    /// <exception cref="InvalidOperationException">When job doesn't exist</exception>
    /// <exception cref="ConcurrencyException">When version token doesn't match</exception>
    Task UpdateAsync(FileJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a FileJob by its identifier.
    /// </summary>
    /// <param name="jobId">The job identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The FileJob if found, null otherwise</returns>
    Task<FileJob?> GetByIdAsync(FileJobId jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all FileJobs in the specified state.
    /// </summary>
    /// <param name="state">The job state to filter by</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of FileJobs in the specified state</returns>
    Task<IReadOnlyList<FileJob>> GetByStateAsync(JobState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves FileJobs by source path (for duplicate detection).
    /// </summary>
    /// <param name="sourcePath">The source file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of FileJobs with the specified source path</returns>
    Task<IReadOnlyList<FileJob>> GetBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a FileJob and all associated target outcomes.
    /// </summary>
    /// <param name="jobId">The job identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the job was deleted, false if it didn't exist</returns>
    Task<bool> DeleteAsync(FileJobId jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of jobs in each state for monitoring.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping JobState to count</returns>
    Task<IReadOnlyDictionary<JobState, int>> GetJobCountsByStateAsync(CancellationToken cancellationToken = default);
}