namespace Forker.Domain.Services;

/// <summary>
/// Orchestrates dual-target file copying operations for 100% reliable replication.
/// Manages parallel copying to both TargetA and TargetB with state tracking and verification.
/// </summary>
public interface ICopyOrchestrator
{
    /// <summary>
    /// Initiates dual-target copy operation for a discovered file.
    /// Copies file to both configured targets in parallel and manages state transitions.
    /// </summary>
    /// <param name="fileJobId">Job identifier</param>
    /// <param name="sourceFilePath">Source file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Orchestration result indicating success/failure of dual-target operation</returns>
    Task<CopyOrchestrationResult> ProcessFileAsync(
        FileJobId fileJobId,
        string sourceFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when copy progress is updated for any target.
    /// </summary>
    event EventHandler<CopyProgressEvent> CopyProgressChanged;

    /// <summary>
    /// Event fired when a target copy operation completes.
    /// </summary>
    event EventHandler<TargetCopyCompletedEvent> TargetCopyCompleted;
}

/// <summary>
/// Result of dual-target copy orchestration.
/// </summary>
public sealed record CopyOrchestrationResult
{
    /// <summary>
    /// Whether both targets were successfully copied and verified.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Job identifier.
    /// </summary>
    public required FileJobId JobId { get; init; }

    /// <summary>
    /// Results for each target copy operation.
    /// </summary>
    public required IReadOnlyDictionary<TargetId, FileCopyResult> TargetResults { get; init; }

    /// <summary>
    /// Source file hash calculated during first copy operation.
    /// </summary>
    public required string SourceHash { get; init; }

    /// <summary>
    /// Total duration for the entire dual-target operation.
    /// </summary>
    public required TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful orchestration result.
    /// </summary>
    public static CopyOrchestrationResult CreateSuccess(
        FileJobId jobId,
        IReadOnlyDictionary<TargetId, FileCopyResult> targetResults,
        string sourceHash,
        TimeSpan totalDuration)
        => new()
        {
            Success = true,
            JobId = jobId,
            TargetResults = targetResults,
            SourceHash = sourceHash,
            TotalDuration = totalDuration
        };

    /// <summary>
    /// Creates a failed orchestration result.
    /// </summary>
    public static CopyOrchestrationResult CreateFailure(
        FileJobId jobId,
        IReadOnlyDictionary<TargetId, FileCopyResult> targetResults,
        string errorMessage,
        TimeSpan totalDuration)
        => new()
        {
            Success = false,
            JobId = jobId,
            TargetResults = targetResults,
            SourceHash = string.Empty,
            TotalDuration = totalDuration,
            ErrorMessage = errorMessage
        };
}

/// <summary>
/// Event data for copy progress updates.
/// </summary>
public sealed record CopyProgressEvent
{
    /// <summary>
    /// Job identifier.
    /// </summary>
    public required FileJobId JobId { get; init; }

    /// <summary>
    /// Target identifier.
    /// </summary>
    public required TargetId TargetId { get; init; }

    /// <summary>
    /// Copy progress information.
    /// </summary>
    public required FileCopyProgress Progress { get; init; }
}

/// <summary>
/// Event data for target copy completion.
/// </summary>
public sealed record TargetCopyCompletedEvent
{
    /// <summary>
    /// Job identifier.
    /// </summary>
    public required FileJobId JobId { get; init; }

    /// <summary>
    /// Target identifier.
    /// </summary>
    public required TargetId TargetId { get; init; }

    /// <summary>
    /// Copy result.
    /// </summary>
    public required FileCopyResult Result { get; init; }
}