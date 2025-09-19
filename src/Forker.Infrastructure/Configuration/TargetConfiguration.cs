namespace Forker.Infrastructure.Configuration;

/// <summary>
/// Configuration for dual-target file copying operations.
/// Defines the required targets for 100% reliable replication.
/// </summary>
public sealed class TargetConfiguration
{
    /// <summary>
    /// Dictionary of target configurations keyed by target identifier.
    /// </summary>
    public Dictionary<string, TargetDefinition> Targets { get; set; } = new();

    /// <summary>
    /// Maximum number of concurrent copy operations per target.
    /// </summary>
    public int MaxConcurrentCopiesPerTarget { get; set; } = 2;

    /// <summary>
    /// Maximum number of retry attempts for failed copy operations.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts in milliseconds.
    /// </summary>
    public int RetryDelayMilliseconds { get; set; } = 5000;

    /// <summary>
    /// Buffer size for file copying operations in bytes.
    /// </summary>
    public int CopyBufferSize { get; set; } = 1024 * 1024; // 1MB

    /// <summary>
    /// Whether to perform parallel copying to all targets.
    /// </summary>
    public bool ParallelCopyEnabled { get; set; } = true;

    /// <summary>
    /// Directory for temporary files during copy operations.
    /// </summary>
    public string TempDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets all enabled target definitions.
    /// </summary>
    public IEnumerable<TargetDefinition> EnabledTargets =>
        Targets.Values.Where(t => t.Enabled);

    /// <summary>
    /// Gets target definition by identifier.
    /// </summary>
    public TargetDefinition? GetTarget(string targetId) =>
        Targets.TryGetValue(targetId, out var target) ? target : null;
}

/// <summary>
/// Definition for a single target destination.
/// </summary>
public sealed class TargetDefinition
{
    /// <summary>
    /// Target identifier (e.g., "TargetA", "TargetB").
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Path to the target directory.
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Whether this target is enabled for copying.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Description of this target.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Priority of this target (lower numbers = higher priority).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Maximum number of concurrent operations for this specific target.
    /// </summary>
    public int? MaxConcurrentOperations { get; set; }

    /// <summary>
    /// Whether to verify files after copying to this target.
    /// </summary>
    public bool VerifyAfterCopy { get; set; } = true;
}