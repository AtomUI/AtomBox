using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;

namespace AtomBox.Providers.Common;

public sealed class ProviderCredentialResolver
{
    private readonly ICredentialStore _credentials;

    public ProviderCredentialResolver(ICredentialStore credentials)
    {
        _credentials = credentials;
    }

    public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.CredentialRef.IsEmpty)
        {
            return Task.FromResult(OperationResult<CredentialMaterialLease>.Failure(
                StorageError.Validation("Credential reference is required.")));
        }

        return _credentials.AcquireMaterialAsync(account.CredentialRef, cancellationToken);
    }
}
