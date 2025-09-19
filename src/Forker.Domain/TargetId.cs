namespace Forker.Domain;

/// <summary>
/// Strongly-typed identifier for a replication target.
/// Immutable value object that ensures type safety.
/// </summary>
public sealed record TargetId
{
    /// <summary>
    /// The target identifier value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a TargetId from a string value.
    /// </summary>
    /// <param name="value">The target identifier (e.g., "TargetA", "TargetB")</param>
    /// <exception cref="ArgumentException">Thrown when value is null, empty, or whitespace</exception>
    public TargetId(string value)
    {
        Value = ValidateValue(value);
    }

    /// <summary>
    /// Creates a TargetId from a string value.
    /// </summary>
    public static TargetId From(string value) => new(value);

    /// <summary>
    /// Returns the string representation of the target identifier.
    /// </summary>
    public override string ToString() => Value;

    /// <summary>
    /// Implicit conversion to string for ease of use.
    /// </summary>
    public static implicit operator string(TargetId id) => id.Value;

    private static string ValidateValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("TargetId value cannot be null, empty, or whitespace.", nameof(value));
        }

        return value.Trim();
    }
}