using System.Text.Json.Serialization;

namespace AtomBox.Core.ValueObjects;

public readonly record struct TransferTaskId
{
    [JsonConstructor]
    public TransferTaskId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Transfer task id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    public static TransferTaskId New()
    {
        return new TransferTaskId(Guid.NewGuid());
    }

    public static TransferTaskId From(Guid value)
    {
        return new TransferTaskId(value);
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
