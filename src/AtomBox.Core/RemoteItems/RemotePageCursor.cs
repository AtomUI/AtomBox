namespace AtomBox.Core.RemoteItems;

public readonly record struct RemotePageCursor
{
    private const string OffsetPrefix = "offset:";

    public RemotePageCursor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Remote page cursor cannot be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static RemotePageCursor FromOffset(int offset)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Remote page cursor offset cannot be negative.");
        }

        return new RemotePageCursor(OffsetPrefix + offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public bool TryGetOffset(out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(Value) ||
            !Value.StartsWith(OffsetPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(
            Value[OffsetPrefix.Length..],
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out offset) &&
            offset >= 0;
    }

    public override string ToString()
    {
        return Value ?? string.Empty;
    }
}
