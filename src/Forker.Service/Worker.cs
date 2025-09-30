using Forker.Domain;
using Forker.Domain.Repositories;
using Forker.Domain.Services;
using Forker.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Forker.Service;

/// <summary>
/// Main worker service that orchestrates the file discovery, copying, and verification pipeline.
/// This service wires together the complete ForkerDotNet processing flow:
/// 1. File Discovery (monitors Input directory)
/// 2. Copy Orchestration (copies to Clinical + Research targets)
/// 3. Verification Orchestration (verifies hashes)
/// 4. Cleanup (removes files from Input after verification)
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IFileDiscoveryService _fileDiscoveryService;
    private readonly ICopyOrchestrator _copyOrchestrator;
    private readonly IVerificationOrchestrator _verificationOrchestrator;
    private readonly IJobRepository _jobRepository;
    private readonly ITargetOutcomeRepository _targetOutcomeRepository;
    private readonly DirectoryConfiguration _directories;
    private readonly TargetConfiguration _targetConfig;
    private readonly IServiceScopeFactory _scopeFactory;

    public Worker(
        ILogger<Worker> logger,
        IFileDiscoveryService fileDiscoveryService,
        IServiceScopeFactory scopeFactory,
        IOptions<DirectoryConfiguration> directories,
        IOptions<TargetConfiguration> targetConfig)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fileDiscoveryService = fileDiscoveryService ?? throw new ArgumentNullException(nameof(fileDiscoveryService));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _directories = directories?.Value ?? throw new ArgumentNullException(nameof(directories));
        _targetConfig = targetConfig?.Value ?? throw new ArgumentNullException(nameof(targetConfig));

        // Create scoped services for this worker (repositories are scoped)
        var scope = _scopeFactory.CreateScope();
        _copyOrchestrator = scope.ServiceProvider.GetRequiredService<ICopyOrchestrator>();
        _verificationOrchestrator = scope.ServiceProvider.GetRequiredService<IVerificationOrchestrator>();
        _jobRepository = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        _targetOutcomeRepository = scope.ServiceProvider.GetRequiredService<ITargetOutcomeRepository>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Forker Worker Service starting - Phase 11.0 (Production Pipeline)");

        try
        {
            // Ensure directories exist
            EnsureDirectoriesExist();

            // Wire up event handlers
            _fileDiscoveryService.FileDiscovered += OnFileDiscovered;

            // Start file discovery service
            _logger.LogInformation("Starting file discovery service - monitoring: {SourceDir}", _directories.Source);
            await _fileDiscoveryService.StartAsync(stoppingToken);

            _logger.LogInformation("ForkerDotNet is now running - Ready to process files");

            // Keep the service running
            while (!stoppingToken.IsCancellationRequested)
            {
                // Heartbeat logging
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Worker heartbeat at: {Time}", DateTimeOffset.Now);
                }

                // Check for pending verifications periodically
                try
                {
                    await _verificationOrchestrator.SchedulePendingVerificationsAsync(
                        maxConcurrentVerifications: 5,
                        cancellationToken: stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scheduling pending verifications");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Worker service cancellation requested");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in Worker service");
            throw;
        }
        finally
        {
            // Cleanup
            _fileDiscoveryService.FileDiscovered -= OnFileDiscovered;
            await _fileDiscoveryService.StopAsync(CancellationToken.None);
            _logger.LogInformation("Forker Worker Service stopped");
        }
    }

    /// <summary>
    /// Event handler for file discovery events.
    /// Creates a FileJob and initiates the copy pipeline.
    /// </summary>
    private async void OnFileDiscovered(object? sender, FileDiscoveredEventArgs e)
    {
        var jobId = FileJobId.New();

        try
        {
            _logger.LogInformation("File discovered: {FilePath} ({FileSize:N0} bytes) - JobId: {JobId}",
                e.FilePath, e.FileSize, jobId);

            // Collect target IDs from configuration
            var targetIds = _targetConfig.EnabledTargets.Select(t => new TargetId(t.Id)).ToList();

            // Create FileJob entity (starts in Discovered state)
            var fileJob = new FileJob(
                id: jobId,
                sourcePath: e.FilePath,
                initialSize: e.FileSize,
                requiredTargets: targetIds);

            // Save initial job state (DISCOVERED)
            await _jobRepository.SaveAsync(fileJob);

            // Transition to QUEUED state
            fileJob.MarkAsQueued();
            await _jobRepository.UpdateAsync(fileJob);

            // Create target outcomes for each enabled target
            foreach (var target in _targetConfig.EnabledTargets)
            {
                var targetId = new TargetId(target.Id);
                var targetOutcome = new TargetOutcome(
                    jobId: jobId,
                    targetId: targetId);

                await _targetOutcomeRepository.SaveAsync(targetOutcome);
            }

            _logger.LogInformation("FileJob created - transitioning to IN_PROGRESS - JobId: {JobId}", jobId);

            // Transition to IN_PROGRESS state
            fileJob.MarkAsInProgress();
            await _jobRepository.UpdateAsync(fileJob);

            // Start copy orchestration
            var copyResult = await _copyOrchestrator.ProcessFileAsync(
                fileJobId: jobId,
                sourceFilePath: e.FilePath,
                cancellationToken: CancellationToken.None);

            if (copyResult.Success)
            {
                _logger.LogInformation("Copy completed successfully - JobId: {JobId}, Duration: {Duration}",
                    jobId, copyResult.TotalDuration);

                // Update job with source hash
                fileJob.SetSourceHash(copyResult.SourceHash);

                // Transition to PARTIAL state (waiting for verification)
                fileJob.MarkAsPartial();
                await _jobRepository.UpdateAsync(fileJob);

                // Start verification orchestration
                var targetOutcomes = await _targetOutcomeRepository.GetByJobIdAsync(jobId);
                var verificationResult = await _verificationOrchestrator.VerifyJobAsync(
                    fileJob: fileJob,
                    targetOutcomes: targetOutcomes,
                    cancellationToken: CancellationToken.None);

                // Handle verification results
                var postVerificationResult = await _verificationOrchestrator.HandleVerificationResultAsync(
                    fileJob: fileJob,
                    verificationResult: verificationResult,
                    cancellationToken: CancellationToken.None);

                if (postVerificationResult.FinalJobState == JobState.Verified)
                {
                    _logger.LogInformation("File processing complete - JobId: {JobId}, State: VERIFIED", jobId);

                    // Cleanup: Delete source file from Input directory
                    try
                    {
                        if (File.Exists(e.FilePath))
                        {
                            File.Delete(e.FilePath);
                            _logger.LogInformation("Source file deleted from Input - JobId: {JobId}, Path: {FilePath}",
                                jobId, e.FilePath);
                        }
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogError(cleanupEx, "Failed to delete source file - JobId: {JobId}, Path: {FilePath}",
                            jobId, e.FilePath);
                    }
                }
                else
                {
                    _logger.LogWarning("File processing completed with issues - JobId: {JobId}, State: {State}",
                        jobId, postVerificationResult.FinalJobState);
                }
            }
            else
            {
                _logger.LogError("Copy failed - JobId: {JobId}, Error: {Error}",
                    jobId, copyResult.ErrorMessage);

                // Transition to FAILED state
                fileJob.MarkAsFailed();
                await _jobRepository.UpdateAsync(fileJob);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing discovered file - JobId: {JobId}, Path: {FilePath}",
                jobId, e.FilePath);
        }
    }

    /// <summary>
    /// Ensures all required directories exist before starting processing.
    /// </summary>
    private void EnsureDirectoriesExist()
    {
        var directoriesToCreate = new[]
        {
            _directories.Source,
            _directories.Error,
            _directories.Processing
        };

        // Also ensure target directories exist
        var targetDirectories = _targetConfig.EnabledTargets.Select(t => t.Path);

        foreach (var dir in directoriesToCreate.Concat(targetDirectories))
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                _logger.LogInformation("Created directory: {Directory}", dir);
            }
        }

        _logger.LogInformation("All required directories verified");
    }
}
