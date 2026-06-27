using System.Text.Json.Serialization;

namespace AtomBox.Core.ValueObjects;

public readonly record struct CredentialRef
{
    [JsonConstructor]
    public CredentialRef(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Credential reference cannot be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public static CredentialRef From(string value)
    {
        return new CredentialRef(value);
    }

    public override string ToString()
    {
        return Value ?? string.Empty;
    }
}
