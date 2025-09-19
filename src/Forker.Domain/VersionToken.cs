namespace Forker.Domain;

/// <summary>
/// Version token for optimistic concurrency control.
/// Immutable value object that tracks entity versions.
/// </summary>
public sealed record VersionToken
{
    /// <summary>
    /// The version number value.
    /// </summary>
    public long Value { get; }

    /// <summary>
    /// Initial version token for new entities.
    /// </summary>
    public static readonly VersionToken Initial = new(1);

    /// <summary>
    /// Creates a VersionToken from a long value.
    /// </summary>
    /// <param name="value">The version number (must be positive)</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when value is not positive</exception>
    public VersionToken(long value)
    {
        Value = ValidateValue(value);
    }

    /// <summary>
    /// Creates a VersionToken from a long value.
    /// </summary>
    public static VersionToken From(long value) => new(value);

    /// <summary>
    /// Creates the next version token (incremented by 1).
    /// </summary>
    public VersionToken Next() => new(Value + 1);

    /// <summary>
    /// Returns the string representation of the version number.
    /// </summary>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Implicit conversion to long for database operations.
    /// </summary>
    public static implicit operator long(VersionToken token) => token.Value;

    private static long ValidateValue(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "VersionToken value must be positive.");
        }

        return value;
    }
}