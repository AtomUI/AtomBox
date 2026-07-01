using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using System.Text;

namespace AtomBox.Providers.Tests;

internal static class S3CompatibleIntegrationTestHarness
{
    public static async Task RunVisibleFunctionalTestAsync(
        S3CompatibleIntegrationEnvironment environment,
        Func<ProviderDescriptor, ProviderCredentialResolver, IStorageProviderCreator> createCreator)
    {
        await using var provider = await CreateProviderAsync(environment, createCreator).ConfigureAwait(false);
        var folderPrefix = environment.CreateVisibleFolderPrefix();
        var rootObjectPath = new RemotePath($"{environment.Bucket}/{folderPrefix}visible-root-file.txt", RemotePathKind.ObjectPath);
        var childObjectPath = new RemotePath($"{environment.Bucket}/{folderPrefix}child/visible-child-file.txt", RemotePathKind.ObjectPath);
        var createdFolderPath = new RemotePath($"{environment.Bucket}/{folderPrefix}created-folder", RemotePathKind.Folder);
        var folderPath = new RemotePath($"{environment.Bucket}/{folderPrefix.TrimEnd('/')}", RemotePathKind.Folder);
        var childFolderPath = new RemotePath($"{environment.Bucket}/{folderPrefix}child", RemotePathKind.Folder);
        var movedFolderPath = new RemotePath($"{environment.Bucket}/{folderPrefix}moved", RemotePathKind.Folder);
        var renameSourcePath = new RemotePath($"{environment.Bucket}/{folderPrefix}rename-source.txt", RemotePathKind.ObjectPath);
        var renamedPath = new RemotePath($"{environment.Bucket}/{folderPrefix}rename-destination.txt", RemotePathKind.ObjectPath);
        var movedPath = new RemotePath($"{environment.Bucket}/{folderPrefix}moved/move-destination.txt", RemotePathKind.ObjectPath);
        var rootPayload = Encoding.UTF8.GetBytes(
            $"AtomBox {environment.DisplayName} visible root object{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        var childPayload = Encoding.UTF8.GetBytes(
            $"AtomBox {environment.DisplayName} visible child object{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        var renamePayload = Encoding.UTF8.GetBytes(
            $"AtomBox {environment.DisplayName} rename/move object{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");

        var bucketList = await provider.ListAsync(new RemotePath(environment.Bucket, RemotePathKind.BucketRoot)).ConfigureAwait(false);
        Assert.True(bucketList.IsSuccess, FormatError($"Expected {environment.DisplayName} bucket root list to succeed.", bucketList.Error));

        await UploadAsync(provider, rootObjectPath, rootPayload, $"{environment.DisplayName} root object upload").ConfigureAwait(false);
        await UploadAsync(provider, childObjectPath, childPayload, $"{environment.DisplayName} child object upload").ConfigureAwait(false);
        await UploadAsync(provider, renameSourcePath, renamePayload, $"{environment.DisplayName} rename source upload").ConfigureAwait(false);

        var createFolder = await provider.CreateFolderAsync(createdFolderPath).ConfigureAwait(false);
        Assert.True(createFolder.IsSuccess, FormatError($"Expected {environment.DisplayName} folder marker creation to succeed.", createFolder.Error));

        var rename = await provider.RenameAsync(renameSourcePath, "rename-destination.txt").ConfigureAwait(false);
        Assert.True(rename.IsSuccess, FormatError($"Expected {environment.DisplayName} rename to succeed.", rename.Error));

        var move = await provider.MoveAsync(renamedPath, movedPath).ConfigureAwait(false);
        Assert.True(move.IsSuccess, FormatError($"Expected {environment.DisplayName} move to succeed.", move.Error));

        var folderList = await provider.ListAsync(folderPath).ConfigureAwait(false);
        Assert.True(folderList.IsSuccess, FormatError($"Expected {environment.DisplayName} folder prefix list to succeed.", folderList.Error));
        var folderItems = folderList.GetValueOrThrow();
        Assert.Contains(folderItems, item => item.Kind == RemoteItemKind.File && item.Name == "visible-root-file.txt");
        Assert.Contains(folderItems, item => item.Kind == RemoteItemKind.Folder && item.Name == "child");
        Assert.Contains(folderItems, item => item.Kind == RemoteItemKind.Folder && item.Name == "created-folder");
        Assert.Contains(folderItems, item => item.Kind == RemoteItemKind.Folder && item.Name == "moved");

        var childFolderList = await provider.ListAsync(childFolderPath).ConfigureAwait(false);
        Assert.True(childFolderList.IsSuccess, FormatError($"Expected {environment.DisplayName} child prefix list to succeed.", childFolderList.Error));
        Assert.Contains(childFolderList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "visible-child-file.txt");

        var movedFolderList = await provider.ListAsync(movedFolderPath).ConfigureAwait(false);
        Assert.True(movedFolderList.IsSuccess, FormatError($"Expected {environment.DisplayName} moved prefix list to succeed.", movedFolderList.Error));
        Assert.Contains(movedFolderList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "move-destination.txt");

        await DownloadAndAssertAsync(provider, rootObjectPath, rootPayload, $"{environment.DisplayName} root object download").ConfigureAwait(false);
        await DownloadAndAssertAsync(provider, childObjectPath, childPayload, $"{environment.DisplayName} child object download").ConfigureAwait(false);
        await DownloadAndAssertAsync(provider, movedPath, renamePayload, $"{environment.DisplayName} moved object download").ConfigureAwait(false);
    }


