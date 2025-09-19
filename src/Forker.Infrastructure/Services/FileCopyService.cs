using System.Diagnostics;
using Forker.Domain;
using Forker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// File copying service with atomic operations and integrity verification.
/// Designed for large medical imaging files with temporary file staging to prevent partial visibility.
/// </summary>
public sealed class FileCopyService : IFileCopyService
{
    private readonly IHashingService _hashingService;
    private readonly ILogger<FileCopyService> _logger;

    private const int DefaultBufferSize = 1024 * 1024; // 1MB buffer for optimal performance
    private const string TempFileExtension = ".forker-tmp";

    public FileCopyService(IHashingService hashingService, ILogger<FileCopyService> logger)
    {
        _hashingService = hashingService ?? throw new ArgumentNullException(nameof(hashingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FileCopyResult> CopyFileAsync(
        string sourceFilePath,
        string targetDirectoryPath,
        TargetId targetId,
        string? expectedHash = null,
        IProgress<FileCopyProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
            throw new ArgumentException("Source file path cannot be null or empty.", nameof(sourceFilePath));

        if (string.IsNullOrWhiteSpace(targetDirectoryPath))
            throw new ArgumentException("Target directory path cannot be null or empty.", nameof(targetDirectoryPath));

        ArgumentNullException.ThrowIfNull(targetId);

        var stopwatch = Stopwatch.StartNew();
        var sourceFileName = Path.GetFileName(sourceFilePath);
        var targetFilePath = Path.Combine(targetDirectoryPath, sourceFileName);
        var tempFilePath = targetFilePath + TempFileExtension;

        _logger.LogInformation("Starting copy operation: {SourceFile} -> {TargetFile} (Target: {TargetId})",
            sourceFilePath, targetFilePath, targetId.Value);

        try
        {
            // Validate source file exists
            if (!File.Exists(sourceFilePath))
            {
                const string errorTemplate = "Source file not found: {SourceFilePath}";
                _logger.LogError(errorTemplate, sourceFilePath);
                var error = $"Source file not found: {sourceFilePath}";
                return FileCopyResult.CreateFailure(targetFilePath, error);
            }

            // Ensure target directory exists
            Directory.CreateDirectory(targetDirectoryPath);

            // Get source file info
            var sourceInfo = new FileInfo(sourceFilePath);
            var totalBytes = sourceInfo.Length;

            // Check if target file already exists and has same size/hash (avoid unnecessary copy)
            if (File.Exists(targetFilePath))
            {
                var targetInfo = new FileInfo(targetFilePath);
                if (targetInfo.Length == totalBytes)
                {
                    var existingHash = await _hashingService.CalculateHashAsync(targetFilePath, cancellationToken);
                    if (expectedHash != null && string.Equals(existingHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        stopwatch.Stop();
                        _logger.LogInformation("Target file already exists with correct hash, skipping copy: {TargetFile}",
                            targetFilePath);
                        return FileCopyResult.CreateSuccess(targetFilePath, existingHash, totalBytes, stopwatch.Elapsed);
                    }
                }
            }

            // Copy to temporary file first (atomic operation)
            var (copySuccess, hash, duration) = await CopyWithHashingAsync(
                sourceFilePath, tempFilePath, totalBytes, progressCallback, cancellationToken);

            if (!copySuccess)
            {
                const string error = "Copy operation failed during streaming";
                _logger.LogError(error);
                return FileCopyResult.CreateFailure(targetFilePath, error);
            }

            // Verify hash if expected hash was provided
            if (expectedHash != null && !string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                const string errorTemplate = "Hash verification failed for {TargetFile}: expected {ExpectedHash}, got {ActualHash}";
                _logger.LogError(errorTemplate, targetFilePath, expectedHash, hash);
                var error = $"Hash mismatch: expected {expectedHash}, got {hash}";

                // Clean up temp file
                try { File.Delete(tempFilePath); } catch { /* ignore cleanup errors */ }

                return FileCopyResult.CreateFailure(targetFilePath, error);
            }

            // Atomic move from temp to final location
            try
            {
                // Remove existing target file if it exists
                if (File.Exists(targetFilePath))
                {
                    File.Delete(targetFilePath);
                }

                File.Move(tempFilePath, targetFilePath);
            }
            catch (Exception ex)
            {
                var error = $"Failed to move temp file to final location: {ex.Message}";
                _logger.LogError(ex, "Atomic move failed for {TempFile} -> {TargetFile}", tempFilePath, targetFilePath);

                // Clean up temp file
                try { File.Delete(tempFilePath); } catch { /* ignore cleanup errors */ }

                return FileCopyResult.CreateFailure(targetFilePath, error, ex);
            }

            stopwatch.Stop();

            _logger.LogInformation("Copy completed successfully: {TargetFile} (Size: {Size} bytes, Duration: {Duration}, Hash: {Hash})",
                targetFilePath, totalBytes, stopwatch.Elapsed, hash);

            return FileCopyResult.CreateSuccess(targetFilePath, hash, totalBytes, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var error = $"Unexpected error during copy operation: {ex.Message}";
            _logger.LogError(ex, "Copy operation failed for {SourceFile} -> {TargetFile}", sourceFilePath, targetFilePath);

            // Clean up any temp files
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch { /* ignore cleanup errors */ }

            return FileCopyResult.CreateFailure(targetFilePath, error, ex);
        }
    }

    private async Task<(bool Success, string Hash, TimeSpan Duration)> CopyWithHashingAsync(
        string sourceFilePath,
        string targetFilePath,
        long totalBytes,
        IProgress<FileCopyProgress>? progressCallback,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var buffer = new byte[DefaultBufferSize];
        long bytesCopied = 0;
        var lastProgressReport = DateTime.UtcNow;

        try
        {
            using var sourceStream = new FileStream(
                sourceFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: DefaultBufferSize,
                useAsync: true);

            using var targetStream = new FileStream(
                targetFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: DefaultBufferSize,
                useAsync: true);

            // Calculate hash while copying
            var hash = await _hashingService.CalculateHashAsync(sourceStream, cancellationToken);

            // Reset source stream for copying
            sourceStream.Position = 0;

            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await targetStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                bytesCopied += bytesRead;

                // Report progress periodically (every second or so)
                var now = DateTime.UtcNow;
                if (progressCallback != null && (now - lastProgressReport).TotalSeconds >= 1.0)
                {
                    var elapsed = stopwatch.Elapsed;
                    var bytesPerSecond = elapsed.TotalSeconds > 0 ? (long)(bytesCopied / elapsed.TotalSeconds) : 0;
                    var estimatedTimeRemaining = bytesPerSecond > 0
                        ? TimeSpan.FromSeconds((totalBytes - bytesCopied) / (double)bytesPerSecond)
                        : (TimeSpan?)null;

                    var progress = new FileCopyProgress
                    {
                        BytesCopied = bytesCopied,
                        TotalBytes = totalBytes,
                        BytesPerSecond = bytesPerSecond,
                        EstimatedTimeRemaining = estimatedTimeRemaining
                    };

                    progressCallback.Report(progress);
                    lastProgressReport = now;
                }
            }

            // Ensure all data is written to disk
            await targetStream.FlushAsync(cancellationToken);

            stopwatch.Stop();
            return (true, hash, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during copy with hashing: {SourceFile} -> {TargetFile}", sourceFilePath, targetFilePath);
            stopwatch.Stop();
            return (false, string.Empty, stopwatch.Elapsed);
        }
    }
}