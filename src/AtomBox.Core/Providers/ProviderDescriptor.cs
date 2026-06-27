using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Providers;

public sealed record ProviderDescriptor
{
    public ProviderDescriptor(
        StorageProviderId id,
        StorageProviderCategory category,
        string displayName,
        string description,
        StorageCapabilitySet capabilities,
        IReadOnlyList<ProviderConfigFieldDescriptor> configFields)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("Provider id cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Provider display name cannot be empty.", nameof(displayName));
        }

        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(configFields);

        Id = id;
        Category = category;
        DisplayName = displayName.Trim();
        Description = description.Trim();
        Capabilities = capabilities;
        ConfigFields = configFields;
    }

    public StorageProviderId Id { get; }

    public StorageProviderCategory Category { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public StorageCapabilitySet Capabilities { get; }

    public IReadOnlyList<ProviderConfigFieldDescriptor> ConfigFields { get; }
}
