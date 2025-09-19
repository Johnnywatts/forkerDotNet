using Forker.Domain.Services;
using Forker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Forker.Infrastructure.Services;

/// <summary>
/// FileSystemWatcher-based implementation of file discovery for medical imaging files.
/// Monitors source directory and raises events when stable files are discovered.
/// </summary>
public sealed class FileDiscoveryService : IFileDiscoveryService, IDisposable
{
    private readonly DirectoryConfiguration _directories;
    private readonly FileMonitoringConfiguration _monitoring;
    private readonly IFileStabilityChecker _stabilityChecker;
    private readonly ILogger<FileDiscoveryService> _logger;

    private FileSystemWatcher? _fileWatcher;
    private readonly ConcurrentDictionary<string, DateTime> _pendingFiles = new();
    private readonly Timer _stabilityCheckTimer;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private volatile bool _isRunning;

    public event EventHandler<FileDiscoveredEventArgs>? FileDiscovered;

    public FileDiscoveryService(
        IOptions<DirectoryConfiguration> directories,
        IOptions<FileMonitoringConfiguration> monitoring,
        IFileStabilityChecker stabilityChecker,
        ILogger<FileDiscoveryService> logger)
    {
        _directories = directories?.Value ?? throw new ArgumentNullException(nameof(directories));
        _monitoring = monitoring?.Value ?? throw new ArgumentNullException(nameof(monitoring));
        _stabilityChecker = stabilityChecker ?? throw new ArgumentNullException(nameof(stabilityChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Timer for periodic stability checks
        _stabilityCheckTimer = new Timer(ProcessPendingFiles, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        _logger.LogInformation("Starting file discovery service for directory: {SourceDirectory}",
            _directories.Source);

        // Ensure source directory exists
        if (!Directory.Exists(_directories.Source))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {_directories.Source}");
        }

        // Initialize FileSystemWatcher
        _fileWatcher = new FileSystemWatcher(_directories.Source)
        {
            IncludeSubdirectories = _monitoring.IncludeSubdirectories,
            EnableRaisingEvents = false
        };

        // Set up file filters - watch all files, we'll filter in the event handler
        _fileWatcher.Filter = "*.*";

        // Subscribe to events
        _fileWatcher.Created += OnFileCreated;
        _fileWatcher.Changed += OnFileChanged;
        _fileWatcher.Renamed += OnFileRenamed;

        // Start watching
        _fileWatcher.EnableRaisingEvents = true;
        _isRunning = true;

        // Start stability check timer
        _stabilityCheckTimer.Change(
            TimeSpan.FromSeconds(_monitoring.StabilityCheckInterval),
            TimeSpan.FromSeconds(_monitoring.StabilityCheckInterval));

        // Perform initial scan for existing files
        await ScanForExistingFilesAsync(cancellationToken);

        _logger.LogInformation("File discovery service started successfully");
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Stopping file discovery service");

        _isRunning = false;

        // Stop timer
        _stabilityCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);

        // Stop file watcher
        if (_fileWatcher != null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Created -= OnFileCreated;
            _fileWatcher.Changed -= OnFileChanged;
            _fileWatcher.Renamed -= OnFileRenamed;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }

        _logger.LogInformation("File discovery service stopped");
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<string>> ScanForExistingFilesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Scanning for existing files in {SourceDirectory}", _directories.Source);

        var discoveredFiles = new List<string>();

        try
        {
            if (!Directory.Exists(_directories.Source))
            {
                return discoveredFiles.AsReadOnly();
            }

            var searchOption = _monitoring.IncludeSubdirectories
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            // Get all files and filter them
            var allFiles = Directory.GetFiles(_directories.Source, "*.*", searchOption);

            foreach (var filePath in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ShouldProcessFile(filePath))
                {
                    // Check if file is immediately stable
                    var isStable = await _stabilityChecker.IsFileStableAsync(filePath, cancellationToken);
                    if (isStable)
                    {
                        discoveredFiles.Add(filePath);
                        await NotifyFileDiscovered(filePath);
                    }
                    else
                    {
                        // Add to pending files for stability monitoring
                        _pendingFiles.TryAdd(filePath, DateTime.UtcNow);
                        _logger.LogDebug("Added file to pending stability check: {FilePath}", filePath);
                    }
                }
            }

            _logger.LogInformation("Initial scan completed: {ImmediateFiles} files ready, {PendingFiles} files pending stability",
                discoveredFiles.Count, _pendingFiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initial file scan");
        }

        return discoveredFiles.AsReadOnly();
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        HandleFileEvent(e.FullPath, "created");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        HandleFileEvent(e.FullPath, "changed");
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        HandleFileEvent(e.FullPath, "renamed");
    }

    private void HandleFileEvent(string filePath, string eventType)
    {
        if (!_isRunning || !ShouldProcessFile(filePath))
        {
            return;
        }

        _logger.LogDebug("File {EventType}: {FilePath}", eventType, filePath);

        // Add or update the pending files list
        _pendingFiles.AddOrUpdate(filePath, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
    }

    private bool ShouldProcessFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(filePath);

        // Check excluded extensions
        if (_monitoring.ExcludeExtensions.Any(ext =>
            extension.Equals(ext, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Check file filters (patterns like *.svs, *.tiff)
        if (_monitoring.FileFilters.Length > 0)
        {
            var matchesFilter = _monitoring.FileFilters.Any(filter =>
                IsFileMatchingPattern(fileName, filter));

            if (!matchesFilter)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFileMatchingPattern(string fileName, string pattern)
    {
        // Convert glob pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";

        return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
    }

    private async void ProcessPendingFiles(object? state)
    {
        if (!_isRunning || _pendingFiles.IsEmpty)
        {
            return;
        }

        await _processingLock.WaitAsync();
        try
        {
            var filesToRemove = new List<string>();

            foreach (var kvp in _pendingFiles.ToArray())
            {
                var filePath = kvp.Key;
                var firstSeen = kvp.Value;

                try
                {
                    // Check if file still exists
                    if (!File.Exists(filePath))
                    {
                        filesToRemove.Add(filePath);
                        continue;
                    }

                    // Check if file has been pending too long
                    var pendingTime = DateTime.UtcNow - firstSeen;
                    var maxPendingTime = TimeSpan.FromSeconds(_monitoring.MaxStabilityChecks * _monitoring.StabilityCheckInterval);

                    if (pendingTime > maxPendingTime)
                    {
                        _logger.LogWarning("File {FilePath} has been pending for {PendingTime}, giving up",
                            filePath, pendingTime);
                        filesToRemove.Add(filePath);
                        continue;
                    }

                    // Check stability
                    var stabilityResult = await _stabilityChecker.WaitForStabilityAsync(filePath);

                    if (stabilityResult.IsStable)
                    {
                        filesToRemove.Add(filePath);
                        await NotifyFileDiscovered(filePath);
                    }
                    else if (stabilityResult.ChecksPerformed >= _monitoring.MaxStabilityChecks)
                    {
                        _logger.LogWarning("File {FilePath} failed stability check: {Reason}",
                            filePath, stabilityResult.UnstableReason);
                        filesToRemove.Add(filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing pending file {FilePath}", filePath);
                    filesToRemove.Add(filePath);
                }
            }

            // Remove processed files from pending list
            foreach (var filePath in filesToRemove)
            {
                _pendingFiles.TryRemove(filePath, out _);
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private Task NotifyFileDiscovered(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var eventArgs = new FileDiscoveredEventArgs(filePath, fileInfo.Length, DateTime.UtcNow);

            _logger.LogInformation("File discovered and stable: {FilePath} ({FileSize:N0} bytes)",
                filePath, fileInfo.Length);

            FileDiscovered?.Invoke(this, eventArgs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying file discovered for {FilePath}", filePath);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isRunning)
        {
            StopAsync().GetAwaiter().GetResult();
        }

        _stabilityCheckTimer.Dispose();
        _processingLock.Dispose();
        _fileWatcher?.Dispose();
    }
}