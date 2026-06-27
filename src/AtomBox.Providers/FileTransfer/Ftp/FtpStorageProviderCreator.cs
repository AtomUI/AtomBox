using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Providers.Common;

namespace AtomBox.Providers.FileTransfer.Ftp;

public sealed class FtpStorageProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public FtpStorageProviderCreator(
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
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("FTP host is required."));
        }

        var portResult = GetPort(account.GetProviderConfigValue("port"));
        if (portResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(portResult.Error!);
        }

        var connectionOptionsResult = GetConnectionOptions(account);
        if (connectionOptionsResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(connectionOptionsResult.Error!);
        }

        var connectionOptions = connectionOptionsResult.GetValueOrThrow();
        if (IsAnonymous(account))
        {
            var anonymousLease = new CredentialMaterialLease(
                new AnonymousCredentialLease(account.CredentialRef),
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["authMode"] = "anonymous"
                }));
            return OperationResult<IStorageProvider>.Success(
                new FtpStorageProvider(
                    new FluentFtpClientAdapter(
                        account.Endpoint,
                        portResult.GetValueOrThrow(),
                        connectionOptions.PassiveMode,
                        connectionOptions.TimeoutSeconds),
                    anonymousLease,
                    Descriptor.Capabilities,
                    account.GetProviderConfigValue("rootPath")));
        }

        var materialResult = await _credentialResolver.AcquireMaterialAsync(account, cancellationToken).ConfigureAwait(false);
        if (materialResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(materialResult.Error!);
        }

        var materialLease = materialResult.GetValueOrThrow();
        try
        {
            var secretValidation = ValidateSecretMaterial(materialLease.Material);
            if (secretValidation.IsFailure)
            {
                await materialLease.DisposeAsync().ConfigureAwait(false);
                return OperationResult<IStorageProvider>.Failure(secretValidation.Error!);
            }

            var username = materialLease.Material.GetRequiredValue("username");
            var password = materialLease.Material.GetRequiredValue("password");
            var client = new FluentFtpClientAdapter(
                account.Endpoint,
                portResult.GetValueOrThrow(),
                username,
                password,
                connectionOptions.PassiveMode,
                connectionOptions.TimeoutSeconds);

            return OperationResult<IStorageProvider>.Success(
                new FtpStorageProvider(
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
            return OperationResult<int>.Success(21);
        }

        return int.TryParse(value, out var port) && port is > 0 and <= 65535
            ? OperationResult<int>.Success(port)
            : OperationResult<int>.Failure(StorageError.Validation("FTP port must be a valid TCP port."));
    }

    private static OperationResult<FtpConnectionOptions> GetConnectionOptions(StorageAccount account)
    {
        var transferMode = account.GetProviderConfigValue("transferMode");
        var passiveMode = true;
        if (!string.IsNullOrWhiteSpace(transferMode))
        {
            if (string.Equals(transferMode, "passive", StringComparison.OrdinalIgnoreCase))
            {
                passiveMode = true;
            }
            else if (string.Equals(transferMode, "active", StringComparison.OrdinalIgnoreCase))
            {
                passiveMode = false;
            }
            else
            {
                return OperationResult<FtpConnectionOptions>.Failure(
                    StorageError.Validation("FTP transferMode must be passive or active."));
            }
        }

        var timeoutSeconds = 30;
        var timeoutValue = account.GetProviderConfigValue("timeoutSeconds");
        if (!string.IsNullOrWhiteSpace(timeoutValue) &&
            (!int.TryParse(timeoutValue, out timeoutSeconds) || timeoutSeconds is < 1 or > 600))
        {
            return OperationResult<FtpConnectionOptions>.Failure(
                StorageError.Validation("FTP timeoutSeconds must be between 1 and 600."));
        }

        return OperationResult<FtpConnectionOptions>.Success(new FtpConnectionOptions(passiveMode, timeoutSeconds));
    }

    private static OperationResult ValidateSecretMaterial(CredentialSecretMaterial material)
    {
        if (material.TryGetValue("authMode", out var authMode) &&
            string.Equals(authMode, "anonymous", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Success();
        }

        if (!material.TryGetValue("username", out _))
        {
            return OperationResult.Failure(StorageError.Validation("FTP credential requires username."));
        }

        if (!material.TryGetValue("password", out _))
        {
            return OperationResult.Failure(StorageError.Validation("FTP credential requires password."));
        }

        return OperationResult.Success();
    }

    private static bool IsAnonymous(StorageAccount account)
    {
        return string.Equals(
            account.GetProviderConfigValue("authMode"),
            "anonymous",
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AnonymousCredentialLease : CredentialLease
    {
        public AnonymousCredentialLease(AtomBox.Core.ValueObjects.CredentialRef credentialRef)
            : base(credentialRef, "ftp-anonymous")
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed record FtpConnectionOptions(bool PassiveMode, int TimeoutSeconds);
}
