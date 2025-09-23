using System.Diagnostics;
using Forker.Domain;
using Forker.Domain.Repositories;
using Forker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of retry orchestrator for coordinating retry operations.
/// Enforces Invariant I6 (MaxAttempts → FAILED_PERMANENT) and integrates with retry policies.
/// Designed for medical imaging file workflows where reliability is critical.
/// </summary>
public sealed class RetryOrchestrator : IRetryOrchestrator
{
    private readonly IRetryPolicy _retryPolicy;
    private readonly ITargetOutcomeRepository _targetOutcomeRepository;
    private readonly IJobRepository _jobRepository;
    private readonly ILogger<RetryOrchestrator> _logger;

    public RetryOrchestrator(
        IRetryPolicy retryPolicy,
        ITargetOutcomeRepository targetOutcomeRepository,
        IJobRepository jobRepository,
        ILogger<RetryOrchestrator> logger)
    {
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        _targetOutcomeRepository = targetOutcomeRepository ?? throw new ArgumentNullException(nameof(targetOutcomeRepository));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RetryProcessingResult> ProcessRetryableFailuresAsync(int maxConcurrentRetries = 10,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting retry processing with max concurrent retries: {MaxConcurrentRetries}",
            maxConcurrentRetries);

        var results = new List<RetryEvaluationResult>();
        var errors = new List<string>();
        var retriesScheduled = 0;
        var permanentFailures = 0;
        var retriesSkipped = 0;

        try
        {
            // Get all targets that are in retryable failed state
            var retryableTargets = await _targetOutcomeRepository.GetRetryableFailed(
                _retryPolicy.MaxAttempts, cancellationToken);

            _logger.LogDebug("Found {RetryableTargetCount} targets in retryable failed state",
                retryableTargets.Count);

            if (retryableTargets.Count == 0)
            {
                stopwatch.Stop();
                return new RetryProcessingResult(0, 0, 0, 0, results, stopwatch.Elapsed);
            }

            // Use SemaphoreSlim to limit concurrent processing
            using var semaphore = new SemaphoreSlim(maxConcurrentRetries, maxConcurrentRetries);

            var processingTasks = retryableTargets.Select(async target =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ProcessSingleRetryableTarget(target, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var taskResults = await Task.WhenAll(processingTasks);
            results.AddRange(taskResults.Where(r => r != null)!);

            // Aggregate results
            foreach (var result in results)
            {
                switch (result.ActionTaken)
                {
                    case RetryAction.RetryScheduled:
                        retriesScheduled++;
                        break;
                    case RetryAction.MarkedPermanentlyFailed:
                        permanentFailures++;
                        break;
                    case RetryAction.RetrySkipped:
                        retriesSkipped++;
                        break;
                    case RetryAction.EvaluationError:
                        if (!string.IsNullOrEmpty(result.ErrorMessage))
                            errors.Add(result.ErrorMessage);
                        break;
                }
            }

            stopwatch.Stop();

            _logger.LogInformation("Retry processing completed: {EvaluatedCount} evaluated, " +
                                  "{RetriesScheduled} scheduled, {PermanentFailures} permanent failures, " +
                                  "{RetriesSkipped} skipped, {ProcessingTimeMs}ms",
                retryableTargets.Count, retriesScheduled, permanentFailures, retriesSkipped,
                stopwatch.ElapsedMilliseconds);

            return new RetryProcessingResult(
                retryableTargets.Count,
                retriesScheduled,
                permanentFailures,
                retriesSkipped,
                results,
                stopwatch.Elapsed,
                errors);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error during retry processing: {Error}", ex.Message);
            errors.Add($"Processing error: {ex.Message}");

            return new RetryProcessingResult(0, 0, 0, 0, results, stopwatch.Elapsed, errors);
        }
    }

    public async Task<RetryEvaluationResult> EvaluateAndScheduleRetryAsync(TargetOutcome targetOutcome,
        Exception lastException, OperationType operationType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetOutcome);
        ArgumentNullException.ThrowIfNull(lastException);

        _logger.LogDebug("Evaluating retry for target {TargetId} of job {JobId}, operation {OperationType}, attempt {AttemptNumber}",
            targetOutcome.TargetId, targetOutcome.JobId, operationType, targetOutcome.Attempts);

        try
        {
            // Get retry decision from policy
            var decision = _retryPolicy.ShouldRetry(targetOutcome.Attempts, lastException, operationType);

            if (decision.ShouldRetry)
            {
                // Schedule retry
                var scheduledRetryAt = DateTime.UtcNow.Add(decision.Delay);

                _logger.LogInformation("Scheduling retry for target {TargetId} of job {JobId} at {ScheduledRetryAt}: {Reason}",
                    targetOutcome.TargetId, targetOutcome.JobId, scheduledRetryAt, decision.Reason);

                // Reset target to pending state for retry
                targetOutcome.Retry();
                await _targetOutcomeRepository.UpdateAsync(targetOutcome, cancellationToken);

                return new RetryEvaluationResult(
                    targetOutcome.TargetId,
                    targetOutcome.JobId,
                    decision,
                    RetryAction.RetryScheduled,
                    scheduledRetryAt);
            }
            else if (decision.IsPermanentFailure)
            {
                // Mark as permanently failed
                await MarkAsPermanentlyFailedAsync(targetOutcome, decision.Reason, cancellationToken);

                return new RetryEvaluationResult(
                    targetOutcome.TargetId,
                    targetOutcome.JobId,
                    decision,
                    RetryAction.MarkedPermanentlyFailed);
            }
            else
            {
                _logger.LogDebug("No retry action required for target {TargetId} of job {JobId}: {Reason}",
                    targetOutcome.TargetId, targetOutcome.JobId, decision.Reason);

                return new RetryEvaluationResult(
                    targetOutcome.TargetId,
                    targetOutcome.JobId,
                    decision,
                    RetryAction.NoActionRequired);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating retry for target {TargetId} of job {JobId}: {Error}",
                targetOutcome.TargetId, targetOutcome.JobId, ex.Message);

            var errorDecision = RetryDecision.PermanentFailure($"Evaluation error: {ex.Message}");
            return new RetryEvaluationResult(
                targetOutcome.TargetId,
                targetOutcome.JobId,
                errorDecision,
                RetryAction.EvaluationError,
                errorMessage: ex.Message);
        }
    }

    public async Task MarkAsPermanentlyFailedAsync(TargetOutcome targetOutcome, string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetOutcome);

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be null, empty, or whitespace.", nameof(reason));

        _logger.LogWarning("Marking target {TargetId} of job {JobId} as permanently failed after {AttemptCount} attempts: {Reason}",
            targetOutcome.TargetId, targetOutcome.JobId, targetOutcome.Attempts, reason);

        try
        {
            // Mark target as permanently failed (Invariant I6)
            targetOutcome.MarkAsPermanentlyFailed(reason);
            await _targetOutcomeRepository.UpdateAsync(targetOutcome, cancellationToken);

            // Check if this affects the overall job state
            await EvaluateJobStateAfterPermanentFailure(targetOutcome.JobId, cancellationToken);

            _logger.LogError("Target {TargetId} of job {JobId} marked as permanently failed: {Reason}",
                targetOutcome.TargetId, targetOutcome.JobId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking target {TargetId} of job {JobId} as permanently failed: {Error}",
                targetOutcome.TargetId, targetOutcome.JobId, ex.Message);
            throw;
        }
    }

