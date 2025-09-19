namespace Forker.Domain.Services;

/// <summary>
/// Provides file copying services with integrity verification and atomic operations.
/// Designed for large medical imaging files with dual-target replication requirements.
/// </summary>
public interface IFileCopyService
{
    /// <summary>
    /// Copies a file to a target destination with hash verification and atomic completion.
    /// Uses temporary files during copy to prevent partial files from being visible.
    /// </summary>
    /// <param name="sourceFilePath">Source file path</param>
    /// <param name="targetDirectoryPath">Target directory path</param>
    /// <param name="targetId">Target identifier (e.g., "TargetA", "TargetB")</param>
    /// <param name="expectedHash">Expected SHA-256 hash for verification (optional)</param>
    /// <param name="progressCallback">Optional callback for copy progress reporting</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Copy result with final file path and calculated hash</returns>
    Task<FileCopyResult> CopyFileAsync(
        string sourceFilePath,
        string targetDirectoryPath,
        TargetId targetId,
        string? expectedHash = null,
        IProgress<FileCopyProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a file copy operation with verification details.
/// </summary>
public sealed record FileCopyResult
{
    /// <summary>
    /// Whether the copy operation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Final path of the copied file.
    /// </summary>
    public required string TargetFilePath { get; init; }

    /// <summary>
    /// SHA-256 hash of the copied file.
    /// </summary>
    public required string Hash { get; init; }

    /// <summary>
    /// Size of the copied file in bytes.
    /// </summary>
    public required long FileSize { get; init; }

    /// <summary>
    /// Time taken for the copy operation.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Exception that caused the failure, if any.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Creates a successful copy result.
    /// </summary>
    public static FileCopyResult CreateSuccess(string targetFilePath, string hash, long fileSize, TimeSpan duration)
        => new()
        {
            Success = true,
            TargetFilePath = targetFilePath,
            Hash = hash,
            FileSize = fileSize,
            Duration = duration
        };

    /// <summary>
    /// Creates a failed copy result.
    /// </summary>
    public static FileCopyResult CreateFailure(string targetFilePath, string errorMessage, Exception? exception = null)
        => new()
        {
            Success = false,
            TargetFilePath = targetFilePath,
            Hash = string.Empty,
            FileSize = 0,
            Duration = TimeSpan.Zero,
            ErrorMessage = errorMessage,
            Exception = exception
        };
}

/// <summary>
/// Progress information for file copy operations.
/// </summary>
public sealed record FileCopyProgress
{
    /// <summary>
    /// Number of bytes copied so far.
    /// </summary>
    public required long BytesCopied { get; init; }

    /// <summary>
    /// Total number of bytes to copy.
    /// </summary>
    public required long TotalBytes { get; init; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public double ProgressPercentage => TotalBytes > 0 ? (double)BytesCopied / TotalBytes * 100 : 0;

    /// <summary>
    /// Current copy speed in bytes per second.
    /// </summary>
    public long BytesPerSecond { get; init; }

    /// <summary>
    /// Estimated time remaining for the copy operation.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; init; }
}