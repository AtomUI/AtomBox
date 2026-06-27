using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;

namespace AtomBox.Providers.FileTransfer.Sftp;

public sealed class SftpStorageProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public SftpStorageProviderCreator(
        ProviderDescriptor descriptor,
        ProviderCredentialResolver credentialResolver)
    {
        Descriptor = descriptor;
        _credentialResolver = credentialResolver;
    }

    public ProviderDescriptor Descriptor { get; }

    public async Task<OperationResult<IStorageProvider>> CreateAsync(
        StorageAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (string.IsNullOrWhiteSpace(account.Endpoint))
        {
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("SFTP host is required."));
        }

        var portResult = GetPort(account.GetProviderConfigValue("port"));
        if (portResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(portResult.Error!);
        }

        var hostKeyOptionsResult = GetHostKeyOptions(account);
        if (hostKeyOptionsResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(hostKeyOptionsResult.Error!);
        }

        var timeoutResult = GetTimeoutSeconds(account.GetProviderConfigValue("timeoutSeconds"));
        if (timeoutResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(timeoutResult.Error!);
        }

        var materialResult = await _credentialResolver.AcquireMaterialAsync(account, cancellationToken).ConfigureAwait(false);
        if (materialResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(materialResult.Error!);
        }

        var materialLease = materialResult.GetValueOrThrow();
        try
        {
            var secretValidation = ValidateSecretMaterial(account, materialLease.Material);
            if (secretValidation.IsFailure)
            {
                await materialLease.DisposeAsync().ConfigureAwait(false);
                return OperationResult<IStorageProvider>.Failure(secretValidation.Error!);
            }

            var username = materialLease.Material.GetRequiredValue("username");
            var authMode = GetAuthMode(account, materialLease.Material);
            var hostKeyOptions = hostKeyOptionsResult.GetValueOrThrow();
            var client = authMode switch
            {
                SftpAuthMode.PrivateKey => new SshNetSftpClientAdapter(
                    account.Endpoint,
                    portResult.GetValueOrThrow(),
                    username,
                    materialLease.Material.GetRequiredValue("privateKey"),
                    materialLease.Material.TryGetValue("privateKeyPassphrase", out var passphrase) ? passphrase : null,
                    hostKeyOptions.Policy,
                    hostKeyOptions.Fingerprint,
                    timeoutResult.GetValueOrThrow()),
                _ => new SshNetSftpClientAdapter(
                    account.Endpoint,
                    portResult.GetValueOrThrow(),
                    username,
                    materialLease.Material.GetRequiredValue("password"),
                    hostKeyOptions.Policy,
                    hostKeyOptions.Fingerprint,
                    timeoutResult.GetValueOrThrow())
            };

            return OperationResult<IStorageProvider>.Success(
                new SftpStorageProvider(
                    client,
                    materialLease,
                    Descriptor.Capabilities,
                    account.GetProviderConfigValue("rootPath")));
        }
        catch (Exception exception)
        {
            await materialLease.DisposeAsync().ConfigureAwait(false);
            return OperationResult<IStorageProvider>.Failure(ProviderErrorMapper.FromException(exception));
        }
    }

    private static OperationResult<int> GetPort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OperationResult<int>.Success(22);
        }

        return int.TryParse(value, out var port) && port is > 0 and <= 65535
            ? OperationResult<int>.Success(port)
            : OperationResult<int>.Failure(StorageError.Validation("SFTP port must be a valid TCP port."));
    }

    private static OperationResult<SftpHostKeyOptions> GetHostKeyOptions(StorageAccount account)
    {
        var policy = account.GetProviderConfigValue("hostKeyPolicy");
        if (string.IsNullOrWhiteSpace(policy))
        {
            policy = "acceptAny";
        }

        if (!string.Equals(policy, "acceptAny", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(policy, "fingerprint", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<SftpHostKeyOptions>.Failure(
                StorageError.Validation("SFTP host key policy must be acceptAny or fingerprint."));
        }

        var fingerprint = account.GetProviderConfigValue("hostKeyFingerprint");
        if (string.Equals(policy, "fingerprint", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(fingerprint))
        {
            return OperationResult<SftpHostKeyOptions>.Failure(
                StorageError.Validation("SFTP host key fingerprint is required when hostKeyPolicy is fingerprint."));
        }

        return OperationResult<SftpHostKeyOptions>.Success(new SftpHostKeyOptions(policy, fingerprint));
    }

    private static OperationResult<int> GetTimeoutSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OperationResult<int>.Success(30);
        }

        return int.TryParse(value, out var timeoutSeconds) && timeoutSeconds is >= 1 and <= 600
            ? OperationResult<int>.Success(timeoutSeconds)
            : OperationResult<int>.Failure(StorageError.Validation("SFTP timeoutSeconds must be between 1 and 600."));
    }

    private static OperationResult ValidateSecretMaterial(StorageAccount account, CredentialSecretMaterial material)
    {
        if (!material.TryGetValue("username", out _))
        {
            return OperationResult.Failure(StorageError.Validation("SFTP credential requires username."));
        }

        return GetAuthMode(account, material) switch
        {
            SftpAuthMode.PrivateKey when !material.TryGetValue("privateKey", out _) =>
                OperationResult.Failure(StorageError.Validation("SFTP private key authentication requires privateKey.")),
            SftpAuthMode.Password when !material.TryGetValue("password", out _) =>
                OperationResult.Failure(StorageError.Validation("SFTP password authentication requires password.")),
            _ => OperationResult.Success()
        };
    }

    private static SftpAuthMode GetAuthMode(StorageAccount account, CredentialSecretMaterial material)
    {
        var value = account.GetProviderConfigValue("authMode");
        if (string.IsNullOrWhiteSpace(value) &&
            material.TryGetValue("authMode", out var materialValue))
        {
            value = materialValue;
        }

        if (string.Equals(value, "privateKey", StringComparison.OrdinalIgnoreCase))
        {
            return SftpAuthMode.PrivateKey;
        }

        return SftpAuthMode.Password;
    }

    private enum SftpAuthMode
    {
        Password,
        PrivateKey
    }

    private sealed record SftpHostKeyOptions(string Policy, string? Fingerprint);
}
