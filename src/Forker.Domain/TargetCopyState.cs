namespace Forker.Domain;

/// <summary>
/// Represents the state of a copy operation to a specific target.
/// Each target has independent state progression.
/// </summary>
public enum TargetCopyState
{
    /// <summary>
    /// Target copy has not started yet.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Copy operation is currently in progress for this target.
    /// </summary>
    Copying = 1,

    /// <summary>
    /// Copy operation completed successfully, file written to target.
    /// </summary>
    Copied = 2,

    /// <summary>
    /// Verification (hash check) is in progress for this target.
    /// </summary>
    Verifying = 3,

    /// <summary>
    /// Target has been successfully verified (hash matches source).
    /// Terminal state for successful target operations.
    /// </summary>
    Verified = 4,

    /// <summary>
    /// Target operation failed but can be retried.
    /// </summary>
    FailedRetryable = 5,

    /// <summary>
    /// Target operation failed permanently and cannot be retried.
    /// Terminal state for failed target operations.
    /// </summary>
    FailedPermanent = 6
}