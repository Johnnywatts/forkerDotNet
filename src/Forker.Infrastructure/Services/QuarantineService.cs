using Forker.Domain;
using Forker.Domain.Services;
using Forker.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of quarantine service for managing corrupted or mismatched files.
/// Enforces zero tolerance for hash mismatches in medical imaging workflows.
/// Provides audit trail and manual release capabilities for quarantined items.
/// </summary>
public sealed class QuarantineService : IQuarantineService
{
    private readonly IQuarantineRepository _quarantineRepository;
    private readonly IJobRepository _jobRepository;
    private readonly ITargetOutcomeRepository _targetOutcomeRepository;
    private readonly ILogger<QuarantineService> _logger;

    public QuarantineService(
        IQuarantineRepository quarantineRepository,
        IJobRepository jobRepository,
        ITargetOutcomeRepository targetOutcomeRepository,
        ILogger<QuarantineService> logger)
    {
        _quarantineRepository = quarantineRepository ?? throw new ArgumentNullException(nameof(quarantineRepository));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _targetOutcomeRepository = targetOutcomeRepository ?? throw new ArgumentNullException(nameof(targetOutcomeRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<QuarantineEntry> QuarantineJobAsync(FileJob fileJob, string reason,
        IEnumerable<TargetOutcome> affectedTargets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileJob);
        ArgumentNullException.ThrowIfNull(affectedTargets);

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be null or empty.", nameof(reason));

        var targetList = affectedTargets.ToList();
        if (targetList.Count == 0)
            throw new ArgumentException("At least one affected target must be provided.", nameof(affectedTargets));

        _logger.LogWarning("Quarantining job {JobId} due to: {Reason}. Affected targets: {TargetCount}",
            fileJob.Id, reason, targetList.Count);

        try
        {
            // Create quarantined targets
            var quarantinedTargets = targetList.Select(target => new QuarantinedTarget(
                target.TargetId,
                target.FinalPath,
                target.Hash,
                fileJob.SourceHash ?? "unknown",
                $"Target failed verification: {target.LastError ?? "Hash mismatch"}",
                DateTime.UtcNow
            )).ToList();

            // Create quarantine entry
            var quarantineEntry = new QuarantineEntry(
                Guid.NewGuid(),
                fileJob.Id,
                fileJob.SourcePath,
                fileJob.SourceHash,
                reason,
                quarantinedTargets,
                DateTime.UtcNow,
                "system", // TODO: Could be enhanced with user context
                QuarantineStatus.Active
            );

            // Persist quarantine entry
            await _quarantineRepository.AddAsync(quarantineEntry, cancellationToken);

            // Update job state to QUARANTINED (Invariant I5: Hash mismatch => QUARANTINED)
            fileJob.MarkAsQuarantined();
            await _jobRepository.UpdateAsync(fileJob, cancellationToken);

            // Update affected target outcomes to permanent failure state
            foreach (var target in targetList)
            {
                if (target.CopyState != TargetCopyState.FailedPermanent)
                {
                    target.MarkAsPermanentlyFailed($"Quarantined: {reason}");
                    await _targetOutcomeRepository.UpdateAsync(target, cancellationToken);
                }
            }

            _logger.LogError("Job {JobId} successfully quarantined with entry ID {QuarantineEntryId}. " +
                            "Manual intervention required (Invariant I16)",
                fileJob.Id, quarantineEntry.Id);

            return quarantineEntry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to quarantine job {JobId}: {Error}", fileJob.Id, ex.Message);
            throw;
        }
    }

    public async Task<QuarantineEntry> QuarantineTargetAsync(TargetOutcome targetOutcome, string reason,
        VerificationResult verificationResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetOutcome);
        ArgumentNullException.ThrowIfNull(verificationResult);

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be null or empty.", nameof(reason));

        _logger.LogWarning("Quarantining target {TargetId} of job {JobId} due to: {Reason}",
            targetOutcome.TargetId, targetOutcome.JobId, reason);

        try
        {
            // Create quarantined target
            var quarantinedTarget = new QuarantinedTarget(
                targetOutcome.TargetId,
                verificationResult.FilePath,
                verificationResult.ComputedHash,
                verificationResult.ExpectedHash,
                $"Verification failed: {reason}. Expected: {verificationResult.ExpectedHash}, " +
                $"Computed: {verificationResult.ComputedHash}",
                DateTime.UtcNow
            );

            // Retrieve file job for quarantine entry
            var fileJob = await _jobRepository.GetByIdAsync(targetOutcome.JobId, cancellationToken);
            if (fileJob == null)
            {
                throw new InvalidOperationException($"File job {targetOutcome.JobId} not found for quarantine");
            }

            // Create quarantine entry
            var quarantineEntry = new QuarantineEntry(
                Guid.NewGuid(),
                fileJob.Id,
                fileJob.SourcePath,
                fileJob.SourceHash,
                reason,
                [quarantinedTarget],
                DateTime.UtcNow,
                "system",
                QuarantineStatus.Active
            );

            // Persist quarantine entry
            await _quarantineRepository.AddAsync(quarantineEntry, cancellationToken);

            // Update target outcome to permanent failure
            targetOutcome.MarkAsPermanentlyFailed($"Quarantined: {reason}");
            await _targetOutcomeRepository.UpdateAsync(targetOutcome, cancellationToken);

            _logger.LogError("Target {TargetId} of job {JobId} successfully quarantined with entry ID {QuarantineEntryId}",
                targetOutcome.TargetId, targetOutcome.JobId, quarantineEntry.Id);

            return quarantineEntry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to quarantine target {TargetId} of job {JobId}: {Error}",
                targetOutcome.TargetId, targetOutcome.JobId, ex.Message);
            throw;
        }
    }

