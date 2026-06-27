using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Credentials;

public interface ICredentialStore
{
    Task<OperationResult<CredentialRef>> SaveAsync(
        CredentialSecretMaterial material,
        CancellationToken cancellationToken = default);

    Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
        CredentialRef credentialRef,
        CancellationToken cancellationToken = default);

    Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
        CredentialRef credentialRef,
        CancellationToken cancellationToken = default);

    Task<OperationResult<bool>> ExistsAsync(
        CredentialRef credentialRef,
        CancellationToken cancellationToken = default);

    Task<OperationResult> MarkPendingDeleteAsync(
        CredentialRef credentialRef,
        CancellationToken cancellationToken = default);
}
