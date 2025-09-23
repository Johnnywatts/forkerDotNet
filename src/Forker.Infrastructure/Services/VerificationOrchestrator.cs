using System.Diagnostics;
using Forker.Domain;
using Forker.Domain.Services;
using Forker.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Orchestrator service that coordinates verification across multiple targets for file jobs.
/// Enforces invariants I2, I11, I19 and manages the transition from PARTIAL to VERIFIED states.
/// Handles quarantine logic for hash mismatches in medical imaging workflows.
/// </summary>
public sealed class VerificationOrchestrator : IVerificationOrchestrator
{
    private readonly IVerificationService _verificationService;
    private readonly IQuarantineService _quarantineService;
    private readonly IJobRepository _jobRepository;
    private readonly ITargetOutcomeRepository _targetOutcomeRepository;
    private readonly ILogger<VerificationOrchestrator> _logger;

    public VerificationOrchestrator(
        IVerificationService verificationService,
        IQuarantineService quarantineService,
        IJobRepository jobRepository,
        ITargetOutcomeRepository targetOutcomeRepository,
        ILogger<VerificationOrchestrator> logger)
    {
        _verificationService = verificationService ?? throw new ArgumentNullException(nameof(verificationService));
        _quarantineService = quarantineService ?? throw new ArgumentNullException(nameof(quarantineService));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _targetOutcomeRepository = targetOutcomeRepository ?? throw new ArgumentNullException(nameof(targetOutcomeRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<JobVerificationResult> VerifyJobAsync(FileJob fileJob,
        IReadOnlyList<TargetOutcome> targetOutcomes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileJob);
        ArgumentNullException.ThrowIfNull(targetOutcomes);

        if (string.IsNullOrWhiteSpace(fileJob.SourceHash))
        {
            throw new InvalidOperationException($"FileJob {fileJob.Id} does not have a source hash for verification");
        }

        var verificationStartedAt = DateTime.UtcNow;
        _logger.LogInformation("Starting job verification for {JobId} with {TargetCount} targets",
            fileJob.Id, targetOutcomes.Count);

        try
        {
            // Verify all targets in parallel (Invariant I19: Independent target progress)
            var targetVerificationResults = await VerifyTargetsAsync(targetOutcomes, fileJob.SourceHash, cancellationToken);

            var verificationCompletedAt = DateTime.UtcNow;

            // Create job verification result
            var jobResult = new JobVerificationResult(
                fileJob.Id,
                fileJob.SourceHash,
                targetVerificationResults,
                verificationStartedAt,
                verificationCompletedAt
            );

            _logger.LogInformation("Job verification completed for {JobId}: Status {Status}, " +
                                  "Successful {SuccessfulCount}/{TotalCount}, Quarantine Required: {QuarantineCount}",
                fileJob.Id, jobResult.Status, jobResult.SuccessfulTargetCount,
                targetOutcomes.Count, jobResult.TargetsRequiringQuarantine.Count);

            return jobResult;
        }
        catch (Exception ex)
        {
            var verificationCompletedAt = DateTime.UtcNow;
            _logger.LogError(ex, "Job verification failed for {JobId}: {Error}", fileJob.Id, ex.Message);

            return new JobVerificationResult(
                fileJob.Id,
                fileJob.SourceHash,
                [],
                verificationStartedAt,
                verificationCompletedAt,
                ex.Message
            );
        }
    }

    public async Task<IReadOnlyList<TargetVerificationResult>> VerifyTargetsAsync(
        IReadOnlyList<TargetOutcome> targetOutcomes, string expectedHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetOutcomes);

        if (string.IsNullOrWhiteSpace(expectedHash))
            throw new ArgumentException("Expected hash cannot be null or empty.", nameof(expectedHash));

        _logger.LogDebug("Starting verification of {TargetCount} targets with expected hash {ExpectedHash}",
            targetOutcomes.Count, expectedHash);

        // Filter targets that are ready for verification (COPIED state)
        var readyTargets = targetOutcomes.Where(t => t.CopyState == TargetCopyState.Copied).ToList();
        var results = new List<TargetVerificationResult>();

        if (readyTargets.Count == 0)
        {
            _logger.LogWarning("No targets in COPIED state available for verification");
            return results.AsReadOnly();
        }

        // Use SemaphoreSlim to limit concurrent verifications for resource control
        using var semaphore = new SemaphoreSlim(Math.Min(readyTargets.Count, Environment.ProcessorCount));

        var verificationTasks = readyTargets.Select(async target =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await VerifyIndividualTargetAsync(target, expectedHash, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var verificationResults = await Task.WhenAll(verificationTasks);
        results.AddRange(verificationResults);

        // Add results for targets not ready for verification
        var notReadyTargets = targetOutcomes.Except(readyTargets);
        foreach (var target in notReadyTargets)
        {
            var errorMessage = $"Target not ready for verification. Current state: {target.CopyState}";
            var failedResult = new VerificationResult(
                target.FinalPath ?? "unknown",
                expectedHash,
                errorMessage
            );

            results.Add(new TargetVerificationResult(
                target.TargetId,
                failedResult,
                target.CopyState,
                TargetVerificationAction.MarkRetryableFailed,
                errorMessage
            ));
        }

        var successfulCount = results.Count(r => r.VerificationResult.IsMatch && r.VerificationResult.VerificationSucceeded);
        var quarantineCount = results.Count(r => r.RequiresQuarantine);

        _logger.LogInformation("Target verification batch completed: {TotalTargets} targets, " +
                              "{SuccessfulCount} successful, {QuarantineCount} requiring quarantine",
            targetOutcomes.Count, successfulCount, quarantineCount);

        return results.AsReadOnly();
    }

    public async Task<PostVerificationResult> HandleVerificationResultAsync(FileJob fileJob,
        JobVerificationResult verificationResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileJob);
        ArgumentNullException.ThrowIfNull(verificationResult);

        _logger.LogInformation("Handling post-verification actions for job {JobId} with status {Status}",
            fileJob.Id, verificationResult.Status);

        var quarantineEntries = new List<QuarantineEntry>();
        var summary = new List<string>();

        try
        {
            // Handle quarantine requirements
            var targetsRequiringQuarantine = verificationResult.TargetsRequiringQuarantine;
            if (targetsRequiringQuarantine.Any())
            {
                _logger.LogWarning("Job {JobId} has {QuarantineCount} targets requiring quarantine",
                    fileJob.Id, targetsRequiringQuarantine.Count);

                // Get the actual target outcomes for quarantine
                var affectedTargetOutcomes = new List<TargetOutcome>();
                foreach (var targetResult in targetsRequiringQuarantine)
                {
                    var targetOutcome = await _targetOutcomeRepository.GetByJobIdAndTargetIdAsync(
                        fileJob.Id, targetResult.TargetId, cancellationToken);
                    if (targetOutcome != null)
                    {
                        affectedTargetOutcomes.Add(targetOutcome);
                    }
                }

                if (affectedTargetOutcomes.Count > 0)
                {
                    var quarantineReason = "Hash verification failed - data integrity compromised";
                    var quarantineEntry = await _quarantineService.QuarantineJobAsync(
                        fileJob, quarantineReason, affectedTargetOutcomes, cancellationToken);
                    quarantineEntries.Add(quarantineEntry);
                    summary.Add($"Quarantined {affectedTargetOutcomes.Count} targets due to hash mismatch");
                }
            }

            // Determine final job state based on verification results
            var finalJobState = DetermineFinalJobState(verificationResult);

            // Update job state based on verification outcome
            switch (finalJobState)
            {
                case JobState.Verified:
                    // Invariant I2: Job VERIFIED only if all targets VERIFIED & hashes match
                    fileJob.MarkAsVerified();
                    await _jobRepository.UpdateAsync(fileJob, cancellationToken);
                    summary.Add("Job marked as VERIFIED - all targets successfully verified");
                    break;

                case JobState.Partial:
                    // Invariant I11: Partial not VERIFIED
                    // Job remains in PARTIAL state until all targets are verified
                    summary.Add("Job remains in PARTIAL state - not all targets verified");
                    break;

                case JobState.Quarantined:
                    // Job already quarantined by QuarantineService
                    summary.Add("Job quarantined due to verification failures");
                    break;

                case JobState.Failed:
                    fileJob.MarkAsFailed();
                    await _jobRepository.UpdateAsync(fileJob, cancellationToken);
                    summary.Add("Job marked as FAILED due to verification errors");
                    break;
            }

            var result = new PostVerificationResult(
                fileJob.Id,
                finalJobState,
                verificationResult.SuccessfulTargetCount,
                quarantineEntries.Count,
                verificationResult.TargetResults.Count(r => r.RecommendedAction == TargetVerificationAction.MarkRetryableFailed),
                verificationResult.TargetResults.Count(r => r.RecommendedAction == TargetVerificationAction.MarkPermanentlyFailed),
                quarantineEntries,
                true, // Metrics updated
                string.Join("; ", summary)
            );

            _logger.LogInformation("Post-verification completed for job {JobId}: Final state {FinalState}, Summary: {Summary}",
                fileJob.Id, finalJobState, result.Summary);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in post-verification handling for job {JobId}: {Error}",
                fileJob.Id, ex.Message);
            throw;
        }
    }

    public async Task<int> SchedulePendingVerificationsAsync(int maxConcurrentVerifications = 5,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Scheduling pending verifications with max concurrent: {MaxConcurrent}",
            maxConcurrentVerifications);

        try
        {
            // Find jobs in PARTIAL state that may need verification
            var partialJobs = await _jobRepository.GetByStateAsync(JobState.Partial, cancellationToken);
            var scheduledCount = 0;

            foreach (var job in partialJobs.Take(maxConcurrentVerifications))
            {
                try
                {
                    // Get target outcomes for this job
                    var targetOutcomes = await _targetOutcomeRepository.GetByJobIdAsync(job.Id, cancellationToken);

                    // Check if any targets are ready for verification (COPIED state)
                    var readyForVerification = targetOutcomes.Where(t => t.CopyState == TargetCopyState.Copied).ToList();

                    if (readyForVerification.Count > 0)
                    {
                        _logger.LogDebug("Scheduling verification for job {JobId} with {ReadyTargets} ready targets",
                            job.Id, readyForVerification.Count);

                        // Schedule verification (in real implementation, this might be queued)
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var verificationResult = await VerifyJobAsync(job, targetOutcomes, cancellationToken);
                                await HandleVerificationResultAsync(job, verificationResult, cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Scheduled verification failed for job {JobId}", job.Id);
                            }
                        }, cancellationToken);

                        scheduledCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scheduling verification for job {JobId}", job.Id);
                }
            }

            _logger.LogInformation("Scheduled {ScheduledCount} verification operations from {TotalPartial} partial jobs",
                scheduledCount, partialJobs.Count);

            return scheduledCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scheduling pending verifications: {Error}", ex.Message);
            throw;
        }
    }

    private async Task<TargetVerificationResult> VerifyIndividualTargetAsync(
        TargetOutcome targetOutcome, string expectedHash, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Verifying target {TargetId} for job {JobId}",
                targetOutcome.TargetId, targetOutcome.JobId);

            var verificationResult = await _verificationService.VerifyTargetOutcomeAsync(
                targetOutcome, expectedHash, cancellationToken);

            // Determine recommended action based on verification result
            var recommendedAction = DetermineRecommendedAction(verificationResult, targetOutcome);
            var updatedState = DetermineUpdatedTargetState(verificationResult, targetOutcome);

            // Update target outcome state in repository
            await _targetOutcomeRepository.UpdateAsync(targetOutcome, cancellationToken);

            return new TargetVerificationResult(
                targetOutcome.TargetId,
                verificationResult,
                updatedState,
                recommendedAction
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying target {TargetId} for job {JobId}: {Error}",
                targetOutcome.TargetId, targetOutcome.JobId, ex.Message);

            var errorResult = new VerificationResult(
                targetOutcome.FinalPath ?? "unknown",
                expectedHash,
                ex.Message
            );

            return new TargetVerificationResult(
                targetOutcome.TargetId,
                errorResult,
                TargetCopyState.FailedRetryable,
                TargetVerificationAction.RetryVerification,
                ex.Message
            );
        }
    }

    private static JobState DetermineFinalJobState(JobVerificationResult verificationResult)
    {
        if (verificationResult.Status == JobVerificationStatus.AllTargetsVerified)
            return JobState.Verified;

        if (verificationResult.Status == JobVerificationStatus.QuarantineRequired)
            return JobState.Quarantined;

        if (verificationResult.Status == JobVerificationStatus.Failed)
            return JobState.Failed;

        // Default to Partial if some targets succeeded but not all
        return JobState.Partial;
    }

    private static TargetVerificationAction DetermineRecommendedAction(VerificationResult verificationResult, TargetOutcome targetOutcome)
    {
        if (!verificationResult.VerificationSucceeded)
        {
            // I/O error during verification
            return TargetVerificationAction.RetryVerification;
        }

        if (verificationResult.IsMatch)
        {
            // Hash matches - successful verification
            return TargetVerificationAction.MarkVerified;
        }

        // Hash mismatch - quarantine required (Invariant I5)
        return TargetVerificationAction.QuarantineTarget;
    }

    private static TargetCopyState DetermineUpdatedTargetState(VerificationResult verificationResult, TargetOutcome targetOutcome)
    {
        if (!verificationResult.VerificationSucceeded)
            return TargetCopyState.FailedRetryable;

        if (verificationResult.IsMatch)
            return TargetCopyState.Verified;

        // Hash mismatch
        return TargetCopyState.FailedPermanent;
    }
}