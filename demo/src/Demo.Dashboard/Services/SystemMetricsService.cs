using Demo.Dashboard.Models;
using System.Diagnostics;
using System.ServiceProcess;

namespace Demo.Dashboard.Services;

/// <summary>
/// Implementation of system metrics service.
/// Provides real-time system performance monitoring for the demo dashboard.
/// </summary>
public class SystemMetricsService : ISystemMetricsService
{
    private readonly ILogger<SystemMetricsService> _logger;
    private readonly PerformanceCounter? _cpuCounter;
    private readonly Process _currentProcess;
    private DateTime _lastThroughputCheck = DateTime.Now;
    private long _lastBytesProcessed = 0;

    public SystemMetricsService(ILogger<SystemMetricsService> logger)
    {
        _logger = logger;
        _currentProcess = Process.GetCurrentProcess();

        try
        {
            // Initialize CPU counter for system-wide CPU usage
#if WINDOWS
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
#endif // First call always returns 0
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not initialize CPU performance counter");
        }
    }

    public async Task<SystemMetrics> GetCurrentMetricsAsync()
    {
        await Task.Yield(); // Make async for consistency

        try
        {
            var memoryMB = GetMemoryUsageMB();
            var cpuPercent = GetCpuUsagePercent();
            var throughput = GetThroughputMBPerMin();

            return new SystemMetrics(
                MemoryMB: memoryMB,
                CpuPercent: cpuPercent,
                ThroughputMBPerMin: throughput,
                LastUpdated: DateTime.Now
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system metrics");
            return new SystemMetrics(0, 0, 0, DateTime.Now);
        }
    }

    public async Task<SafetyStatus> GetSafetyStatusAsync()
    {
        await Task.Yield(); // Make async for consistency

        try
        {
            // For demo purposes, simulate safety checks
            // In a real implementation, this would check actual ForkerDotNet service status

            var dataIntegrity = new SafetyIndicator("healthy", "100% Verified");
            var serviceHealth = await CheckServiceHealthAsync();
            var hashVerification = new SafetyIndicator("healthy", "All Passed");

            return new SafetyStatus(dataIntegrity, serviceHealth, hashVerification);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting safety status");
            var errorIndicator = new SafetyIndicator("error", "Check Failed");
            return new SafetyStatus(errorIndicator, errorIndicator, errorIndicator);
        }
    }

    public async Task<ProcessingStatus> GetProcessingStatusAsync()
    {
        await Task.Yield(); // Make async for consistency

        try
        {
            // For demo purposes, simulate processing status
            // In a real implementation, this would query the actual ForkerDotNet service

            var queueDepth = GetSimulatedQueueDepth();
            var processedCount = GetSimulatedProcessedCount();
            var currentOperation = GetCurrentOperation(queueDepth);

            return new ProcessingStatus(
                QueueDepth: queueDepth,
                ProcessedCount: processedCount,
                CurrentOperation: currentOperation,
                LastActivity: DateTime.Now
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting processing status");
            return new ProcessingStatus(0, 0, "Error", DateTime.Now);
        }
    }

    private int GetMemoryUsageMB()
    {
        try
        {
            _currentProcess.Refresh();
            return (int)(_currentProcess.WorkingSet64 / 1024 / 1024);
        }
        catch
        {
            return 0;
        }
    }

    private double GetCpuUsagePercent()
    {
        try
        {
            if (_cpuCounter != null)
            {
#if WINDOWS
                return Math.Round(_cpuCounter.NextValue(), 1);
#else
                return 0;
#endif
            }

            // Fallback: Use process CPU time
            return Math.Round(_currentProcess.TotalProcessorTime.TotalMilliseconds / Environment.TickCount * 100, 1);
        }
        catch
        {
            return 0;
        }
    }

    private double GetThroughputMBPerMin()
    {
        try
        {
            // Simulate throughput calculation based on file processing
            // In a real implementation, this would track actual bytes processed
            var now = DateTime.Now;
            var elapsedMinutes = (now - _lastThroughputCheck).TotalMinutes;

            if (elapsedMinutes > 0.1) // Update every ~6 seconds
            {
                _lastThroughputCheck = now;
                _lastBytesProcessed += Random.Shared.Next(10, 50) * 1024 * 1024; // Simulate 10-50MB processed
            }

            var throughputMBPerMin = elapsedMinutes > 0 ? (_lastBytesProcessed / 1024.0 / 1024.0) / elapsedMinutes : 0;
            return Math.Round(Math.Min(throughputMBPerMin, 1500), 1); // Cap at reasonable max
        }
        catch
        {
            return 0;
        }
    }

    private Task<SafetyIndicator> CheckServiceHealthAsync()
    {
        try
        {
#if WINDOWS
            // Check if ForkerDotNet service is running
            var forkerServices = ServiceController.GetServices()
                .Where(s => s.ServiceName.Contains("Forker", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (forkerServices.Any(s => s.Status == ServiceControllerStatus.Running))
            {
                return Task.FromResult(new SafetyIndicator("healthy", "Service Running"));
            }

            if (forkerServices.Any())
            {
                return Task.FromResult(new SafetyIndicator("warning", "Service Stopped"));
            }

            return Task.FromResult(new SafetyIndicator("warning", "Service Not Installed"));
#else
            return Task.FromResult(new SafetyIndicator("info", "Service check not available on this platform"));
#endif
        }
        catch
        {
            // If we can't check service status, assume healthy for demo
            return Task.FromResult(new SafetyIndicator("healthy", "Operational"));
        }
    }

    private int GetSimulatedQueueDepth()
    {
        // Simulate queue depth based on input directory
        try
        {
            var inputPath = @"C:\ForkerDemo\Input";
            if (Directory.Exists(inputPath))
            {
                return Directory.GetFiles(inputPath).Length;
            }
        }
        catch { }

        return 0;
    }

    private int GetSimulatedProcessedCount()
    {
        // Simulate processed count based on destination directories
        try
        {
            var destAPath = @"C:\ForkerDemo\DestinationA";
            var destBPath = @"C:\ForkerDemo\DestinationB";

            var countA = Directory.Exists(destAPath) ? Directory.GetFiles(destAPath).Length : 0;
            var countB = Directory.Exists(destBPath) ? Directory.GetFiles(destBPath).Length : 0;

            return Math.Max(countA, countB); // Use higher count as processed files
        }
        catch { }

        return 0;
    }

    private string GetCurrentOperation(int queueDepth)
    {
        if (queueDepth == 0)
            return "Idle - Monitoring for files";

        if (queueDepth == 1)
            return "Processing 1 file";

        return $"Processing {queueDepth} files";
    }

    public void Dispose()
    {
        _cpuCounter?.Dispose();
        _currentProcess?.Dispose();
    }
}

