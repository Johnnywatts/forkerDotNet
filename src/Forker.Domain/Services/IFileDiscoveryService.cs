namespace Forker.Domain.Services;

/// <summary>
/// Service responsible for discovering files that need to be processed.
/// Monitors source directories for files matching configured patterns.
/// </summary>
public interface IFileDiscoveryService
{
    /// <summary>
    /// Starts monitoring the configured source directory for new files.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop monitoring</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops monitoring the source directory.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for shutdown</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a stable file is discovered and ready for processing.
    /// </summary>
    event EventHandler<FileDiscoveredEventArgs> FileDiscovered;

    /// <summary>
    /// Manually scans the source directory for existing files.
    /// Used for initial startup or recovery scenarios.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of discovered files ready for processing</returns>
    Task<IReadOnlyList<string>> ScanForExistingFilesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Event arguments for file discovery events.
/// </summary>
public sealed class FileDiscoveredEventArgs : EventArgs
{
    /// <summary>
    /// Full path to the discovered file.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Size of the file in bytes.
    /// </summary>
    public long FileSize { get; }

    /// <summary>
    /// Timestamp when the file was first detected.
    /// </summary>
    public DateTime DiscoveredAt { get; }

    /// <summary>
    /// Creates a new FileDiscoveredEventArgs instance.
    /// </summary>
    public FileDiscoveredEventArgs(string filePath, long fileSize, DateTime discoveredAt)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        FileSize = fileSize;
        DiscoveredAt = discoveredAt;
    }
}