namespace Forker.Domain.Exceptions;

/// <summary>
/// Exception thrown when optimistic concurrency control detects a conflict.
/// Indicates that an entity was modified by another process since it was loaded.
/// </summary>
public sealed class ConcurrencyException : DomainException
{
    /// <summary>
    /// The entity identifier that experienced the concurrency conflict.
    /// </summary>
    public string EntityId { get; }

    /// <summary>
    /// The expected version token.
    /// </summary>
    public long ExpectedVersion { get; }

    /// <summary>
    /// The actual version token found in the database.
    /// </summary>
    public long ActualVersion { get; }

    /// <summary>
    /// Creates a new ConcurrencyException.
    /// </summary>
    /// <param name="entityId">The entity identifier</param>
    /// <param name="expectedVersion">The expected version token</param>
    /// <param name="actualVersion">The actual version token</param>
    public ConcurrencyException(string entityId, long expectedVersion, long actualVersion)
        : base($"Concurrency conflict for entity {entityId}. Expected version {expectedVersion}, but found {actualVersion}.")
    {
        EntityId = entityId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    /// <summary>
    /// Creates a new ConcurrencyException with a custom message.
    /// </summary>
    /// <param name="entityId">The entity identifier</param>
    /// <param name="expectedVersion">The expected version token</param>
    /// <param name="actualVersion">The actual version token</param>
    /// <param name="message">Custom error message</param>
    public ConcurrencyException(string entityId, long expectedVersion, long actualVersion, string message)
        : base(message)
    {
        EntityId = entityId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}