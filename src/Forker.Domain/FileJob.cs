using Forker.Domain.Exceptions;

namespace Forker.Domain;

/// <summary>
/// Core domain entity representing a file job with state machine behavior.
/// Enforces invariants I1, I2, I8, I10 and guards all state transitions.
/// </summary>
public sealed class FileJob
{
    private JobState _state;
    private string? _sourceHash;

    /// <summary>
    /// Unique identifier for this file job.
    /// </summary>
    public FileJobId Id { get; }

    /// <summary>
    /// Full path to the source file being processed.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Initial file size when job was created (bytes).
    /// </summary>
    public long InitialSize { get; }

    /// <summary>
    /// SHA-256 hash of the source file. Immutable once set (Invariant I10).
    /// </summary>
    public string? SourceHash => _sourceHash;

    /// <summary>
    /// Current state of the job. Transitions are guarded (Invariant I8).
    /// </summary>
    public JobState State => _state;

    /// <summary>
    /// List of required target identifiers for this job.
    /// </summary>
    public IReadOnlyList<TargetId> RequiredTargets { get; }

    /// <summary>
    /// Timestamp when the job was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Version token for optimistic concurrency control.
    /// </summary>
    public VersionToken VersionToken { get; private set; }

    /// <summary>
    /// Creates a new FileJob in Discovered state.
    /// </summary>
    public FileJob(FileJobId id, string sourcePath, long initialSize, IEnumerable<TargetId> requiredTargets)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        SourcePath = ValidateSourcePath(sourcePath);
        InitialSize = ValidateInitialSize(initialSize);
        RequiredTargets = requiredTargets?.ToList().AsReadOnly()
                         ?? throw new ArgumentNullException(nameof(requiredTargets));

        if (!RequiredTargets.Any())
            throw new ArgumentException("At least one target is required.", nameof(requiredTargets));

        _state = JobState.Discovered;
        CreatedAt = DateTime.UtcNow;
        VersionToken = VersionToken.Initial;
    }

    /// <summary>
    /// Sets the source hash. Can only be set once (Invariant I10).
    /// </summary>
    public void SetSourceHash(string hash)
    {
        if (_sourceHash is not null)
        {
            throw new InvariantViolationException("I10", Id.ToString(),
                $"SourceHash is immutable once set. Current: {_sourceHash}, Attempted: {hash}");
        }

        _sourceHash = ValidateHash(hash);
        VersionToken = VersionToken.Next();
    }

    /// <summary>
    /// Transitions job to Queued state. Guards transition validity (Invariant I8).
    /// </summary>
    public void MarkAsQueued()
    {
        GuardTransition(JobState.Queued);
        _state = JobState.Queued;
        VersionToken = VersionToken.Next();
    }

    /// <summary>
    /// Transitions job to InProgress state. Guards transition validity (Invariant I8).
    /// </summary>
    public void MarkAsInProgress()
    {
        GuardTransition(JobState.InProgress);
        _state = JobState.InProgress;
        VersionToken = VersionToken.Next();
    }

    /// <summary>
    /// Transitions job to Partial state. Guards transition validity (Invariant I8).
    /// </summary>
    public void MarkAsPartial()
    {
        GuardTransition(JobState.Partial);
        _state = JobState.Partial;
        VersionToken = VersionToken.Next();
    }

    /// <summary>
    /// Transitions job to Verified state. Guards transition validity (Invariant I8).
    /// </summary>
    public void MarkAsVerified()
    {
        GuardTransition(JobState.Verified);
        _state = JobState.Verified;
        VersionToken = VersionToken.Next();
    }

    /// <summary>
    /// Transitions job to Failed state. Guards transition validity (Invariant I8).
    /// </summary>
    public void MarkAsFailed()
    {
        GuardTransition(JobState.Failed);
        _state = JobState.Failed;
        VersionToken = VersionToken.Next();
    }

    /// <summary>
    /// Transitions job to Quarantined state. Guards transition validity (Invariant I8).
    /// </summary>
    public void MarkAsQuarantined()
    {
        GuardTransition(JobState.Quarantined);
        _state = JobState.Quarantined;
        VersionToken = VersionToken.Next();
    }

    /// <summary>
    /// Manual requeue from Quarantined state. Only allowed transition that goes "backwards".
    /// </summary>
    public void RequeueFromQuarantine()
    {
        if (_state != JobState.Quarantined)
        {
            throw new InvalidStateTransitionException(_state.ToString(), JobState.Queued.ToString(), Id.ToString(),
                "Can only requeue from Quarantined state");
        }

        _state = JobState.Queued;
        VersionToken = VersionToken.Next();
    }

    /// <summary>
    /// Guards state transitions to ensure monotonic progression (Invariant I8).
    /// </summary>
    private void GuardTransition(JobState targetState)
    {
        if (!IsValidTransition(_state, targetState))
        {
            throw new InvalidStateTransitionException(_state.ToString(), targetState.ToString(), Id.ToString());
        }
    }

    /// <summary>
    /// Validates if a state transition is allowed based on the state machine rules.
    /// </summary>
    private static bool IsValidTransition(JobState fromState, JobState toState)
    {
        return fromState switch
        {
            JobState.Discovered => toState == JobState.Queued || toState == JobState.Failed,
            JobState.Queued => toState == JobState.InProgress || toState == JobState.Failed,
            JobState.InProgress => toState == JobState.Partial || toState == JobState.Verified ||
                                 toState == JobState.Failed || toState == JobState.Quarantined,
            JobState.Partial => toState == JobState.Verified || toState == JobState.Failed ||
                              toState == JobState.Quarantined,
            JobState.Verified => false, // Terminal state
            JobState.Failed => false,   // Terminal state
            JobState.Quarantined => false, // Terminal state (except manual requeue)
            _ => false
        };
    }

    private static string ValidateSourcePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be null, empty, or whitespace.", nameof(sourcePath));
        }
        return sourcePath.Trim();
    }

    private static long ValidateInitialSize(long initialSize)
    {
        if (initialSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialSize), initialSize,
                "Initial size cannot be negative.");
        }
        return initialSize;
    }

    private static string ValidateHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new ArgumentException("Hash cannot be null, empty, or whitespace.", nameof(hash));
        }
        return hash.Trim();
    }
}