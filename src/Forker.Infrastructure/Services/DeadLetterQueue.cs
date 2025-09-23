using Forker.Domain;
using Forker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of dead letter queue for permanently failed medical imaging operations.
/// Provides audit trail and manual intervention capabilities for failed workflows.
/// NOTE: This is a basic implementation for Phase 7. Full persistence will be enhanced in future phases.
/// </summary>
public sealed class DeadLetterService : IDeadLetterService
{
    private readonly ILogger<DeadLetterService> _logger;

    public DeadLetterService(ILogger<DeadLetterService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DeadLetterEntry> AddToDeadLetterQueueAsync(TargetOutcome targetOutcome, string reason,
        OperationType operationType, Exception lastException, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetOutcome);

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be null, empty, or whitespace.", nameof(reason));

        ArgumentNullException.ThrowIfNull(lastException);

        _logger.LogCritical("Adding target {TargetId} of job {JobId} to dead letter queue: {Reason}. " +
                           "Operation: {OperationType}, Attempts: {AttemptCount}, Exception: {ExceptionType}",
            targetOutcome.TargetId, targetOutcome.JobId, reason, operationType,
            targetOutcome.Attempts, lastException.GetType().Name);

        var entry = new DeadLetterEntry(
            Guid.NewGuid(),
            targetOutcome.JobId,
            targetOutcome.TargetId,
            "unknown", // Would get from associated FileJob in full implementation
            targetOutcome.FinalPath ?? targetOutcome.TempPath,
            operationType,
            reason,
            FormatExceptionDetails(lastException),
            targetOutcome.Attempts,
            DateTime.UtcNow);

        // For Phase 7, we'll log the dead letter entry
        // In a full implementation, this would be persisted to a DeadLetterEntries table
        _logger.LogError("DEAD LETTER ENTRY CREATED: ID={EntryId}, JobId={JobId}, TargetId={TargetId}, " +
                        "SourcePath={SourcePath}, TargetPath={TargetPath}, Operation={OperationType}, " +
                        "Reason={Reason}, AttemptCount={AttemptCount}, Exception={ExceptionDetails}",
            entry.Id, entry.JobId, entry.TargetId, entry.SourcePath, entry.TargetPath,
            entry.OperationType, entry.Reason, entry.AttemptCount, entry.ExceptionDetails);

        await Task.CompletedTask; // Placeholder for async persistence
        return entry;
    }

    public async Task<DeadLetterEntry> AddJobToDeadLetterQueueAsync(FileJob fileJob, string reason,
        IEnumerable<TargetOutcome> failedTargets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileJob);

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be null, empty, or whitespace.", nameof(reason));

        ArgumentNullException.ThrowIfNull(failedTargets);

        var targetList = failedTargets.ToList();
        var totalAttempts = targetList.Sum(t => t.Attempts);

        _logger.LogCritical("Adding job {JobId} to dead letter queue: {Reason}. " +
                           "Failed targets: {FailedTargetCount}, Total attempts: {TotalAttempts}",
            fileJob.Id, reason, targetList.Count, totalAttempts);

        var entry = new DeadLetterEntry(
            Guid.NewGuid(),
            fileJob.Id,
            null, // This represents the entire job, not a specific target
            fileJob.SourcePath,
            null,
            OperationType.FileCopy, // Default to file copy for job-level failures
            reason,
            FormatJobFailureDetails(targetList),
            totalAttempts,
            DateTime.UtcNow);

        _logger.LogError("DEAD LETTER JOB ENTRY CREATED: ID={EntryId}, JobId={JobId}, " +
                        "SourcePath={SourcePath}, Reason={Reason}, FailedTargets={FailedTargetCount}, " +
                        "TotalAttempts={TotalAttempts}",
            entry.Id, entry.JobId, entry.SourcePath, entry.Reason, targetList.Count, entry.AttemptCount);

