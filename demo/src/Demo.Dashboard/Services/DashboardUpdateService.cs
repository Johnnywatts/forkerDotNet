using Demo.Dashboard.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Demo.Dashboard.Services;

/// <summary>
/// Background service that coordinates dashboard updates.
/// Collects data from monitoring services and pushes updates to SignalR clients.
/// </summary>
public class DashboardUpdateService : BackgroundService
{
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly IFileMonitoringService _fileMonitoring;
    private readonly ISystemMetricsService _systemMetrics;
    private readonly ILogger<DashboardUpdateService> _logger;
    private readonly int _updateIntervalMs = 500; // Update every 500ms

    public DashboardUpdateService(
        IHubContext<DashboardHub> hubContext,
        IFileMonitoringService fileMonitoring,
        ISystemMetricsService systemMetrics,
        ILogger<DashboardUpdateService> logger)
    {
        _hubContext = hubContext;
        _fileMonitoring = fileMonitoring;
        _systemMetrics = systemMetrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Dashboard update service starting");

        try
        {
            // Start file monitoring
            await _fileMonitoring.StartMonitoringAsync();

            // Subscribe to file monitoring events
            _fileMonitoring.FileCountsChanged += OnFileCountsChanged;
            _fileMonitoring.FileEventOccurred += OnFileEventOccurred;

            // Main update loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await UpdateDashboard();
                    await Task.Delay(_updateIntervalMs, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during dashboard update cycle");
                    await Task.Delay(1000, stoppingToken); // Wait longer on error
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in dashboard update service");
        }
        finally
        {
            await _fileMonitoring.StopMonitoringAsync();
            _logger.LogInformation("Dashboard update service stopped");
        }
    }

    private async Task UpdateDashboard()
    {
        try
        {
            // Get current metrics
            var systemMetrics = await _systemMetrics.GetCurrentMetricsAsync();
            var safetyStatus = await _systemMetrics.GetSafetyStatusAsync();
            var processingStatus = await _systemMetrics.GetProcessingStatusAsync();

            // Send updates to all connected clients
            await Task.WhenAll(
                _hubContext.Clients.All.SendAsync("UpdateSystemMetrics", systemMetrics),
                _hubContext.Clients.All.SendAsync("UpdateSafetyStatus", safetyStatus),
                _hubContext.Clients.All.SendAsync("UpdateProcessingStatus", processingStatus)
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating dashboard metrics");
        }
    }

    private async void OnFileCountsChanged(object? sender, Models.FileCountData fileCounts)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("UpdateFileCounts", fileCounts);
            _logger.LogDebug("Sent file count update: {Counts}", fileCounts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending file count update");
        }
    }

    private async void OnFileEventOccurred(object? sender, Models.FileEvent fileEvent)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("FileEvent", fileEvent);
            _logger.LogDebug("Sent file event: {Event}", fileEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending file event");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Dashboard update service stopping");

        // Unsubscribe from events
        _fileMonitoring.FileCountsChanged -= OnFileCountsChanged;
        _fileMonitoring.FileEventOccurred -= OnFileEventOccurred;

        await base.StopAsync(cancellationToken);
    }
}