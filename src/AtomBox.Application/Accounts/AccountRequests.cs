using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Application.Accounts;

public sealed record AddStorageAccountRequest(
    StorageProviderCategory ProviderCategory,
    StorageProviderId ProviderId,
    string DisplayName,
    string? Endpoint,
    string? Region,
    CredentialRef CredentialRef,
    IReadOnlyDictionary<string, string>? ProviderConfig = null)
{
    public CredentialSecretMaterial? CredentialMaterial { get; init; }

    public AddStorageAccountRequest(
        StorageProviderCategory providerCategory,
        StorageProviderId providerId,
        string displayName,
        string? endpoint,
        string? region,
        CredentialSecretMaterial credentialMaterial,
        IReadOnlyDictionary<string, string>? providerConfig = null)
        : this(providerCategory, providerId, displayName, endpoint, region, default(CredentialRef), providerConfig)
    {
        CredentialMaterial = credentialMaterial ?? throw new ArgumentNullException(nameof(credentialMaterial));
    }
}

public sealed record UpdateStorageAccountRequest(
    StorageAccountId Id,
    string DisplayName,
    string? Endpoint,
    string? Region,
    CredentialRef CredentialRef,
    IReadOnlyDictionary<string, string>? ProviderConfig = null)
{
    public CredentialSecretMaterial? CredentialMaterial { get; init; }

    public bool RetainExistingCredential { get; init; }

    public UpdateStorageAccountRequest(
        StorageAccountId id,
        string displayName,
        string? endpoint,
        string? region,
        CredentialSecretMaterial? credentialMaterial,
        IReadOnlyDictionary<string, string>? providerConfig = null)
        : this(id, displayName, endpoint, region, default(CredentialRef), providerConfig)
    {
        CredentialMaterial = credentialMaterial;
        RetainExistingCredential = credentialMaterial is null;
    }
}

public sealed record DeleteStorageAccountRequest(StorageAccountId Id);

public sealed record ListStorageAccountsRequest(StorageProviderCategory? ProviderCategory = null);

public sealed record TestConnectionRequest(StorageAccountId Id);

public sealed record TestConnectionDraftRequest(
    StorageProviderCategory ProviderCategory,
    StorageProviderId ProviderId,
    string DisplayName,
    string? Endpoint,
    string? Region,
    CredentialRef CredentialRef,
    IReadOnlyDictionary<string, string>? ProviderConfig = null)
{
    public CredentialSecretMaterial? CredentialMaterial { get; init; }

    public TestConnectionDraftRequest(
        StorageProviderCategory providerCategory,
        StorageProviderId providerId,
        string displayName,
        string? endpoint,
        string? region,
        CredentialSecretMaterial credentialMaterial,
        IReadOnlyDictionary<string, string>? providerConfig = null)
        : this(providerCategory, providerId, displayName, endpoint, region, default(CredentialRef), providerConfig)
    {
        CredentialMaterial = credentialMaterial ?? throw new ArgumentNullException(nameof(credentialMaterial));
    }
}