        await Task.CompletedTask; // Placeholder for async persistence
        return entry;
    }

    public async Task<IReadOnlyList<DeadLetterEntry>> GetDeadLetterEntriesAsync(DeadLetterFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving dead letter entries with filter: {Filter}", filter);

        // Placeholder implementation - returns empty list for now
        // In a real implementation, this would query the DeadLetterEntries table with proper filtering
        await Task.CompletedTask;
        return Array.Empty<DeadLetterEntry>().ToList().AsReadOnly();
    }

    public async Task<DeadLetterRequeueResult> RequeueFromDeadLetterAsync(Guid entryId, string reason,
        string requeuedBy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be null, empty, or whitespace.", nameof(reason));

        if (string.IsNullOrWhiteSpace(requeuedBy))
            throw new ArgumentException("RequeuedBy cannot be null, empty, or whitespace.", nameof(requeuedBy));

        _logger.LogWarning("Requeuing dead letter entry {EntryId} by {RequeuedBy}: {Reason}",
            entryId, requeuedBy, reason);

        // Placeholder implementation - logs the requeue action
        // In a real implementation, this would:
        // 1. Retrieve the dead letter entry
        // 2. Update its status to Requeued
        // 3. Reset the associated target outcome for retry
        // 4. Return appropriate result

        _logger.LogInformation("DEAD LETTER REQUEUE: EntryId={EntryId}, RequeuedBy={RequeuedBy}, Reason={Reason}",
            entryId, requeuedBy, reason);

        await Task.CompletedTask; // Placeholder for async operation
        return DeadLetterRequeueResult.Successful(entryId,
            $"Entry requeued successfully. Reason: {reason}", requeuedBy);
    }

    public async Task<DeadLetterPurgeResult> PurgeFromDeadLetterAsync(Guid entryId, string reason, string purgedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be null, empty, or whitespace.", nameof(reason));

        if (string.IsNullOrWhiteSpace(purgedBy))
            throw new ArgumentException("PurgedBy cannot be null, empty, or whitespace.", nameof(purgedBy));

        _logger.LogCritical("Purging dead letter entry {EntryId} by {PurgedBy}: {Reason}",
            entryId, purgedBy, reason);

        // Placeholder implementation - logs the purge action
        // In a real implementation, this would:
        // 1. Retrieve the dead letter entry
        // 2. Update its status to Purged
        // 3. Optionally delete associated files
        // 4. Return appropriate result

        _logger.LogCritical("DEAD LETTER PURGE: EntryId={EntryId}, PurgedBy={PurgedBy}, Reason={Reason}",
            entryId, purgedBy, reason);

        await Task.CompletedTask; // Placeholder for async operation
        return DeadLetterPurgeResult.Successful(entryId,
            $"Entry purged permanently. Reason: {reason}", purgedBy);
    }

    public async Task<DeadLetterStatistics> GetDeadLetterStatisticsAsync(DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Calculating dead letter statistics since {Since}", since);

        // Placeholder implementation - returns zero statistics
        // In a real implementation, this would aggregate from the DeadLetterEntries table
        await Task.CompletedTask;

        return new DeadLetterStatistics(
            0, // activeEntries
            0, // requeuedEntries
            0, // purgedEntries
            new Dictionary<OperationType, int>(), // entriesByOperationType
            new Dictionary<string, int>(), // failureReasonCounts
            null, // oldestActiveEntry
            null, // mostRecentEntry
            DateTime.UtcNow // calculatedAt
        );
    }

    public async Task<int> CleanupOldEntriesAsync(DateTime olderThan, bool onlyPurged = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cleaning up dead letter entries older than {OlderThan}, onlyPurged: {OnlyPurged}",
            olderThan, onlyPurged);

        // Placeholder implementation - returns 0 cleaned up
        // In a real implementation, this would delete old entries from the DeadLetterEntries table
        await Task.CompletedTask;
        return 0;
    }

    private static string FormatExceptionDetails(Exception exception)
    {
        return $"{exception.GetType().Name}: {exception.Message}\n" +
               $"StackTrace: {exception.StackTrace}";
    }

    private static string FormatJobFailureDetails(IList<TargetOutcome> failedTargets)
    {
        var details = new List<string>();

        foreach (var target in failedTargets)
        {
            details.Add($"Target {target.TargetId}: {target.CopyState}, " +
                       $"Attempts: {target.Attempts}, LastError: {target.LastError ?? "Unknown"}");
        }

        return string.Join("\n", details);
    }
}