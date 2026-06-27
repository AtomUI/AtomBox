using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Application.Accounts;

public sealed record StorageAccountSummary(
    StorageAccountId Id,
    StorageProviderCategory ProviderCategory,
    StorageProviderId ProviderId,
    string DisplayName,
    string? Endpoint,
    string? Region,
    CredentialRef CredentialRef,
    IReadOnlyDictionary<string, string> ProviderConfig)
{
    public static StorageAccountSummary From(StorageAccount account)
    {
        return new StorageAccountSummary(
            account.Id,
            account.ProviderCategory,
            account.ProviderId,
            account.DisplayName,
            account.Endpoint,
            account.Region,
            account.CredentialRef,
            account.ProviderConfig);
    }
}

public sealed record TestConnectionResult(
    bool IsAvailable,
    StorageCapabilitySet Capabilities,
    StorageProviderId ProviderId,
    string TargetSummary,
    string? Endpoint,
    string? Region,
    string? BucketName,
    string? HomePath = null);
