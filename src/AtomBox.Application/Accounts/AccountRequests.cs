using AtomBox.Core.Accounts;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Application.Accounts;

public sealed record AddStorageAccountRequest(
    StorageProviderCategory ProviderCategory,
    StorageProviderId ProviderId,
    string DisplayName,
    string? Endpoint,
    string? Region,
    CredentialRef CredentialRef,
    IReadOnlyDictionary<string, string>? ProviderConfig = null);

public sealed record UpdateStorageAccountRequest(
    StorageAccountId Id,
    string DisplayName,
    string? Endpoint,
    string? Region,
    CredentialRef CredentialRef,
    IReadOnlyDictionary<string, string>? ProviderConfig = null);

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
    IReadOnlyDictionary<string, string>? ProviderConfig = null);
