namespace Demo.Dashboard.Models;

/// <summary>
/// File count data for dashboard display.
/// </summary>
public record FileCountData(
    int Input,
    int DestinationA,
    int DestinationB,
    int Archive,
    int Quarantine
);

/// <summary>
/// System performance metrics for dashboard display.
/// </summary>
public record SystemMetrics(
    int MemoryMB,
    double CpuPercent,
    double ThroughputMBPerMin,
    DateTime LastUpdated
);

/// <summary>
/// Processing status information.
/// </summary>
public record ProcessingStatus(
    int QueueDepth,
    int ProcessedCount,
    string CurrentOperation,
    DateTime LastActivity
);

/// <summary>
/// Individual file processing event.
/// </summary>
public record FileEvent(
    string FileName,
    string Operation,
    string Status,
    DateTime Timestamp,
    string? Details = null
);

/// <summary>
/// Safety indicator status.
/// </summary>
public record SafetyIndicator(
    string Status, // "healthy", "warning", "error"
    string Text
);

/// <summary>
/// Overall safety status for the system.
/// </summary>
public record SafetyStatus(
    SafetyIndicator DataIntegrity,
    SafetyIndicator ServiceHealth,
    SafetyIndicator HashVerification
);

/// <summary>
/// Demo configuration settings.
/// </summary>
public class DemoConfiguration
{
    public string DemoDataPath { get; set; } = @"C:\ForkerDemo";
    public string ReservoirPath => Path.Combine(DemoDataPath, "Reservoir");
    public string InputPath => Path.Combine(DemoDataPath, "Input");
    public string DestinationAPath => Path.Combine(DemoDataPath, "DestinationA");
    public string DestinationBPath => Path.Combine(DemoDataPath, "DestinationB");
    public string ArchivePath => Path.Combine(DemoDataPath, "Archive");
    public string QuarantinePath => Path.Combine(DemoDataPath, "Quarantine");

    public int UpdateIntervalMs { get; set; } = 500;
    public bool EnableFileSystemWatcher { get; set; } = true;
    public bool EnableSystemMetrics { get; set; } = true;
}