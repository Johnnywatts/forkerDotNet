namespace Forker.Domain;

/// <summary>
/// Strongly-typed identifier for a file job.
/// Immutable value object that ensures type safety.
/// </summary>
public sealed record FileJobId(Guid Value)
{
    /// <summary>
    /// Creates a new unique FileJobId.
    /// </summary>
    public static FileJobId New() => new(Guid.NewGuid());

    /// <summary>
    /// Creates a FileJobId from an existing Guid value.
    /// </summary>
    public static FileJobId From(Guid value) => new(value);

    /// <summary>
    /// Returns the string representation of the underlying Guid.
    /// </summary>
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Implicit conversion to Guid for database operations.
    /// </summary>
    public static implicit operator Guid(FileJobId id) => id.Value;
}