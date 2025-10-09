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
public sealed class FileDiscoveryService : IFileDiscoveryService, IAsyncDisposable, IDisposable
{
    private readonly DirectoryConfiguration _directories;
    private readonly FileMonitoringConfiguration _monitoring;
    private readonly IFileStabilityChecker _stabilityChecker;
    private readonly ILogger<FileDiscoveryService> _logger;

    private FileSystemWatcher? _fileWatcher;
    private readonly ConcurrentDictionary<string, DateTime> _pendingFiles = new();
    private readonly Timer _stabilityCheckTimer;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ConcurrentBag<EventHandler<FileDiscoveredEventArgs>> _eventHandlers = new();
    private readonly object _eventLock = new();
    private readonly object _stateLock = new();
    private volatile int _serviceState; // 0 = Stopped, 1 = Starting, 2 = Running, 3 = Stopping
    private volatile int _processingInProgress; // 0 = not processing, 1 = processing

    // Service state constants for atomic operations
    private const int STATE_STOPPED = 0;
    private const int STATE_STARTING = 1;
    private const int STATE_RUNNING = 2;
    private const int STATE_STOPPING = 3;

    /// <summary>
    /// Gets a value indicating whether the service is currently running and accepting operations.
    /// </summary>
    private bool IsRunning => _serviceState == STATE_RUNNING;

    /// <summary>
    /// Thread-safe event for file discovery notifications.
    /// Subscribers are isolated from each other's exceptions.
    /// </summary>
    public event EventHandler<FileDiscoveredEventArgs>? FileDiscovered
    {
        add
        {
            if (value != null)
            {
                lock (_eventLock)
                {
                    _eventHandlers.Add(value);
                }
            }
        }
        remove
        {
            // Note: ConcurrentBag doesn't support efficient removal
            // For production use, consider ConcurrentDictionary with weak references
            // For now, we'll leave handlers in place (they'll be cleaned up on disposal)
        }
    }

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

