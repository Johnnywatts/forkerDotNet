namespace Forker.Domain.Exceptions;

/// <summary>
/// Exception thrown when an invalid state transition is attempted.
/// Enforces state machine invariants.
/// </summary>
public sealed class InvalidStateTransitionException : DomainException
{
    /// <summary>
    /// The current state from which transition was attempted.
    /// </summary>
    public string FromState { get; }

    /// <summary>
    /// The target state to which transition was attempted.
    /// </summary>
    public string ToState { get; }

    /// <summary>
    /// The entity identifier where the invalid transition occurred.
    /// </summary>
    public string EntityId { get; }

    public InvalidStateTransitionException(string fromState, string toState, string entityId)
        : base($"Invalid state transition from {fromState} to {toState} for entity {entityId}")
    {
        FromState = fromState;
        ToState = toState;
        EntityId = entityId;
    }

    public InvalidStateTransitionException(string fromState, string toState, string entityId, string reason)
        : base($"Invalid state transition from {fromState} to {toState} for entity {entityId}: {reason}")
    {
        FromState = fromState;
        ToState = toState;
        EntityId = entityId;
    }
}