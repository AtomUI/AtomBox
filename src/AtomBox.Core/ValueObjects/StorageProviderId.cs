using System.Text.Json.Serialization;

namespace AtomBox.Core.ValueObjects;

public readonly record struct StorageProviderId
{
    [JsonConstructor]
    public StorageProviderId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Storage provider id cannot be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static StorageProviderId From(string value)
    {
        return new StorageProviderId(value);
    }

    public override string ToString()
    {
        return Value ?? string.Empty;
    }
}