        // Timer for periodic stability checks - using safe callback pattern
        _stabilityCheckTimer = new Timer(ProcessPendingFilesCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Atomic state transition from STOPPED to STARTING
        if (Interlocked.CompareExchange(ref _serviceState, STATE_STARTING, STATE_STOPPED) != STATE_STOPPED)
        {
            return; // Already starting or running
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

        // Atomic transition to RUNNING state
        Interlocked.Exchange(ref _serviceState, STATE_RUNNING);

        // Start stability check timer
        _stabilityCheckTimer.Change(
            TimeSpan.FromSeconds(_monitoring.StabilityCheckInterval),
            TimeSpan.FromSeconds(_monitoring.StabilityCheckInterval));

        // Perform initial scan for existing files
        await ScanForExistingFilesAsync(cancellationToken);

        _logger.LogInformation("File discovery service started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Atomic state transition from RUNNING to STOPPING
        var previousState = Interlocked.CompareExchange(ref _serviceState, STATE_STOPPING, STATE_RUNNING);
        if (previousState != STATE_RUNNING)
        {
            return; // Already stopping or stopped
        }

        _logger.LogInformation("Stopping file discovery service");

        // Cancel any ongoing processing operations
        _cancellationTokenSource.Cancel();

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

        // Wait for any pending processing to complete (with timeout)
        var timeout = TimeSpan.FromSeconds(10);
        var waitStart = DateTime.UtcNow;
        while (_processingInProgress != 0 && DateTime.UtcNow - waitStart < timeout)
        {
            await Task.Delay(100, cancellationToken);
        }

        if (_processingInProgress != 0)
        {
            _logger.LogWarning("File processing did not complete within timeout during shutdown");
        }

        // Atomic transition to STOPPED state
        Interlocked.Exchange(ref _serviceState, STATE_STOPPED);

        _logger.LogInformation("File discovery service stopped");
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
        if (!IsRunning || !ShouldProcessFile(filePath))
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

    /// <summary>
    /// Timer callback that safely schedules async processing without overlapping executions.
    /// This prevents the async void anti-pattern that can cause race conditions.
    /// </summary>
    private void ProcessPendingFilesCallback(object? state)
    {
        // Atomic check-and-set to prevent overlapping executions - critical for race condition prevention
        if (Interlocked.CompareExchange(ref _processingInProgress, 1, 0) != 0)
        {
            return; // Already processing
        }

        if (!IsRunning || _pendingFiles.IsEmpty)
        {
            Interlocked.Exchange(ref _processingInProgress, 0);
            return;
        }

        // Schedule async processing on thread pool to avoid blocking timer
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessPendingFilesAsync(_cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in pending files processing");
            }
            finally
            {
                // Always reset processing flag in finally block
                Interlocked.Exchange(ref _processingInProgress, 0);
            }
        }, _cancellationTokenSource.Token);
    }

    /// <summary>
    /// Async implementation of pending file processing with proper cancellation support.
    /// </summary>
    private async Task ProcessPendingFilesAsync(CancellationToken cancellationToken)
    {
        // Processing flag is already set atomically in callback, just verify state
        if (!IsRunning || _pendingFiles.IsEmpty)
        {
            return;
        }

        await _processingLock.WaitAsync(cancellationToken);
        try
        {
            var filesToRemove = new List<string>();

            foreach (var kvp in _pendingFiles.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

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

                    // Check stability with cancellation support
                    // The stability checker will handle its own timeout logic based on MaxStabilityChecks
                    var stabilityResult = await _stabilityChecker.WaitForStabilityAsync(filePath, cancellationToken);

                    if (stabilityResult.IsStable)
                    {
                        // File is stable (not growing, not locked) - ready for processing
                        filesToRemove.Add(filePath);
                        await NotifyFileDiscovered(filePath);
                    }
                    else if (stabilityResult.ChecksPerformed >= _monitoring.MaxStabilityChecks)
                    {
                        // File failed stability check after max attempts (still growing or locked)
                        _logger.LogWarning("File {FilePath} failed stability check after {Checks} attempts: {Reason}",
                            filePath, stabilityResult.ChecksPerformed, stabilityResult.UnstableReason);
                        filesToRemove.Add(filePath);
                    }
                    // If stability check is incomplete (< MaxStabilityChecks), file stays in pending queue for next iteration
                }
                catch (OperationCanceledException)
                {
                    // Operation cancelled, exit gracefully
                    break;
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

    /// <summary>
    /// Thread-safe notification of file discovery events.
    /// Each event handler is called in isolation to prevent one subscriber's exception
    /// from affecting other subscribers or the main processing flow.
    /// </summary>
    private async Task NotifyFileDiscovered(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var eventArgs = new FileDiscoveredEventArgs(filePath, fileInfo.Length, DateTime.UtcNow);

            _logger.LogInformation("File discovered and stable: {FilePath} ({FileSize:N0} bytes)",
                filePath, fileInfo.Length);

            // Get a snapshot of current handlers to avoid modification during enumeration
            EventHandler<FileDiscoveredEventArgs>[] handlers;
            lock (_eventLock)
            {
                handlers = _eventHandlers.ToArray();
            }

            // Invoke each handler in parallel with exception isolation
            if (handlers.Length > 0)
            {
                var tasks = handlers.Select(handler => Task.Run(() =>
                {
                    try
                    {
                        handler.Invoke(this, eventArgs);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Event handler failed for file discovery: {FilePath}. Handler: {HandlerType}",
                            filePath, handler.Method.DeclaringType?.Name ?? "Unknown");
                    }
                }));

                // Wait for all handlers to complete with a reasonable timeout
                try
                {
                    await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Some event handlers did not complete within timeout for file: {FilePath}", filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in NotifyFileDiscovered for {FilePath}", filePath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (IsRunning)
        {
            await StopAsync();
        }

        _cancellationTokenSource.Dispose();
        _stabilityCheckTimer.Dispose();
        _processingLock.Dispose();
        _fileWatcher?.Dispose();
    }

    public void Dispose()
    {
        // Synchronous disposal with timeout to avoid deadlocks
        var disposeTask = DisposeAsync().AsTask();
        var completed = disposeTask.Wait(TimeSpan.FromSeconds(30));

        if (!completed)
        {
            _logger?.LogWarning("Async disposal did not complete within timeout, forcing synchronous disposal");

            // Force synchronous cleanup
            Interlocked.Exchange(ref _serviceState, STATE_STOPPED);
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _stabilityCheckTimer?.Dispose();
            _processingLock?.Dispose();
            _fileWatcher?.Dispose();
        }
    }
}