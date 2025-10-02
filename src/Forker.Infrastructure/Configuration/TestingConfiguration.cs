namespace Forker.Infrastructure.Configuration;

/// <summary>
/// Configuration for testing and demonstration scenarios.
/// These settings allow injection of delays and faults for testing corruption detection,
/// crash recovery, and concurrent access scenarios.
/// </summary>
public sealed class TestingConfiguration
{
    /// <summary>
    /// Delay in seconds between copy completion and verification start.
    /// Used for corruption detection testing - allows time to manually corrupt files.
    /// Default: 0 (no delay, immediate verification)
    /// </summary>
    public int VerificationDelaySeconds { get; set; }

    /// <summary>
    /// Enable test mode features (delays, fault injection hooks, etc.)
    /// Default: false (production mode)
    /// </summary>
    public bool EnableTestMode { get; set; }

    /// <summary>
    /// If true, logs additional debug information for testing scenarios
    /// Default: false
    /// </summary>
    public bool VerboseTestLogging { get; set; }
}
