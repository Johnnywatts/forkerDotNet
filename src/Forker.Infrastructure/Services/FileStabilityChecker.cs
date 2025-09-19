using Forker.Domain.Services;
using Forker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Implementation of file stability checking for large medical imaging files.
/// Ensures files are not growing and not locked before processing.
/// </summary>
public sealed class FileStabilityChecker : IFileStabilityChecker
{
    private readonly FileMonitoringConfiguration _config;
    private readonly ILogger<FileStabilityChecker> _logger;

    public FileStabilityChecker(IOptions<FileMonitoringConfiguration> config, ILogger<FileStabilityChecker> logger)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> IsFileStableAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                return false;
            }

            // Check minimum file age
            var fileAge = DateTime.UtcNow - fileInfo.CreationTimeUtc;
            if (fileAge.TotalSeconds < _config.MinimumFileAge)
            {
                return false;
            }

            // Check if file is locked by trying to open it
            return await IsFileAccessibleAsync(filePath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking file stability for {FilePath}", filePath);
            return false;
        }
    }

    public async Task<FileStabilityResult> WaitForStabilityAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        var checksPerformed = 0;
        long lastSize = -1;
        var stableChecks = 0;
        const int requiredStableChecks = 2; // File must be stable for 2 consecutive checks

        while (checksPerformed < _config.MaxStabilityChecks && !cancellationToken.IsCancellationRequested)
        {
            checksPerformed++;

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    return FileStabilityResult.Unstable(0, checksPerformed, "File no longer exists");
                }

                var currentSize = fileInfo.Length;

                // Check if file size changed
                if (lastSize != -1 && currentSize != lastSize)
                {
                    _logger.LogDebug("File {FilePath} size changed from {LastSize} to {CurrentSize}",
                        filePath, lastSize, currentSize);
                    stableChecks = 0; // Reset stable check counter
                }
                else if (lastSize == currentSize)
                {
                    stableChecks++;
                }

                lastSize = currentSize;

                // Check if file is accessible (not locked)
                var isAccessible = await IsFileAccessibleAsync(filePath, cancellationToken);
                if (!isAccessible)
                {
                    _logger.LogDebug("File {FilePath} is locked or inaccessible", filePath);
                    stableChecks = 0; // Reset stable check counter
                }

                // File is considered stable if size hasn't changed and it's accessible for required checks
                if (stableChecks >= requiredStableChecks && isAccessible)
                {
                    _logger.LogDebug("File {FilePath} is stable after {Checks} checks, size: {Size} bytes",
                        filePath, checksPerformed, currentSize);
                    return FileStabilityResult.Stable(currentSize, checksPerformed);
                }

                // Wait before next check
                if (checksPerformed < _config.MaxStabilityChecks)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_config.StabilityCheckInterval), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during stability check {Check} for {FilePath}",
                    checksPerformed, filePath);

                if (checksPerformed < _config.MaxStabilityChecks)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_config.StabilityCheckInterval), cancellationToken);
                }
            }
        }

        return FileStabilityResult.Unstable(lastSize, checksPerformed,
            $"File did not stabilize after {checksPerformed} checks");
    }

    private static async Task<bool> IsFileAccessibleAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            // Try to open the file in read mode with shared read access
            // This will fail if the file is locked for writing
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Try to read a small amount to ensure the file is actually accessible
            var buffer = new byte[1024];
            await fileStream.ReadAsync(buffer, cancellationToken);

            return true;
        }
        catch (IOException)
        {
            // File is likely locked
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Permission denied
            return false;
        }
    }
}