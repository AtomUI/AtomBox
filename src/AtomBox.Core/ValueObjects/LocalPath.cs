using System.Text.Json.Serialization;

namespace AtomBox.Core.ValueObjects;

public readonly record struct LocalPath
{
    [JsonConstructor]
    public LocalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Local path cannot be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static LocalPath From(string value)
    {
        return new LocalPath(value);
    }

    public string GetFileName()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var normalized = Value.Replace('\\', '/');
        var index = normalized.LastIndexOf('/');
        return index < 0 ? normalized : normalized[(index + 1)..];
    }

    public override string ToString()
    {
        return Value ?? string.Empty;
    }
}
