namespace AtomBox.Core.Providers;

public sealed record ProviderConfigFieldDescriptor
{
    public ProviderConfigFieldDescriptor(
        string key,
        string displayName,
        ProviderConfigFieldKind kind,
        bool isRequired)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Provider config field key cannot be empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Provider config field display name cannot be empty.", nameof(displayName));
        }

        Key = key.Trim();
        DisplayName = displayName.Trim();
        Kind = kind;
        IsRequired = isRequired;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public ProviderConfigFieldKind Kind { get; }

    public bool IsRequired { get; }
}
