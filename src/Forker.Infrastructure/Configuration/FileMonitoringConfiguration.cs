namespace Forker.Infrastructure.Configuration;

/// <summary>
/// Configuration for file monitoring and discovery.
/// Maps to the "monitoring" section in settings.json.
/// </summary>
public sealed class FileMonitoringConfiguration
{
    /// <summary>
    /// Whether to include subdirectories in monitoring.
    /// </summary>
    public bool IncludeSubdirectories { get; init; }

    /// <summary>
    /// File patterns to monitor (e.g., "*.svs", "*.tiff").
    /// </summary>
    public string[] FileFilters { get; init; } = [];

    /// <summary>
    /// File extensions to exclude from monitoring.
    /// </summary>
    public string[] ExcludeExtensions { get; init; } = [];

    /// <summary>
    /// Minimum age in seconds before a file is considered for processing.
    /// </summary>
    public int MinimumFileAge { get; init; } = 5;

    /// <summary>
    /// Interval in seconds between stability checks.
    /// </summary>
    public int StabilityCheckInterval { get; init; } = 2;

    /// <summary>
    /// Maximum number of stability checks before giving up.
    /// </summary>
    public int MaxStabilityChecks { get; init; } = 10;
}

/// <summary>
/// Configuration for directory paths used by the service.
/// Maps to the "directories" section in settings.json.
/// </summary>
public sealed class DirectoryConfiguration
{
    /// <summary>
    /// Source directory to monitor for files.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Target A directory for file copies.
    /// </summary>
    public string TargetA { get; init; } = string.Empty;

    /// <summary>
    /// Target B directory for file copies.
    /// </summary>
    public string TargetB { get; init; } = string.Empty;

    /// <summary>
    /// Directory for files that encountered errors.
    /// </summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>
    /// Temporary directory for processing operations.
    /// </summary>
    public string Processing { get; init; } = string.Empty;
}