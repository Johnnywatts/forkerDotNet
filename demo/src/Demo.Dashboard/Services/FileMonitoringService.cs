using Demo.Dashboard.Models;

namespace Demo.Dashboard.Services;

/// <summary>
/// Implementation of file monitoring service using FileSystemWatcher.
/// Monitors demo directories and reports file count changes and events.
/// </summary>
public class FileMonitoringService : IFileMonitoringService, IDisposable
{
    private readonly DemoConfiguration _config;
    private readonly ILogger<FileMonitoringService> _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, int> _lastFileCounts = new();
    private bool _isMonitoring = false;

    public event EventHandler<FileCountData>? FileCountsChanged;
    public event EventHandler<FileEvent>? FileEventOccurred;

    public FileMonitoringService(ILogger<FileMonitoringService> logger)
    {
        _logger = logger;
        _config = new DemoConfiguration(); // TODO: Inject from configuration
    }

    public async Task<FileCountData> GetCurrentFileCountsAsync()
    {
        await Task.Yield(); // Make async for consistency

        return new FileCountData(
            Input: GetFileCount(_config.InputPath),
            DestinationA: GetFileCount(_config.DestinationAPath),
            DestinationB: GetFileCount(_config.DestinationBPath),
            Archive: GetFileCount(_config.ArchivePath),
            Quarantine: GetFileCount(_config.QuarantinePath)
        );
    }

    public async Task StartMonitoringAsync()
    {
        if (_isMonitoring)
        {
            _logger.LogWarning("File monitoring is already active");
            return;
        }

        try
        {
            // Ensure directories exist
            CreateDirectoriesIfNeeded();

            // Set up watchers for each directory
            var directories = new Dictionary<string, string>
            {
                ["Input"] = _config.InputPath,
                ["DestinationA"] = _config.DestinationAPath,
                ["DestinationB"] = _config.DestinationBPath,
                ["Archive"] = _config.ArchivePath,
                ["Quarantine"] = _config.QuarantinePath
            };

            foreach (var (name, path) in directories)
            {
                var watcher = CreateFileWatcher(path, name);
                _watchers.Add(watcher);
                watcher.EnableRaisingEvents = true;
                _logger.LogInformation("Started monitoring {DirectoryName} at {Path}", name, path);
            }

            _isMonitoring = true;
            _logger.LogInformation("File monitoring service started successfully");

            // Send initial file counts
            var initialCounts = await GetCurrentFileCountsAsync();
            FileCountsChanged?.Invoke(this, initialCounts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start file monitoring service");
            throw;
        }
    }

    public async Task StopMonitoringAsync()
    {
        if (!_isMonitoring)
        {
            return;
        }

        try
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            _watchers.Clear();
            _isMonitoring = false;

            _logger.LogInformation("File monitoring service stopped");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping file monitoring service");
        }
    }

    private FileSystemWatcher CreateFileWatcher(string path, string directoryName)
    {
        var watcher = new FileSystemWatcher(path)
        {
            Filter = "*.*",
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime
        };

        watcher.Created += (sender, e) => OnFileEvent(e.FullPath, "Created", directoryName);
        watcher.Deleted += (sender, e) => OnFileEvent(e.FullPath, "Deleted", directoryName);
        watcher.Renamed += (sender, e) => OnFileEvent(e.FullPath, "Renamed", directoryName);

        return watcher;
    }

    private void OnFileEvent(string filePath, string operation, string directoryName)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            var status = DetermineFileStatus(operation, directoryName);

            var fileEvent = new FileEvent(
                FileName: fileName,
                Operation: $"{directoryName} - {operation}",
                Status: status,
                Timestamp: DateTime.Now,
                Details: $"File {operation.ToLower()} in {directoryName}"
            );

            FileEventOccurred?.Invoke(this, fileEvent);

            // Check if file counts changed and notify
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(100); // Brief delay to allow file operations to complete
                    var currentCounts = await GetCurrentFileCountsAsync();
                    FileCountsChanged?.Invoke(this, currentCounts);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating file counts after file event");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file event for {FilePath}", filePath);
        }
    }

    private static string DetermineFileStatus(string operation, string directoryName)
    {
        return operation switch
        {
            "Created" when directoryName == "Input" => "discovered",
            "Created" when directoryName == "DestinationA" || directoryName == "DestinationB" => "copied",
            "Created" when directoryName == "Archive" => "archived",
            "Created" when directoryName == "Quarantine" => "quarantined",
            "Deleted" => "removed",
            "Renamed" => "renamed",
            _ => "processed"
        };
    }

    private int GetFileCount(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return 0;
            }

            return Directory.GetFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly).Length;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error counting files in {DirectoryPath}", directoryPath);
            return 0;
        }
    }

    private void CreateDirectoriesIfNeeded()
    {
        var directories = new[]
        {
            _config.ReservoirPath,
            _config.InputPath,
            _config.DestinationAPath,
            _config.DestinationBPath,
            _config.ArchivePath,
            _config.QuarantinePath
        };

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Created directory: {Directory}", directory);
            }
        }
    }

    public void Dispose()
    {
        StopMonitoringAsync().GetAwaiter().GetResult();
    }
}