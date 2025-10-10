using System.Diagnostics;
using Forker.Domain;
using Forker.Domain.Repositories;
using Forker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of verification service for medical imaging file integrity validation.
/// Uses streaming SHA-256 hashing to verify copied files against source hashes.
/// Optimized for 500MB-20GB medical imaging files with constant memory usage.
/// </summary>
public sealed class VerificationService : IVerificationService
{
    private readonly IHashingService _hashingService;
    private readonly ITargetOutcomeRepository _targetOutcomeRepository;
    private readonly IStateChangeLogger _stateChangeLogger;
    private readonly ILogger<VerificationService> _logger;

    public VerificationService(
        IHashingService hashingService,
        ITargetOutcomeRepository targetOutcomeRepository,
        IStateChangeLogger stateChangeLogger,
        ILogger<VerificationService> logger)
    {
        _hashingService = hashingService ?? throw new ArgumentNullException(nameof(hashingService));
        _targetOutcomeRepository = targetOutcomeRepository ?? throw new ArgumentNullException(nameof(targetOutcomeRepository));
        _stateChangeLogger = stateChangeLogger ?? throw new ArgumentNullException(nameof(stateChangeLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<VerificationResult> VerifyFileHashAsync(string filePath, string expectedHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        if (string.IsNullOrWhiteSpace(expectedHash))
            throw new ArgumentException("Expected hash cannot be null or empty.", nameof(expectedHash));

        _logger.LogDebug("Starting file verification for {FilePath} with expected hash {ExpectedHash}",
            filePath, expectedHash);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Check if file exists before attempting verification
            if (!File.Exists(filePath))
            {
                var errorMessage = $"File not found during verification: {filePath}";
                _logger.LogWarning("File not found during verification: {FilePath}", filePath);
                return new VerificationResult(filePath, expectedHash, errorMessage);
            }

            // Get file size for result
            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Length;

            // Calculate hash using existing hashing service
            var computedHash = await _hashingService.CalculateHashAsync(filePath, cancellationToken);
            stopwatch.Stop();

            var result = new VerificationResult(filePath, computedHash, expectedHash, fileSize, stopwatch.Elapsed);

            if (result.IsMatch)
            {
                _logger.LogDebug("File verification successful for {FilePath}. Hash matches: {Hash}",
                    filePath, computedHash);
            }
            else
            {
                _logger.LogWarning("File verification FAILED for {FilePath}. Expected: {ExpectedHash}, Computed: {ComputedHash}",
                    filePath, expectedHash, computedHash);
            }

            return result;
        }
        catch (UnauthorizedAccessException ex)
        {
            stopwatch.Stop();
            var errorMessage = $"Access denied during verification: {ex.Message}";
            _logger.LogError(ex, "Access denied verifying file {FilePath}", filePath);
            return new VerificationResult(filePath, expectedHash, errorMessage);
        }
        catch (IOException ex)
        {
            stopwatch.Stop();
            var errorMessage = $"I/O error during verification: {ex.Message}";
            _logger.LogError(ex, "I/O error verifying file {FilePath}", filePath);
            return new VerificationResult(filePath, expectedHash, errorMessage);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation("File verification cancelled for {FilePath}", filePath);
            throw; // Re-throw cancellation exceptions
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorMessage = $"Unexpected error during verification: {ex.Message}";
            _logger.LogError(ex, "Unexpected error verifying file {FilePath}", filePath);
            return new VerificationResult(filePath, expectedHash, errorMessage);
        }
    }

    public async Task<VerificationResult> VerifyTargetOutcomeAsync(TargetOutcome targetOutcome, string expectedHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetOutcome);

        if (string.IsNullOrWhiteSpace(expectedHash))
            throw new ArgumentException("Expected hash cannot be null or empty.", nameof(expectedHash));

        _logger.LogDebug("Starting target outcome verification for Job {JobId}, Target {TargetId}",
            targetOutcome.JobId, targetOutcome.TargetId);

        // Validate target outcome state
        if (targetOutcome.CopyState != TargetCopyState.Copied)
        {
            var errorMessage = $"Target outcome must be in COPIED state for verification. Current state: {targetOutcome.CopyState}";
            _logger.LogWarning("Invalid target state for verification: Job {JobId}, Target {TargetId}, State {State}",
                targetOutcome.JobId, targetOutcome.TargetId, targetOutcome.CopyState);
            return new VerificationResult(targetOutcome.FinalPath ?? "unknown", expectedHash, errorMessage);
        }

        if (string.IsNullOrWhiteSpace(targetOutcome.FinalPath))
        {
            var errorMessage = "Target outcome does not have a final path set";
            _logger.LogWarning("Target outcome missing final path: Job {JobId}, Target {TargetId}",
                targetOutcome.JobId, targetOutcome.TargetId);
            return new VerificationResult("unknown", expectedHash, errorMessage);
        }

        try
        {
            // Update target state to VERIFYING before starting verification
            var previousState = targetOutcome.CopyState.ToString();
            targetOutcome.StartVerification();

            // Log state change to audit trail
            await _stateChangeLogger.LogTargetStateChangeAsync(
                targetOutcome.JobId.Value.ToString(),
                targetOutcome.TargetId,
                previousState,
                TargetCopyState.Verifying.ToString(),
                additionalContext: $"{{\"filePath\":\"{targetOutcome.FinalPath}\",\"expectedHash\":\"{expectedHash}\"}}",
                cancellationToken);

            // Persist VERIFYING state to database immediately so it's visible via API
            await _targetOutcomeRepository.UpdateAsync(targetOutcome, cancellationToken);

            _logger.LogDebug("Target state updated to VERIFYING and persisted: Job {JobId}, Target {TargetId}",
                targetOutcome.JobId, targetOutcome.TargetId);

            var result = await VerifyFileHashAsync(targetOutcome.FinalPath, expectedHash, cancellationToken);

            // Update target state based on verification result
            if (result.VerificationSucceeded && result.IsMatch)
            {
                targetOutcome.CompleteVerification();

                // Log successful verification to audit trail
                await _stateChangeLogger.LogTargetStateChangeAsync(
                    targetOutcome.JobId.Value.ToString(),
                    targetOutcome.TargetId,
                    TargetCopyState.Verifying.ToString(),
                    TargetCopyState.Verified.ToString(),
                    additionalContext: $"{{\"computedHash\":\"{result.ComputedHash}\",\"fileSizeBytes\":{result.FileSize},\"verificationDurationMs\":{result.VerificationDuration.TotalMilliseconds}}}",
                    cancellationToken);

                _logger.LogInformation("Target verification successful: Job {JobId}, Target {TargetId}",
                    targetOutcome.JobId, targetOutcome.TargetId);
            }
            else if (result.VerificationSucceeded && !result.IsMatch)
            {
                // Hash mismatch - this is a serious integrity issue
                targetOutcome.MarkAsPermanentlyFailed($"Hash mismatch: expected {expectedHash}, got {result.ComputedHash}");

                // Log hash mismatch to audit trail
                await _stateChangeLogger.LogTargetStateChangeAsync(
                    targetOutcome.JobId.Value.ToString(),
                    targetOutcome.TargetId,
                    TargetCopyState.Verifying.ToString(),
                    TargetCopyState.FailedPermanent.ToString(),
                    additionalContext: $"{{\"expectedHash\":\"{expectedHash}\",\"computedHash\":\"{result.ComputedHash}\",\"error\":\"Hash mismatch\"}}",
                    cancellationToken);

                _logger.LogError("Hash mismatch detected: Job {JobId}, Target {TargetId}, Expected {ExpectedHash}, Computed {ComputedHash}",
                    targetOutcome.JobId, targetOutcome.TargetId, expectedHash, result.ComputedHash);
            }
            else
            {
                // Verification failed due to I/O issues - may be retryable
                targetOutcome.MarkAsRetryableFailed($"Verification failed: {result.ErrorMessage}");

                // Log I/O error to audit trail
                await _stateChangeLogger.LogTargetStateChangeAsync(
                    targetOutcome.JobId.Value.ToString(),
                    targetOutcome.TargetId,
                    TargetCopyState.Verifying.ToString(),
                    TargetCopyState.FailedRetryable.ToString(),
                    additionalContext: $"{{\"error\":\"{result.ErrorMessage?.Replace("\"", "\\\"")}\"}}",
                    cancellationToken);

                _logger.LogWarning("Target verification failed with I/O error: Job {JobId}, Target {TargetId}, Error {Error}",
                    targetOutcome.JobId, targetOutcome.TargetId, result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            targetOutcome.MarkAsRetryableFailed($"Verification exception: {ex.Message}");
            _logger.LogError(ex, "Exception during target verification: Job {JobId}, Target {TargetId}",
                targetOutcome.JobId, targetOutcome.TargetId);
            throw;
        }
    }

    public async Task<Dictionary<string, VerificationResult>> VerifyMultipleFilesAsync(
        IEnumerable<string> filePaths, string expectedHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        if (string.IsNullOrWhiteSpace(expectedHash))
            throw new ArgumentException("Expected hash cannot be null or empty.", nameof(expectedHash));

        var filePathList = filePaths.ToList();
        var results = new Dictionary<string, VerificationResult>();

        _logger.LogDebug("Starting batch verification of {FileCount} files with expected hash {ExpectedHash}",
            filePathList.Count, expectedHash);

        // Use SemaphoreSlim to limit concurrent hash operations for memory control
        using var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);

        var tasks = filePathList.Select(async filePath =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await VerifyFileHashAsync(filePath, expectedHash, cancellationToken);
                lock (results)
                {
                    results[filePath] = result;
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var successfulCount = results.Values.Count(r => r.VerificationSucceeded && r.IsMatch);
        var failedCount = results.Values.Count(r => !r.VerificationSucceeded);
        var mismatchCount = results.Values.Count(r => r.VerificationSucceeded && !r.IsMatch);

        _logger.LogInformation("Batch verification completed: {TotalFiles} files, {SuccessfulCount} successful, " +
                              "{MismatchCount} hash mismatches, {FailedCount} I/O failures",
            filePathList.Count, successfulCount, mismatchCount, failedCount);

        return results;
    }
}