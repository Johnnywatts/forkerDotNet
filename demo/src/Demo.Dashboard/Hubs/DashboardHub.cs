using Microsoft.AspNetCore.SignalR;

namespace Demo.Dashboard.Hubs;

/// <summary>
/// SignalR hub for real-time dashboard updates.
/// Provides live updates for file processing, system metrics, and safety indicators.
/// </summary>
public class DashboardHub : Hub
{
    private readonly ILogger<DashboardHub> _logger;

    public DashboardHub(ILogger<DashboardHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Dashboard client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Dashboard client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Sends file count updates to all connected clients.
    /// </summary>
    public async Task SendFileCountUpdate(object counts)
    {
        await Clients.All.SendAsync("UpdateFileCounts", counts);
    }

    /// <summary>
    /// Sends system metrics updates to all connected clients.
    /// </summary>
    public async Task SendSystemMetricsUpdate(object metrics)
    {
        await Clients.All.SendAsync("UpdateSystemMetrics", metrics);
    }

    /// <summary>
    /// Sends processing status updates to all connected clients.
    /// </summary>
    public async Task SendProcessingStatusUpdate(object status)
    {
        await Clients.All.SendAsync("UpdateProcessingStatus", status);
    }

    /// <summary>
    /// Sends individual file events to all connected clients.
    /// </summary>
    public async Task SendFileEvent(object fileEvent)
    {
        await Clients.All.SendAsync("FileEvent", fileEvent);
    }

    /// <summary>
    /// Sends safety status updates to all connected clients.
    /// </summary>
    public async Task SendSafetyStatusUpdate(object safety)
    {
        await Clients.All.SendAsync("UpdateSafetyStatus", safety);
    }
}