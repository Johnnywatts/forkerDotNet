namespace Forker.Domain.Exceptions;

/// <summary>
/// Exception thrown when a domain invariant is violated.
/// Represents a critical system integrity issue.
/// </summary>
public sealed class InvariantViolationException : DomainException
{
    /// <summary>
    /// The identifier of the violated invariant (e.g., "I1", "I2").
    /// </summary>
    public string InvariantId { get; }

    /// <summary>
    /// The entity identifier where the violation occurred.
    /// </summary>
    public string EntityId { get; }

    public InvariantViolationException(string invariantId, string entityId, string message)
        : base($"Invariant {invariantId} violated for entity {entityId}: {message}")
    {
        InvariantId = invariantId;
        EntityId = entityId;
    }

    public InvariantViolationException(string invariantId, string entityId, string message, Exception innerException)
        : base($"Invariant {invariantId} violated for entity {entityId}: {message}", innerException)
    {
        InvariantId = invariantId;
        EntityId = entityId;
    }
}