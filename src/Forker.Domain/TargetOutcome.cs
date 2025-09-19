using Forker.Domain.Exceptions;

namespace Forker.Domain;

/// <summary>
/// Domain entity representing the outcome of a copy operation to a specific target.
/// Enforces invariant I1 (Target VERIFYING requires prior COPIED) and guards state transitions.
/// </summary>
public sealed class TargetOutcome
{
    private TargetCopyState _copyState;
    private string? _hash;

    /// <summary>
    /// The job this target outcome belongs to.
    /// </summary>
    public FileJobId JobId { get; }

    /// <summary>
    /// The target identifier for this outcome.
    /// </summary>
    public TargetId TargetId { get; }

    /// <summary>
    /// Current state of the copy operation for this target.
    /// </summary>
    public TargetCopyState CopyState => _copyState;

    /// <summary>
    /// Number of attempts made for this target.
    /// </summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// Hash of the copied file at the target. Set when copy is complete.
    /// </summary>
    public string? Hash => _hash;

    /// <summary>
    /// Temporary file path during copy operation.
    /// </summary>
    public string? TempPath { get; private set; }

    /// <summary>
    /// Final file path at the target.
    /// </summary>
    public string? FinalPath { get; private set; }

    /// <summary>
    /// Last error message if operation failed.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Timestamp of the last state transition.
    /// </summary>
    public DateTime LastTransitionAt { get; private set; }

    /// <summary>
    /// Creates a new TargetOutcome in Pending state.
    /// </summary>
    public TargetOutcome(FileJobId jobId, TargetId targetId)
    {
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
        _copyState = TargetCopyState.Pending;
        Attempts = 0;
        LastTransitionAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Starts copy operation. Transitions to Copying state.
    /// </summary>
    public void StartCopy(string tempPath)
    {
        GuardTransition(TargetCopyState.Copying);

        TempPath = ValidatePath(tempPath, nameof(tempPath));
        _copyState = TargetCopyState.Copying;
        Attempts++;
        LastTransitionAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Completes copy operation. Transitions to Copied state and sets hash.
    /// </summary>
    public void CompleteCopy(string hash, string finalPath)
    {
        GuardTransition(TargetCopyState.Copied);

        _hash = ValidateHash(hash);
        FinalPath = ValidatePath(finalPath, nameof(finalPath));
        _copyState = TargetCopyState.Copied;
        LastTransitionAt = DateTime.UtcNow;
        LastError = null; // Clear any previous errors
    }

    /// <summary>
    /// Starts verification. Transitions to Verifying state.
    /// Enforces Invariant I1: Target VERIFYING requires prior COPIED.
    /// </summary>
    public void StartVerification()
    {
        // Invariant I1: Target VERIFYING requires prior COPIED
        if (_copyState != TargetCopyState.Copied)
        {
            throw new InvariantViolationException("I1", GetEntityIdentifier(),
                $"Target VERIFYING requires prior COPIED state. Current state: {_copyState}");
        }

        GuardTransition(TargetCopyState.Verifying);
        _copyState = TargetCopyState.Verifying;
        LastTransitionAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Completes verification successfully. Transitions to Verified state.
    /// </summary>
    public void CompleteVerification()
    {
        GuardTransition(TargetCopyState.Verified);
        _copyState = TargetCopyState.Verified;
        LastTransitionAt = DateTime.UtcNow;
        LastError = null; // Clear any previous errors
    }

    /// <summary>
    /// Marks operation as retryably failed. Can retry from this state.
    /// </summary>
    public void MarkAsRetryableFailed(string error)
    {
        GuardTransition(TargetCopyState.FailedRetryable);

        LastError = ValidateError(error);
        _copyState = TargetCopyState.FailedRetryable;
        LastTransitionAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks operation as permanently failed. Terminal state.
    /// </summary>
    public void MarkAsPermanentlyFailed(string error)
    {
        GuardTransition(TargetCopyState.FailedPermanent);

        LastError = ValidateError(error);
        _copyState = TargetCopyState.FailedPermanent;
        LastTransitionAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Retries operation from a failed state. Resets to Pending.
    /// </summary>
    public void Retry()
    {
        if (_copyState != TargetCopyState.FailedRetryable)
        {
            throw new InvalidStateTransitionException(_copyState.ToString(), TargetCopyState.Pending.ToString(),
                GetEntityIdentifier(), "Can only retry from FailedRetryable state");
        }

        _copyState = TargetCopyState.Pending;
        TempPath = null;
        LastTransitionAt = DateTime.UtcNow;
        // Note: Keep Attempts, Hash, FinalPath, and LastError for audit trail
    }

    /// <summary>
    /// Checks if this target can be considered successfully completed.
    /// </summary>
    public bool IsSuccessfullyCompleted => _copyState == TargetCopyState.Verified;

    /// <summary>
    /// Checks if this target has failed permanently.
    /// </summary>
    public bool HasFailedPermanently => _copyState == TargetCopyState.FailedPermanent;

    /// <summary>
    /// Checks if this target can be retried.
    /// </summary>
    public bool CanRetry => _copyState == TargetCopyState.FailedRetryable;

    /// <summary>
    /// Guards state transitions to ensure valid progression.
    /// </summary>
    private void GuardTransition(TargetCopyState targetState)
    {
        if (!IsValidTransition(_copyState, targetState))
        {
            throw new InvalidStateTransitionException(_copyState.ToString(), targetState.ToString(),
                GetEntityIdentifier());
        }
    }

    /// <summary>
    /// Validates if a state transition is allowed based on the state machine rules.
    /// </summary>
    private static bool IsValidTransition(TargetCopyState fromState, TargetCopyState toState)
    {
        return fromState switch
        {
            TargetCopyState.Pending => toState == TargetCopyState.Copying ||
                                     toState == TargetCopyState.FailedRetryable ||
                                     toState == TargetCopyState.FailedPermanent,

            TargetCopyState.Copying => toState == TargetCopyState.Copied ||
                                     toState == TargetCopyState.FailedRetryable ||
                                     toState == TargetCopyState.FailedPermanent,

            TargetCopyState.Copied => toState == TargetCopyState.Verifying ||
                                    toState == TargetCopyState.FailedRetryable ||
                                    toState == TargetCopyState.FailedPermanent,

            TargetCopyState.Verifying => toState == TargetCopyState.Verified ||
                                       toState == TargetCopyState.FailedRetryable ||
                                       toState == TargetCopyState.FailedPermanent,

            TargetCopyState.Verified => false, // Terminal state
            TargetCopyState.FailedRetryable => toState == TargetCopyState.Pending, // Can retry
            TargetCopyState.FailedPermanent => false, // Terminal state
            _ => false
        };
    }

    private string GetEntityIdentifier() => $"{JobId}:{TargetId}";

    private static string ValidatePath(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null, empty, or whitespace.", paramName);
        }
        return path.Trim();
    }

    private static string ValidateHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new ArgumentException("Hash cannot be null, empty, or whitespace.", nameof(hash));
        }
        return hash.Trim();
    }

    private static string ValidateError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("Error message cannot be null, empty, or whitespace.", nameof(error));
        }
        return error.Trim();
    }
}