    public async Task<IReadOnlyList<QuarantineEntry>> GetQuarantinedJobsAsync(QuarantineFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving quarantined jobs with filter: {Filter}", filter);

        try
        {
            var entries = await _quarantineRepository.GetEntriesAsync(filter, cancellationToken);

            _logger.LogDebug("Retrieved {EntryCount} quarantine entries", entries.Count);

            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving quarantined jobs: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<bool> ReleaseFromQuarantineAsync(Guid quarantineEntryId, string releaseReason, string releasedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(releaseReason))
            throw new ArgumentException("Release reason cannot be null or empty.", nameof(releaseReason));

        if (string.IsNullOrWhiteSpace(releasedBy))
            throw new ArgumentException("Released by cannot be null or empty.", nameof(releasedBy));

        _logger.LogInformation("Attempting to release quarantine entry {QuarantineEntryId} by {ReleasedBy}: {ReleaseReason}",
            quarantineEntryId, releasedBy, releaseReason);

        try
        {
            var entry = await _quarantineRepository.GetByIdAsync(quarantineEntryId, cancellationToken);
            if (entry == null)
            {
                _logger.LogWarning("Quarantine entry {QuarantineEntryId} not found for release", quarantineEntryId);
                return false;
            }

            if (entry.Status != QuarantineStatus.Active)
            {
                _logger.LogWarning("Quarantine entry {QuarantineEntryId} is not active (status: {Status})",
                    quarantineEntryId, entry.Status);
                return false;
            }

            // Mark quarantine entry as released
            var releasedEntry = new QuarantineEntry(
                entry.Id, entry.JobId, entry.SourcePath, entry.ExpectedHash,
                entry.Reason, entry.AffectedTargets, entry.QuarantinedAt, entry.QuarantinedBy,
                QuarantineStatus.Released, DateTime.UtcNow, releaseReason, releasedBy);

            await _quarantineRepository.UpdateAsync(releasedEntry, cancellationToken);

            // Retrieve and requeue the file job from quarantined state
            var fileJob = await _jobRepository.GetByIdAsync(entry.JobId, cancellationToken);
            if (fileJob != null && fileJob.State == JobState.Quarantined)
            {
                fileJob.RequeueFromQuarantine();
                await _jobRepository.UpdateAsync(fileJob, cancellationToken);

                _logger.LogInformation("File job {JobId} requeued from quarantine by {ReleasedBy}",
                    fileJob.Id, releasedBy);
            }

            _logger.LogInformation("Quarantine entry {QuarantineEntryId} successfully released by {ReleasedBy}",
                quarantineEntryId, releasedBy);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing quarantine entry {QuarantineEntryId}: {Error}",
                quarantineEntryId, ex.Message);
            throw;
        }
    }

    public async Task<bool> PurgeQuarantinedJobAsync(Guid quarantineEntryId, string purgeReason, string purgedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(purgeReason))
            throw new ArgumentException("Purge reason cannot be null or empty.", nameof(purgeReason));

        if (string.IsNullOrWhiteSpace(purgedBy))
            throw new ArgumentException("Purged by cannot be null or empty.", nameof(purgedBy));

        _logger.LogWarning("Attempting to permanently purge quarantine entry {QuarantineEntryId} by {PurgedBy}: {PurgeReason}",
            quarantineEntryId, purgedBy, purgeReason);

        try
        {
            var entry = await _quarantineRepository.GetByIdAsync(quarantineEntryId, cancellationToken);
            if (entry == null)
            {
                _logger.LogWarning("Quarantine entry {QuarantineEntryId} not found for purge", quarantineEntryId);
                return false;
            }

            // TODO: Implement file deletion logic here
            // This would delete the corrupted files from target directories
            // For now, just mark as purged in database

            // Mark quarantine entry as purged
            var purgedEntry = new QuarantineEntry(
                entry.Id, entry.JobId, entry.SourcePath, entry.ExpectedHash,
                entry.Reason, entry.AffectedTargets, entry.QuarantinedAt, entry.QuarantinedBy,
                QuarantineStatus.Purged, DateTime.UtcNow, purgeReason, purgedBy);

            await _quarantineRepository.UpdateAsync(purgedEntry, cancellationToken);

            // Update job to failed state if it was quarantined
            var fileJob = await _jobRepository.GetByIdAsync(entry.JobId, cancellationToken);
            if (fileJob != null && fileJob.State == JobState.Quarantined)
            {
                fileJob.MarkAsFailed();
                await _jobRepository.UpdateAsync(fileJob, cancellationToken);
            }

            _logger.LogCritical("Quarantine entry {QuarantineEntryId} permanently purged by {PurgedBy}. " +
                               "Files and records marked for deletion.",
                quarantineEntryId, purgedBy);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error purging quarantine entry {QuarantineEntryId}: {Error}",
                quarantineEntryId, ex.Message);
            throw;
        }
    }

    public async Task<QuarantineStatistics> GetQuarantineStatisticsAsync(DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving quarantine statistics since {Since}", since);

        try
        {
            var statistics = await _quarantineRepository.GetStatisticsAsync(since, cancellationToken);

            _logger.LogDebug("Retrieved quarantine statistics: {ActiveCount} active, {ReleasedCount} released, {PurgedCount} purged",
                statistics.ActiveCount, statistics.ReleasedCount, statistics.PurgedCount);

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving quarantine statistics: {Error}", ex.Message);
            throw;
        }
    }
}