    public static async Task RunBucketRootPageTestAsync(
        S3CompatibleIntegrationEnvironment environment,
        Func<ProviderDescriptor, ProviderCredentialResolver, IStorageProviderCreator> createCreator)
    {
        await using var provider = await CreateProviderAsync(environment, createCreator).ConfigureAwait(false);
        var pageResult = await provider.ListPageAsync(
            new RemotePath(environment.Bucket, RemotePathKind.BucketRoot),
            RemotePageRequest.FirstPage).ConfigureAwait(false);
        Assert.True(pageResult.IsSuccess, FormatError($"Expected {environment.DisplayName} bucket root page list to succeed.", pageResult.Error));
        Assert.Equal(new RemotePath(environment.Bucket, RemotePathKind.BucketRoot), pageResult.GetValueOrThrow().Path);
    }
    public static string FormatError(string message, StorageError? error)
    {
        return error is null
            ? message
            : $"{message} Code={error.Code}; Category={error.Category}; Retryable={error.IsRetryable}; ProviderErrorCode={error.ProviderErrorCode}; Message={error.Message}";
    }

    private static async Task<IStorageProvider> CreateProviderAsync(
        S3CompatibleIntegrationEnvironment environment,
        Func<ProviderDescriptor, ProviderCredentialResolver, IStorageProviderCreator> createCreator)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == environment.ProviderId);
        var creator = createCreator(
            descriptor,
            new ProviderCredentialResolver(new EnvironmentCredentialStore(environment)));
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            environment.ProviderId,
            $"{environment.DisplayName} Integration",
            environment.Endpoint,
            environment.Region,
            new CredentialRef(environment.CredentialName),
            now,
            now,
            new Dictionary<string, string>
            {
                ["bucket"] = environment.Bucket,
                ["region"] = environment.Region
            });

        var result = await creator.CreateAsync(account).ConfigureAwait(false);
        Assert.True(result.IsSuccess, FormatError($"Expected {environment.DisplayName} provider creation to succeed.", result.Error));
        return result.GetValueOrThrow();
    }

    private static async Task UploadAsync(
        IStorageProvider provider,
        RemotePath path,
        byte[] payload,
        string operationName)
    {
        await using var upload = new MemoryStream(payload);
        var uploadResult = await provider.UploadAsync(path, upload, upload.Length).ConfigureAwait(false);
        Assert.True(uploadResult.IsSuccess, FormatError($"Expected {operationName} to succeed.", uploadResult.Error));
    }

    private static async Task DownloadAndAssertAsync(
        IStorageProvider provider,
        RemotePath path,
        byte[] expectedPayload,
        string operationName)
    {
        await using var download = new MemoryStream();
        var downloadResult = await provider.DownloadAsync(path, download).ConfigureAwait(false);
        Assert.True(downloadResult.IsSuccess, FormatError($"Expected {operationName} to succeed.", downloadResult.Error));
        Assert.Equal(expectedPayload, download.ToArray());
    }

    private sealed class EnvironmentCredentialStore : ICredentialStore
    {
        private readonly S3CompatibleIntegrationEnvironment _environment;

        public EnvironmentCredentialStore(S3CompatibleIntegrationEnvironment environment)
        {
            _environment = environment;
        }

        public Task<OperationResult<CredentialRef>> SaveAsync(
            CredentialSecretMaterial material,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialRef>.Success(new CredentialRef(_environment.CredentialName)));
        }

        public Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialLease>.Success(new EnvironmentCredentialLease(_environment.CredentialName)));
        }

        public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            var lease = new CredentialMaterialLease(
                new EnvironmentCredentialLease(_environment.CredentialName),
                new CredentialSecretMaterial(new Dictionary<string, string>
                {
                    ["accessKeyId"] = _environment.AccessKeyId,
                    ["accessKeySecret"] = _environment.AccessKeySecret
                }));

            return Task.FromResult(OperationResult<CredentialMaterialLease>.Success(lease));
        }

        public Task<OperationResult<bool>> ExistsAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<bool>.Success(credentialRef.Value == _environment.CredentialName));
        }

        public Task<OperationResult> MarkPendingDeleteAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class EnvironmentCredentialLease : CredentialLease
    {
        public EnvironmentCredentialLease(string credentialName)
            : base(new CredentialRef(credentialName), credentialName)
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed record S3CompatibleIntegrationEnvironment(
    StorageProviderId ProviderId,
    string DisplayName,
    string EnvironmentPrefix,
    string Endpoint,
    string Region,
    string Bucket,
    string AccessKeyId,
    string AccessKeySecret,
    string TestPrefix)
{
    public string CredentialName => $"{EnvironmentPrefix.ToLowerInvariant().Replace('_', '-')}-integration";

    public static S3CompatibleIntegrationEnvironment? TryRead(
        StorageProviderId providerId,
        string displayName,
        string environmentPrefix,
        string regionVariableName = "REGION")
    {
        if (!string.Equals(Environment.GetEnvironmentVariable($"ATOMBOX_TEST_{environmentPrefix}"), "1", StringComparison.Ordinal))
        {
            return null;
        }

        var endpoint = Environment.GetEnvironmentVariable($"ATOMBOX_{environmentPrefix}_ENDPOINT");
        var region = Environment.GetEnvironmentVariable($"ATOMBOX_{environmentPrefix}_{regionVariableName}");
        var bucket = Environment.GetEnvironmentVariable($"ATOMBOX_{environmentPrefix}_BUCKET");
        var accessKeyId = Environment.GetEnvironmentVariable($"ATOMBOX_{environmentPrefix}_ACCESS_KEY_ID");
        var accessKeySecret = Environment.GetEnvironmentVariable($"ATOMBOX_{environmentPrefix}_ACCESS_KEY_SECRET");
        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(region) ||
            string.IsNullOrWhiteSpace(bucket) ||
            string.IsNullOrWhiteSpace(accessKeyId) ||
            string.IsNullOrWhiteSpace(accessKeySecret))
        {
            return null;
        }

        var prefix = Environment.GetEnvironmentVariable($"ATOMBOX_{environmentPrefix}_TEST_PREFIX");
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "atombox-tests/";
        }

        return new S3CompatibleIntegrationEnvironment(
            providerId,
            displayName,
            environmentPrefix,
            endpoint.Trim(),
            region.Trim(),
            bucket.Trim(),
            accessKeyId.Trim(),
            accessKeySecret.Trim(),
            NormalizePrefix(prefix));
    }

    public string CreateVisibleFolderPrefix()
    {
        var explicitPrefix = Environment.GetEnvironmentVariable($"ATOMBOX_{EnvironmentPrefix}_MANUAL_PREFIX");
        if (!string.IsNullOrWhiteSpace(explicitPrefix))
        {
            return NormalizePrefix(explicitPrefix);
        }

        return $"{TestPrefix}{EnvironmentPrefix.ToLowerInvariant().Replace('_', '-')}-visible-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}/";
    }

    private static string NormalizePrefix(string prefix)
    {
        return prefix.Trim().TrimStart('/').TrimEnd('/') + "/";
    }
}
