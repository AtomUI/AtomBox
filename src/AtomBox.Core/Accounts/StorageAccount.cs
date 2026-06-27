using System.Text.Json.Serialization;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Accounts;

public sealed record StorageAccount
{
    public StorageAccount(
        StorageAccountId Id,
        StorageProviderCategory ProviderCategory,
        StorageProviderId ProviderId,
        string DisplayName,
        string? Endpoint,
        string? Region,
        CredentialRef CredentialRef,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt)
        : this(
            Id,
            ProviderCategory,
            ProviderId,
            DisplayName,
            Endpoint,
            Region,
            CredentialRef,
            CreatedAt,
            UpdatedAt,
            null)
    {
    }

    [JsonConstructor]
    public StorageAccount(
        StorageAccountId Id,
        StorageProviderCategory ProviderCategory,
        StorageProviderId ProviderId,
        string DisplayName,
        string? Endpoint,
        string? Region,
        CredentialRef CredentialRef,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyDictionary<string, string>? ProviderConfig)
    {
        if (Id.IsEmpty)
        {
            throw new ArgumentException("Storage account id cannot be empty.", nameof(Id));
        }

        if (ProviderId.IsEmpty)
        {
            throw new ArgumentException("Storage provider id cannot be empty.", nameof(ProviderId));
        }

        if (CredentialRef.IsEmpty)
        {
            throw new ArgumentException("Credential reference cannot be empty.", nameof(CredentialRef));
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new ArgumentException("Storage account display name cannot be empty.", nameof(DisplayName));
        }

        if (UpdatedAt < CreatedAt)
        {
            throw new ArgumentException("Updated time cannot be earlier than created time.", nameof(UpdatedAt));
        }

        this.Id = Id;
        this.ProviderCategory = ProviderCategory;
        this.ProviderId = ProviderId;
        this.DisplayName = DisplayName.Trim();
        this.Endpoint = Endpoint?.Trim();
        this.Region = Region?.Trim();
        this.CredentialRef = CredentialRef;
        this.CreatedAt = CreatedAt;
        this.UpdatedAt = UpdatedAt;
        this.ProviderConfig = NormalizeProviderConfig(ProviderConfig);
    }

    public StorageAccountId Id { get; }

    public StorageProviderCategory ProviderCategory { get; }

    public StorageProviderId ProviderId { get; }

    public string DisplayName { get; }

    public string? Endpoint { get; }

    public string? Region { get; }

    public CredentialRef CredentialRef { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public IReadOnlyDictionary<string, string> ProviderConfig { get; }

    public StorageAccount UpdateConfiguration(
        string displayName,
        string? endpoint,
        string? region,
        CredentialRef credentialRef,
        DateTimeOffset updatedAt)
    {
        return UpdateConfiguration(displayName, endpoint, region, credentialRef, updatedAt, ProviderConfig);
    }

    public StorageAccount UpdateConfiguration(
        string displayName,
        string? endpoint,
        string? region,
        CredentialRef credentialRef,
        DateTimeOffset updatedAt,
        IReadOnlyDictionary<string, string>? providerConfig)
    {
        return new StorageAccount(
            Id,
            ProviderCategory,
            ProviderId,
            displayName,
            endpoint,
            region,
            credentialRef,
            CreatedAt,
            updatedAt,
            providerConfig);
    }

    public string? GetProviderConfigValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return key.Trim() switch
        {
            "endpoint" => Endpoint,
            "region" => Region,
            var normalizedKey when ProviderConfig.TryGetValue(normalizedKey, out var value) => value,
            _ => null
        };
    }

    private static IReadOnlyDictionary<string, string> NormalizeProviderConfig(
        IReadOnlyDictionary<string, string>? providerConfig)
    {
        if (providerConfig is null || providerConfig.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in providerConfig)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalizedKey = key.Trim();
            if (normalizedKey is "endpoint" or "region")
            {
                continue;
            }

            normalized[normalizedKey] = value.Trim();
        }

        return normalized;
    }
}