    public async Task<RetryStatistics> GetRetryStatisticsAsync(DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Calculating retry statistics since {Since}", since);

        try
        {
            // This is a placeholder implementation for Phase 7
            // In a full implementation, this would query retry history from a dedicated table
            // For now, we'll return basic statistics from current target outcomes

            var allTargets = await _targetOutcomeRepository.GetByCopyStateAsync(TargetCopyState.FailedPermanent, cancellationToken);
            var retryableTargets = await _targetOutcomeRepository.GetRetryableFailed(_retryPolicy.MaxAttempts, cancellationToken);

            var totalRetriesAttempted = allTargets.Sum(t => t.Attempts) + retryableTargets.Sum(t => t.Attempts);
            var successfulRetries = 0; // Would need retry history to calculate this properly
            var permanentFailuresAfterMaxAttempts = allTargets.Count(t => t.Attempts >= _retryPolicy.MaxAttempts);
            var averageAttemptsToSuccess = 0.0; // Would need success history to calculate this

            var retriesByOperationType = new Dictionary<OperationType, int>();
            var failureReasonCounts = new Dictionary<string, int>();

            // Count failure reasons from current permanent failures
            foreach (var target in allTargets)
            {
                if (!string.IsNullOrEmpty(target.LastError))
                {
                    failureReasonCounts[target.LastError] = failureReasonCounts.GetValueOrDefault(target.LastError, 0) + 1;
                }
            }

            var statistics = new RetryStatistics(
                totalRetriesAttempted,
                successfulRetries,
                permanentFailuresAfterMaxAttempts,
                averageAttemptsToSuccess,
                retriesByOperationType,
                failureReasonCounts,
                DateTime.UtcNow);

            _logger.LogDebug("Retry statistics calculated: {TotalRetries} total retries, {PermanentFailures} permanent failures",
                totalRetriesAttempted, permanentFailuresAfterMaxAttempts);

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating retry statistics: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<ManualRetryResult> ManualRetryAsync(TargetOutcome targetOutcome, OperationType operationType,
        string reason, string triggeredBy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetOutcome);

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be null, empty, or whitespace.", nameof(reason));

        if (string.IsNullOrWhiteSpace(triggeredBy))
            throw new ArgumentException("TriggeredBy cannot be null, empty, or whitespace.", nameof(triggeredBy));

        _logger.LogWarning("Manual retry triggered for target {TargetId} of job {JobId} by {TriggeredBy}: {Reason}",
            targetOutcome.TargetId, targetOutcome.JobId, triggeredBy, reason);

        try
        {
            // Check if target is in a retryable state
            if (!targetOutcome.CanRetry && targetOutcome.CopyState != TargetCopyState.FailedPermanent)
            {
                var message = $"Target {targetOutcome.TargetId} is not in a retryable state (current state: {targetOutcome.CopyState})";
                _logger.LogWarning("Manual retry failed: {Message}", message);
                return ManualRetryResult.Failed(targetOutcome.TargetId, message, triggeredBy);
            }

            // Reset target for retry (even if permanently failed - manual override)
            if (targetOutcome.CopyState == TargetCopyState.FailedPermanent)
            {
                // For permanent failures, we need to reset the state manually
                // This bypasses the normal retry logic for administrative overrides
                _logger.LogInformation("Manual retry overriding permanent failure for target {TargetId}", targetOutcome.TargetId);
            }

            targetOutcome.Retry();
            await _targetOutcomeRepository.UpdateAsync(targetOutcome, cancellationToken);

            var successMessage = $"Manual retry scheduled for {operationType} operation. Reason: {reason}";
            _logger.LogInformation("Manual retry successful for target {TargetId} of job {JobId}: {Message}",
                targetOutcome.TargetId, targetOutcome.JobId, successMessage);

            return ManualRetryResult.Successful(targetOutcome.TargetId, successMessage, triggeredBy);
        }
        catch (Exception ex)
        {
            var errorMessage = $"Manual retry failed: {ex.Message}";
            _logger.LogError(ex, "Manual retry failed for target {TargetId} of job {JobId}: {Error}",
                targetOutcome.TargetId, targetOutcome.JobId, ex.Message);

            return ManualRetryResult.Failed(targetOutcome.TargetId, errorMessage, triggeredBy);
        }
    }

    private async Task<RetryEvaluationResult?> ProcessSingleRetryableTarget(TargetOutcome target,
        CancellationToken cancellationToken)
    {
        try
        {
            // For this implementation, we'll assume the last operation was a file copy
            // In a more sophisticated system, we'd track the specific operation that failed
            var operationType = DetermineOperationType(target);
            var syntheticException = CreateSyntheticException(target);

            return await EvaluateAndScheduleRetryAsync(target, syntheticException, operationType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing retryable target {TargetId} of job {JobId}: {Error}",
                target.TargetId, target.JobId, ex.Message);

            var errorDecision = RetryDecision.PermanentFailure($"Processing error: {ex.Message}");
            return new RetryEvaluationResult(
                target.TargetId,
                target.JobId,
                errorDecision,
                RetryAction.EvaluationError,
                errorMessage: ex.Message);
        }
    }

    private static OperationType DetermineOperationType(TargetOutcome target)
    {
        // Simple heuristic based on the target state and error message
        if (target.LastError?.Contains("hash", StringComparison.OrdinalIgnoreCase) == true ||
            target.LastError?.Contains("verification", StringComparison.OrdinalIgnoreCase) == true)
        {
            return OperationType.FileVerification;
        }

        if (target.LastError?.Contains("copy", StringComparison.OrdinalIgnoreCase) == true ||
            target.LastError?.Contains("write", StringComparison.OrdinalIgnoreCase) == true)
        {
            return OperationType.FileCopy;
        }

        // Default to file copy operation
        return OperationType.FileCopy;
    }

    private static InvalidOperationException CreateSyntheticException(TargetOutcome target)
    {
        // Create a synthetic exception based on the last error message
        // This is a simplification - in a real system, we'd store the actual exception details
        var errorMessage = target.LastError ?? "Unknown error";
        return new InvalidOperationException(errorMessage);
    }

    private async Task EvaluateJobStateAfterPermanentFailure(FileJobId jobId, CancellationToken cancellationToken)
    {
        try
        {
            // Get the job and all its target outcomes
            var job = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
            if (job == null)
            {
                _logger.LogWarning("Job {JobId} not found when evaluating state after permanent failure", jobId);
                return;
            }

            var allTargets = await _targetOutcomeRepository.GetByJobIdAsync(jobId, cancellationToken);

            // Check if all targets have failed permanently
            var allPermanentlyFailed = allTargets.All(t => t.HasFailedPermanently);

            if (allPermanentlyFailed && job.State != JobState.Failed)
            {
                _logger.LogWarning("All targets for job {JobId} have failed permanently, marking job as failed", jobId);
                job.MarkAsFailed();
                await _jobRepository.UpdateAsync(job, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating job state after permanent failure for job {JobId}: {Error}",
                jobId, ex.Message);
            // Don't rethrow - this is a secondary operation
        }
    }
}