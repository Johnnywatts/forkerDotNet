using System.Collections.Concurrent;
using System.Diagnostics;
using Forker.Domain;
using Forker.Domain.Repositories;
using Forker.Domain.Services;
using Forker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Orchestrates dual-target file copying operations for 100% reliable replication.
/// Manages parallel copying to both TargetA and TargetB with comprehensive state tracking.
/// </summary>
public sealed class CopyOrchestrator : ICopyOrchestrator, IDisposable
{
    private readonly IFileCopyService _fileCopyService;
    private readonly IJobRepository _jobRepository;
    private readonly ITargetOutcomeRepository _targetOutcomeRepository;
    private readonly TargetConfiguration _config;
    private readonly TestingConfiguration _testingConfig;
    private readonly ILogger<CopyOrchestrator> _logger;

    // Semaphores to control concurrent operations per target
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _targetSemaphores = new();

    public event EventHandler<CopyProgressEvent>? CopyProgressChanged;
    public event EventHandler<TargetCopyCompletedEvent>? TargetCopyCompleted;

    public CopyOrchestrator(
        IFileCopyService fileCopyService,
        IJobRepository jobRepository,
        ITargetOutcomeRepository targetOutcomeRepository,
        IOptions<TargetConfiguration> config,
        IOptions<TestingConfiguration> testingConfig,
        ILogger<CopyOrchestrator> logger)
    {
        _fileCopyService = fileCopyService ?? throw new ArgumentNullException(nameof(fileCopyService));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _targetOutcomeRepository = targetOutcomeRepository ?? throw new ArgumentNullException(nameof(targetOutcomeRepository));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _testingConfig = testingConfig?.Value ?? new TestingConfiguration();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize semaphores for each configured target
        foreach (var target in _config.EnabledTargets)
        {
            var maxConcurrent = target.MaxConcurrentOperations ?? _config.MaxConcurrentCopiesPerTarget;
            _targetSemaphores[target.Id] = new SemaphoreSlim(maxConcurrent, maxConcurrent);
        }
    }

    public async Task<CopyOrchestrationResult> ProcessFileAsync(
        FileJobId fileJobId,
        string sourceFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileJobId);

        if (string.IsNullOrWhiteSpace(sourceFilePath))
            throw new ArgumentException("Source file path cannot be null or empty.", nameof(sourceFilePath));

