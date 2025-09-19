using System.Security.Cryptography;
using Forker.Domain;
using Forker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Forker.Infrastructure.Services;

/// <summary>
/// Streaming SHA-256 hashing service optimized for large medical imaging files.
/// Uses incremental hashing to maintain constant memory usage regardless of file size.
/// </summary>
public sealed class HashingService : IHashingService
{
    private readonly ILogger<HashingService> _logger;

    private const int DefaultBufferSize = 1024 * 1024; // 1MB buffer for optimal performance

    public HashingService(ILogger<HashingService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> CalculateHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        _logger.LogDebug("Starting SHA-256 hash calculation for file: {FilePath}", filePath);

        try
        {
            using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: DefaultBufferSize,
                useAsync: true);

            var result = await CalculateHashAsync(fileStream, cancellationToken);

            _logger.LogDebug("SHA-256 hash calculation completed for {FilePath}: {Hash}", filePath, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating hash for file: {FilePath}", filePath);
            throw;
        }
    }

    public async Task<string> CalculateHashAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));

        _logger.LogDebug("Starting SHA-256 hash calculation for stream");

        try
        {
            // Use IncrementalHash for streaming operations to maintain constant memory usage
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[DefaultBufferSize];

            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, bytesRead);
                totalBytesRead += bytesRead;

                // Log progress for very large files (every 1GB)
                if (totalBytesRead % (1024L * 1024 * 1024) == 0)
                {
                    _logger.LogDebug("Hashed {TotalGB} GB so far...", totalBytesRead / (1024L * 1024 * 1024));
                }
            }

            var hashBytes = hash.GetHashAndReset();
            var result = Convert.ToHexString(hashBytes).ToLowerInvariant();

            _logger.LogDebug("SHA-256 hash calculation completed for stream. Total bytes: {TotalBytes}, Hash: {Hash}",
                totalBytesRead, result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating hash for stream");
            throw;
        }
    }
}