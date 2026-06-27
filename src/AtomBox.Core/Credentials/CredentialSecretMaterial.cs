namespace AtomBox.Core.Credentials;

public sealed class CredentialSecretMaterial
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public CredentialSecretMaterial(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            throw new ArgumentException("Credential secret material cannot be empty.", nameof(values));
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Credential secret key cannot be empty.", nameof(values));
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Credential secret value cannot be empty.", nameof(values));
            }

            normalized[key.Trim()] = value;
        }

        _values = normalized;
    }

    public IReadOnlyDictionary<string, string> Values => _values;

    public bool TryGetValue(string key, out string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = null;
            return false;
        }

        return _values.TryGetValue(key.Trim(), out value);
    }

    public string GetRequiredValue(string key)
    {
        if (TryGetValue(key, out var value) && value is not null)
        {
            return value;
        }

        throw new InvalidOperationException($"Credential secret material is missing required key '{key}'.");
    }

    public override string ToString()
    {
        return "[credential secret material redacted]";
    }
}
