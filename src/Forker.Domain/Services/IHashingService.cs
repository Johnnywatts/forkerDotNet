namespace Forker.Domain.Services;

/// <summary>
/// Provides streaming hash calculation services for file integrity verification.
/// Optimized for large medical imaging files (500MB-20GB) with minimal memory usage.
/// </summary>
public interface IHashingService
{
    /// <summary>
    /// Calculates SHA-256 hash of a file using streaming operations.
    /// Memory usage remains constant regardless of file size.
    /// </summary>
    /// <param name="filePath">Path to the file to hash</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>SHA-256 hash as hexadecimal string</returns>
    Task<string> CalculateHashAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates SHA-256 hash of a stream using streaming operations.
    /// Useful for hashing during copy operations.
    /// </summary>
    /// <param name="stream">Stream to hash (must be readable)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>SHA-256 hash as hexadecimal string</returns>
    Task<string> CalculateHashAsync(Stream stream, CancellationToken cancellationToken = default);
}