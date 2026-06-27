using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Credentials;

public sealed class CredentialStore : ICredentialStore
{
    private readonly JsonFileStore<List<ProtectedCredentialPayload>> _store;
    private readonly CredentialProtectionService _protection;
    private readonly SemaphoreSlim _credentialGate = new(1, 1);
    private readonly Dictionary<CredentialRef, int> _activeLeases = new();
    private readonly object _leaseGate = new();

    public CredentialStore(AtomBoxStoragePaths paths, CredentialProtectionService protection)
    {
        _store = new JsonFileStore<List<ProtectedCredentialPayload>>(paths.CredentialIndexFile);
        _protection = protection;
    }

    public async Task<OperationResult<CredentialRef>> SaveAsync(
        CredentialSecretMaterial material,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(material);

        var payloadResult = ProtectMaterial(material);
        if (payloadResult.IsFailure)
        {
            return OperationResult<CredentialRef>.Failure(payloadResult.Error!);
        }

        await _credentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credentialsResult = await ReadCredentialsAsync(cancellationToken).ConfigureAwait(false);
            if (credentialsResult.IsFailure)
            {
                return OperationResult<CredentialRef>.Failure(credentialsResult.Error!);
            }

            var credentialRef = new CredentialRef($"cred-{Guid.NewGuid():N}");
            var credentials = credentialsResult.GetValueOrThrow();
            credentials.Add(new ProtectedCredentialPayload(
                credentialRef.Value,
                payloadResult.GetValueOrThrow(),
                PendingDelete: false,
                UpdatedAt: DateTimeOffset.UtcNow));

            var writeResult = await _store.WriteAsync(credentials, cancellationToken).ConfigureAwait(false);
            return writeResult.IsFailure
                ? OperationResult<CredentialRef>.Failure(writeResult.Error!)
                : OperationResult<CredentialRef>.Success(credentialRef);
        }
        finally
        {
            _credentialGate.Release();
        }
    }

    public async Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
        CredentialRef credentialRef,
        CancellationToken cancellationToken = default)
    {
        await _credentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credentialsResult = await ReadCredentialsAsync(cancellationToken).ConfigureAwait(false);
            if (credentialsResult.IsFailure)
            {
                return OperationResult<CredentialLease>.Failure(credentialsResult.Error!);
            }

            var exists = credentialsResult.GetValueOrThrow()
                .Any(item => item.CredentialRef == credentialRef.Value && !item.PendingDelete);
            if (!exists)
            {
                return OperationResult<CredentialLease>.Failure(StorageError.NotFound("Credential reference was not found."));
            }

            await InvokeBeforeCredentialLeaseCreatedAsync(credentialRef, cancellationToken).ConfigureAwait(false);
            return OperationResult<CredentialLease>.Success(CreateLease(credentialRef));
        }
        finally
        {
            _credentialGate.Release();
        }
    }

    public async Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
        CredentialRef credentialRef,
        CancellationToken cancellationToken = default)
    {
        await _credentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credentialsResult = await ReadCredentialsAsync(cancellationToken).ConfigureAwait(false);
            if (credentialsResult.IsFailure)
            {
                return OperationResult<CredentialMaterialLease>.Failure(credentialsResult.Error!);
            }

            var credential = credentialsResult.GetValueOrThrow()
                .FirstOrDefault(item => item.CredentialRef == credentialRef.Value && !item.PendingDelete);
            if (credential is null)
            {
                return OperationResult<CredentialMaterialLease>.Failure(StorageError.NotFound("Credential reference was not found."));
            }

            var materialResult = UnprotectMaterial(credential.ProtectedPayload);
            if (materialResult.IsFailure)
            {
                return OperationResult<CredentialMaterialLease>.Failure(materialResult.Error!);
            }

            await InvokeBeforeCredentialLeaseCreatedAsync(credentialRef, cancellationToken).ConfigureAwait(false);
            return OperationResult<CredentialMaterialLease>.Success(
                new CredentialMaterialLease(CreateLease(credentialRef), materialResult.GetValueOrThrow()));
        }
        finally
        {
            _credentialGate.Release();
        }
    }

    public async Task<OperationResult<bool>> ExistsAsync(
        CredentialRef credentialRef,
        CancellationToken cancellationToken = default)
    {
        await _credentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credentialsResult = await ReadCredentialsAsync(cancellationToken).ConfigureAwait(false);
            if (credentialsResult.IsFailure)
            {
                return OperationResult<bool>.Failure(credentialsResult.Error!);
            }

            var exists = credentialsResult.GetValueOrThrow()
                .Any(item => item.CredentialRef == credentialRef.Value && !item.PendingDelete);

            return OperationResult<bool>.Success(exists);
        }
        finally
        {
            _credentialGate.Release();
        }
    }

    public async Task<OperationResult> MarkPendingDeleteAsync(
        CredentialRef credentialRef,
        CancellationToken cancellationToken = default)
    {
        await _credentialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credentialsResult = await ReadCredentialsAsync(cancellationToken).ConfigureAwait(false);
            if (credentialsResult.IsFailure)
            {
                return OperationResult.Failure(credentialsResult.Error!);
            }

            var credentials = credentialsResult.GetValueOrThrow();
            var index = credentials.FindIndex(item => item.CredentialRef == credentialRef.Value);
            if (index < 0)
            {
                return OperationResult.Failure(StorageError.NotFound("Credential reference was not found."));
            }

            var credential = credentials[index];
            credentials[index] = credential with
            {
                PendingDelete = true,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            return await _store.WriteAsync(credentials, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _credentialGate.Release();
        }
    }

    internal bool HasActiveLease(CredentialRef credentialRef)
    {
        lock (_leaseGate)
        {
            return _activeLeases.TryGetValue(credentialRef, out var count) && count > 0;
        }
    }

    internal Func<CredentialRef, CancellationToken, Task>? BeforeCredentialLeaseCreatedAsync { get; set; }

    private Task<OperationResult<List<ProtectedCredentialPayload>>> ReadCredentialsAsync(CancellationToken cancellationToken)
    {
        return _store.ReadAsync([], cancellationToken);
    }

    private CredentialLease CreateLease(CredentialRef credentialRef)
    {
        lock (_leaseGate)
        {
            _activeLeases.TryGetValue(credentialRef, out var count);
            _activeLeases[credentialRef] = count + 1;
        }

        return new InfrastructureCredentialLease(credentialRef, Guid.NewGuid().ToString("N"), ReleaseLease);
    }

    private Task InvokeBeforeCredentialLeaseCreatedAsync(CredentialRef credentialRef, CancellationToken cancellationToken)
    {
        var hook = BeforeCredentialLeaseCreatedAsync;
        return hook is null ? Task.CompletedTask : hook(credentialRef, cancellationToken);
    }

    private OperationResult<string> ProtectMaterial(CredentialSecretMaterial material)
    {
        try
        {
            var json = JsonSerializer.Serialize(material.Values);
            var payload = Encoding.UTF8.GetBytes(json);
            return OperationResult<string>.Success(_protection.Protect(payload));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or NotSupportedException)
        {
            return OperationResult<string>.Failure(new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                "Unable to protect credential payload.",
                StorageErrorCategory.Infrastructure));
        }
    }

    private OperationResult<CredentialSecretMaterial> UnprotectMaterial(string protectedPayload)
    {
        try
        {
            var payload = _protection.Unprotect(protectedPayload);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(payload);
            if (values is null || values.Count == 0)
            {
                return OperationResult<CredentialSecretMaterial>.Failure(new StorageError(
                    StorageErrorCode.InfrastructureUnavailable,
                    "Credential payload format is invalid.",
                    StorageErrorCategory.Infrastructure));
            }

            return OperationResult<CredentialSecretMaterial>.Success(new CredentialSecretMaterial(values));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or JsonException or FormatException or InvalidOperationException)
        {
            return OperationResult<CredentialSecretMaterial>.Failure(new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                "Unable to read credential payload.",
                StorageErrorCategory.Infrastructure));
        }
    }

    private void ReleaseLease(CredentialRef credentialRef)
    {
        lock (_leaseGate)
        {
            if (!_activeLeases.TryGetValue(credentialRef, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                _activeLeases.Remove(credentialRef);
            }
            else
            {
                _activeLeases[credentialRef] = count - 1;
            }
        }
    }

    private sealed class InfrastructureCredentialLease : CredentialLease
    {
        private readonly Action<CredentialRef> _release;
        private bool _isDisposed;

        public InfrastructureCredentialLease(CredentialRef credentialRef, string leaseId, Action<CredentialRef> release)
            : base(credentialRef, leaseId)
        {
            _release = release;
        }

        public override ValueTask DisposeAsync()
        {
            if (_isDisposed)
            {
                return ValueTask.CompletedTask;
            }

            _isDisposed = true;
            _release(CredentialRef);
            return ValueTask.CompletedTask;
        }
    }
}
