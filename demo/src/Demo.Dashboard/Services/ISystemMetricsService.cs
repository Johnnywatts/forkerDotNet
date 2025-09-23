using Demo.Dashboard.Models;

namespace Demo.Dashboard.Services;

/// <summary>
/// Service for monitoring system performance metrics.
/// </summary>
public interface ISystemMetricsService
{
    /// <summary>
    /// Gets current system performance metrics.
    /// </summary>
    Task<SystemMetrics> GetCurrentMetricsAsync();

    /// <summary>
    /// Gets current safety status indicators.
    /// </summary>
    Task<SafetyStatus> GetSafetyStatusAsync();

    /// <summary>
    /// Gets current processing status.
    /// </summary>
    Task<ProcessingStatus> GetProcessingStatusAsync();
}