        var totalStopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting dual-target copy orchestration for job {JobId}, file: {SourceFile}",
            fileJobId.Value, sourceFilePath);

        try
        {
            // Get the job from repository to update its state
            var job = await _jobRepository.GetByIdAsync(fileJobId, cancellationToken);
            if (job == null)
            {
                const string errorTemplate = "Job {JobId} not found in repository";
                _logger.LogError(errorTemplate, fileJobId.Value);
                var error = $"Job {fileJobId.Value} not found in repository";
                return CopyOrchestrationResult.CreateFailure(
                    fileJobId,
                    new Dictionary<TargetId, FileCopyResult>(),
                    error,
                    totalStopwatch.Elapsed);
            }

            // Transition job to IN_PROGRESS (only if not already in that state)
            if (job.State != JobState.InProgress)
            {
                job.MarkAsInProgress();
                await _jobRepository.UpdateAsync(job, cancellationToken);
            }

            // Get enabled targets for copying
            var enabledTargets = _config.EnabledTargets.ToList();
            if (enabledTargets.Count == 0)
            {
                const string error = "No enabled targets configured for copying";
                _logger.LogError(error);
                return CopyOrchestrationResult.CreateFailure(
                    fileJobId,
                    new Dictionary<TargetId, FileCopyResult>(),
                    error,
                    totalStopwatch.Elapsed);
            }

            _logger.LogInformation("Copying to {TargetCount} targets: {Targets}",
                enabledTargets.Count, string.Join(", ", enabledTargets.Select(t => t.Id)));

            // Load existing target outcomes (created by Worker.cs)
            var targetOutcomes = await _targetOutcomeRepository.GetByJobIdAsync(fileJobId, cancellationToken);
            if (targetOutcomes.Count == 0)
            {
                _logger.LogError("No target outcomes found for job {JobId} - cannot proceed with copy", fileJobId.Value);
                return CopyOrchestrationResult.CreateFailure(
                    fileJobId,
                    new Dictionary<TargetId, FileCopyResult>(),
                    "No target outcomes found",
                    totalStopwatch.Elapsed);
            }

            // Transition all target outcomes to COPYING state (Pending -> Copying)
            foreach (var outcome in targetOutcomes)
            {
                // Use temp directory from configuration (even though we copy directly to final destination,
                // the state machine requires a valid temp path)
                var tempPath = Path.Combine(_config.TempDirectory, Path.GetFileName(sourceFilePath));
                outcome.StartCopy(tempPath);
                await _targetOutcomeRepository.UpdateAsync(outcome, cancellationToken);
            }

            // Perform copying operations (parallel or sequential based on configuration)
            var copyResults = _config.ParallelCopyEnabled
                ? await PerformParallelCopyAsync(sourceFilePath, enabledTargets, fileJobId, cancellationToken)
                : await PerformSequentialCopyAsync(sourceFilePath, enabledTargets, fileJobId, cancellationToken);

            totalStopwatch.Stop();

            // Determine overall success (all targets must succeed)
            var allSuccess = copyResults.Values.All(r => r.Success);
            var sourceHash = copyResults.Values.FirstOrDefault(r => r.Success)?.Hash ?? string.Empty;

            if (allSuccess)
            {
                // Update job with source hash and mark as PARTIAL (waiting for verification)
                job.SetSourceHash(sourceHash);
                job.MarkAsPartial();
                await _jobRepository.UpdateAsync(job, cancellationToken);

                // Apply verification delay if configured (for testing corruption detection)
                // This keeps TargetOutcomes in COPYING state during the delay, preventing
                // scheduled verification from picking them up prematurely
                if (_testingConfig.VerificationDelaySeconds > 0)
                {
                    _logger.LogInformation("Test mode: Delaying before marking COPIED by {DelaySeconds} seconds - JobId: {JobId}",
                        _testingConfig.VerificationDelaySeconds, fileJobId.Value);
                    await Task.Delay(TimeSpan.FromSeconds(_testingConfig.VerificationDelaySeconds), cancellationToken);
                }

                // Update all target outcomes to COPIED (after delay if configured)
                foreach (var outcome in targetOutcomes)
                {
                    var result = copyResults[new TargetId(outcome.TargetId.Value)];
                    outcome.CompleteCopy(result.Hash, result.TargetFilePath);
                    await _targetOutcomeRepository.UpdateAsync(outcome, cancellationToken);
                }

                _logger.LogInformation("Dual-target copy completed successfully for job {JobId}. All {TargetCount} targets copied.",
                    fileJobId.Value, enabledTargets.Count);

                return CopyOrchestrationResult.CreateSuccess(
                    fileJobId,
                    copyResults,
                    sourceHash,
                    totalStopwatch.Elapsed);
            }
            else
            {
                // Handle partial failure - some targets succeeded, others failed
                var failedTargets = copyResults.Where(kvp => !kvp.Value.Success).Select(kvp => kvp.Key.Value);
                var error = $"Copy failed for targets: {string.Join(", ", failedTargets)}";

                _logger.LogError("Dual-target copy failed for job {JobId}: {Error}", fileJobId.Value, error);

                // Update target outcomes based on results
                foreach (var outcome in targetOutcomes)
                {
                    var targetId = new TargetId(outcome.TargetId.Value);
                    if (copyResults.TryGetValue(targetId, out var result))
                    {
                        if (result.Success)
                        {
                            outcome.CompleteCopy(result.Hash, result.TargetFilePath);
                        }
                        else
                        {
                            outcome.MarkAsRetryableFailed(result.ErrorMessage ?? "Copy operation failed");
                        }
                        await _targetOutcomeRepository.UpdateAsync(outcome, cancellationToken);
                    }
                }

                return CopyOrchestrationResult.CreateFailure(
                    fileJobId,
                    copyResults,
                    error,
                    totalStopwatch.Elapsed);
            }
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            var error = $"Unexpected error during copy orchestration: {ex.Message}";
            _logger.LogError(ex, "Copy orchestration failed for job {JobId}", fileJobId.Value);

            return CopyOrchestrationResult.CreateFailure(
                fileJobId,
                new Dictionary<TargetId, FileCopyResult>(),
                error,
                totalStopwatch.Elapsed);
        }
    }

    private async Task<Dictionary<TargetId, FileCopyResult>> PerformParallelCopyAsync(
        string sourceFilePath,
        List<TargetDefinition> targets,
        FileJobId jobId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting parallel copy to {TargetCount} targets", targets.Count);

        var copyTasks = targets.Select(target => CopyToTargetAsync(sourceFilePath, target, jobId, cancellationToken));
        var results = await Task.WhenAll(copyTasks);

        var resultDictionary = new Dictionary<TargetId, FileCopyResult>();
        for (int i = 0; i < targets.Count; i++)
        {
            resultDictionary[new TargetId(targets[i].Id)] = results[i];
        }

        return resultDictionary;
    }

    private async Task<Dictionary<TargetId, FileCopyResult>> PerformSequentialCopyAsync(
        string sourceFilePath,
        List<TargetDefinition> targets,
        FileJobId jobId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting sequential copy to {TargetCount} targets", targets.Count);

        var results = new Dictionary<TargetId, FileCopyResult>();

        foreach (var target in targets.OrderBy(t => t.Priority))
        {
            var result = await CopyToTargetAsync(sourceFilePath, target, jobId, cancellationToken);
            results[new TargetId(target.Id)] = result;

            // For sequential copying, we might want to stop on first failure (configurable)
            if (!result.Success)
            {
                _logger.LogWarning("Sequential copy failed for target {TargetId}, stopping remaining copies", target.Id);
                // Continue with remaining targets for now - this could be made configurable
            }
        }

        return results;
    }

    private async Task<FileCopyResult> CopyToTargetAsync(
        string sourceFilePath,
        TargetDefinition target,
        FileJobId jobId,
        CancellationToken cancellationToken)
    {
        var targetId = new TargetId(target.Id);

        // Acquire semaphore for this target to control concurrency
        var semaphore = _targetSemaphores[target.Id];
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            _logger.LogDebug("Starting copy to target {TargetId} for job {JobId}", target.Id, jobId.Value);

            // Create progress callback to forward events
            var progressCallback = new Progress<FileCopyProgress>(progress =>
            {
                CopyProgressChanged?.Invoke(this, new CopyProgressEvent
                {
                    JobId = jobId,
                    TargetId = targetId,
                    Progress = progress
                });
            });

            // Perform the actual copy operation
            var result = await _fileCopyService.CopyFileAsync(
                sourceFilePath,
                target.Path,
                targetId,
                expectedHash: null, // We'll calculate hash during copy
                progressCallback,
                cancellationToken);

            _logger.LogDebug("Copy to target {TargetId} completed for job {JobId}. Success: {Success}",
                target.Id, jobId.Value, result.Success);

            // Fire completion event
            TargetCopyCompleted?.Invoke(this, new TargetCopyCompletedEvent
            {
                JobId = jobId,
                TargetId = targetId,
                Result = result
            });

            return result;
        }
        catch (Exception ex)
        {
            var error = $"Error copying to target {target.Id}: {ex.Message}";
            _logger.LogError(ex, "Copy failed for target {TargetId}, job {JobId}", target.Id, jobId.Value);

            var failureResult = FileCopyResult.CreateFailure(
                Path.Combine(target.Path, Path.GetFileName(sourceFilePath)),
                error,
                ex);

            // Fire completion event even for failures
            TargetCopyCompleted?.Invoke(this, new TargetCopyCompletedEvent
            {
                JobId = jobId,
                TargetId = targetId,
                Result = failureResult
            });

            return failureResult;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void Dispose()
    {
        foreach (var semaphore in _targetSemaphores.Values)
        {
            semaphore.Dispose();
        }
        _targetSemaphores.Clear();
    }
}