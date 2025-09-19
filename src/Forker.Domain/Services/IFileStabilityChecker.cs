namespace Forker.Domain.Services;

/// <summary>
/// Service responsible for determining if a file is stable and ready for processing.
/// Critical for large medical imaging files that may take time to copy.
/// </summary>
public interface IFileStabilityChecker
{
    /// <summary>
    /// Checks if a file is stable (not growing, not locked) and ready for processing.
    /// </summary>
    /// <param name="filePath">Path to the file to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the file is stable and ready for processing</returns>
    Task<bool> IsFileStableAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Monitors a file for stability over time.
    /// Returns when the file becomes stable or maximum checks are exceeded.
    /// </summary>
    /// <param name="filePath">Path to the file to monitor</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>FileStabilityResult indicating if the file is stable</returns>
    Task<FileStabilityResult> WaitForStabilityAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of file stability checking.
/// </summary>
public sealed class FileStabilityResult
{
    /// <summary>
    /// Whether the file is stable and ready for processing.
    /// </summary>
    public bool IsStable { get; }

    /// <summary>
    /// Current size of the file in bytes.
    /// </summary>
    public long FileSize { get; }

    /// <summary>
    /// Number of stability checks performed.
    /// </summary>
    public int ChecksPerformed { get; }

    /// <summary>
    /// Reason why the file is not stable (if applicable).
    /// </summary>
    public string? UnstableReason { get; }

    /// <summary>
    /// Creates a stable file result.
    /// </summary>
    public static FileStabilityResult Stable(long fileSize, int checksPerformed) =>
        new(true, fileSize, checksPerformed, null);

    /// <summary>
    /// Creates an unstable file result.
    /// </summary>
    public static FileStabilityResult Unstable(long fileSize, int checksPerformed, string reason) =>
        new(false, fileSize, checksPerformed, reason);

    private FileStabilityResult(bool isStable, long fileSize, int checksPerformed, string? unstableReason)
    {
        IsStable = isStable;
        FileSize = fileSize;
        ChecksPerformed = checksPerformed;
        UnstableReason = unstableReason;
    }
}