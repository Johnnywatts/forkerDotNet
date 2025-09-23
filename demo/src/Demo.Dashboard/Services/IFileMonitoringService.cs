using Demo.Dashboard.Models;

namespace Demo.Dashboard.Services;

/// <summary>
/// Service for monitoring file system changes in demo directories.
/// </summary>
public interface IFileMonitoringService
{
    /// <summary>
    /// Event fired when file counts change in any directory.
    /// </summary>
    event EventHandler<FileCountData>? FileCountsChanged;

    /// <summary>
    /// Event fired when a file processing event occurs.
    /// </summary>
    event EventHandler<FileEvent>? FileEventOccurred;

    /// <summary>
    /// Gets current file counts across all directories.
    /// </summary>
    Task<FileCountData> GetCurrentFileCountsAsync();

    /// <summary>
    /// Starts monitoring the demo directories.
    /// </summary>
    Task StartMonitoringAsync();

    /// <summary>
    /// Stops monitoring the demo directories.
    /// </summary>
    Task StopMonitoringAsync();
}