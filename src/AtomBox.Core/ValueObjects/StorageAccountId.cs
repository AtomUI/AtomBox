using System.Text.Json.Serialization;

namespace AtomBox.Core.ValueObjects;

public readonly record struct StorageAccountId
{
    [JsonConstructor]
    public StorageAccountId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Storage account id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static StorageAccountId New()
    {
        return new StorageAccountId(Guid.NewGuid());
    }

    public static StorageAccountId From(Guid value)
    {
        return new StorageAccountId(value);
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
