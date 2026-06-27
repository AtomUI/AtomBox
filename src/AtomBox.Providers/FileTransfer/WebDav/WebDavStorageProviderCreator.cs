using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;

namespace AtomBox.Providers.FileTransfer.WebDav;

public sealed class WebDavStorageProviderCreator : IStorageProviderCreator
{
    private readonly ProviderCredentialResolver _credentialResolver;

    public WebDavStorageProviderCreator(
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

        if (!Uri.TryCreate(account.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            return OperationResult<IStorageProvider>.Failure(StorageError.Validation("WebDAV endpoint must be an absolute http or https URL."));
        }

        var timeoutResult = GetTimeoutSeconds(account.GetProviderConfigValue("timeoutSeconds"));
        if (timeoutResult.IsFailure)
        {
            return OperationResult<IStorageProvider>.Failure(timeoutResult.Error!);
        }

        if (IsAnonymous(account))
        {
            var anonymousLease = new CredentialMaterialLease(
                new AnonymousCredentialLease(account.CredentialRef),
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["authMode"] = "anonymous"
                }));
            return OperationResult<IStorageProvider>.Success(
                new WebDavStorageProvider(
                    new WebDavHttpClientAdapter(endpoint, null, null, timeoutResult.GetValueOrThrow()),
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

            return OperationResult<IStorageProvider>.Success(
                new WebDavStorageProvider(
                    new WebDavHttpClientAdapter(
                        endpoint,
                        materialLease.Material.GetRequiredValue("username"),
                        materialLease.Material.GetRequiredValue("password"),
                        timeoutResult.GetValueOrThrow()),
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

    private static OperationResult<int> GetTimeoutSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OperationResult<int>.Success(30);
        }

        return int.TryParse(value, out var timeoutSeconds) && timeoutSeconds is >= 1 and <= 600
            ? OperationResult<int>.Success(timeoutSeconds)
            : OperationResult<int>.Failure(StorageError.Validation("WebDAV timeoutSeconds must be between 1 and 600."));
    }

    private static OperationResult ValidateSecretMaterial(CredentialSecretMaterial material)
    {
        if (!material.TryGetValue("username", out _))
        {
            return OperationResult.Failure(StorageError.Validation("WebDAV credential requires username."));
        }

        if (!material.TryGetValue("password", out _))
        {
            return OperationResult.Failure(StorageError.Validation("WebDAV credential requires password."));
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
        public AnonymousCredentialLease(CredentialRef credentialRef)
            : base(credentialRef, "webdav-anonymous")
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
