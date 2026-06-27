using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.FileTransfer.WebDav;
using System.Text;

namespace AtomBox.Providers.Tests;

public sealed class WebDavIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task WebDavIntegration_ListCreateUploadDownloadRenameMove_LeavesArtifacts()
    {
        var environment = WebDavTestEnvironment.TryRead();
        if (environment is null)
        {
            return;
        }

        await using var provider = await CreateProviderAsync(environment);
        var testRoot = environment.CreateTestRoot();
        var rootPath = new RemotePath(testRoot, RemotePathKind.Folder);
        var nestedPath = new RemotePath($"{testRoot}/nested", RemotePathKind.Folder);
        var sourcePath = new RemotePath($"{testRoot}/nested/source.txt", RemotePathKind.ObjectPath);
        var renamedPath = new RemotePath($"{testRoot}/nested/renamed.txt", RemotePathKind.ObjectPath);
        var movedFolderPath = new RemotePath($"{testRoot}/moved", RemotePathKind.Folder);
        var movedPath = new RemotePath($"{testRoot}/moved/renamed.txt", RemotePathKind.ObjectPath);
        var visibleFilePath = new RemotePath($"{testRoot}/webdav-visible-file.txt", RemotePathKind.ObjectPath);
        var visibleFolderPath = new RemotePath($"{testRoot}/webdav-visible-folder", RemotePathKind.Folder);
        var payload = Encoding.UTF8.GetBytes(
            $"AtomBox WebDAV integration probe{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");

        var initialList = await provider.ListAsync(RemotePath.Root);
        if (initialList.IsFailure &&
            initialList.Error?.Category is not StorageErrorCategory.Provider and not StorageErrorCategory.Network)
        {
            Assert.True(initialList.IsSuccess, FormatError("Expected WebDAV root list to succeed or be temporarily unavailable.", initialList.Error));
        }

        var createNested = await provider.CreateFolderAsync(nestedPath);
        Assert.True(createNested.IsSuccess, FormatError("Expected WebDAV nested folder creation to succeed.", createNested.Error));

        await using (var upload = new MemoryStream(payload))
        {
            var uploadResult = await provider.UploadAsync(sourcePath, upload, upload.Length);
            Assert.True(uploadResult.IsSuccess, FormatError("Expected WebDAV upload to succeed.", uploadResult.Error));
        }

        var nestedList = await provider.ListAsync(nestedPath);
        Assert.True(nestedList.IsSuccess, FormatError("Expected WebDAV nested list to succeed.", nestedList.Error));
        Assert.Contains(nestedList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "source.txt");

        await using (var download = new MemoryStream())
        {
            var downloadResult = await provider.DownloadAsync(sourcePath, download);
            Assert.True(downloadResult.IsSuccess, FormatError("Expected WebDAV download to succeed.", downloadResult.Error));
            Assert.Equal(payload, download.ToArray());
        }

        var renameResult = await provider.RenameAsync(sourcePath, "renamed.txt");
        Assert.True(renameResult.IsSuccess, FormatError("Expected WebDAV rename to succeed.", renameResult.Error));

        var listAfterRename = await provider.ListAsync(nestedPath);
        Assert.True(listAfterRename.IsSuccess, FormatError("Expected WebDAV renamed folder list to succeed.", listAfterRename.Error));
        Assert.Contains(listAfterRename.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "renamed.txt");

        var moveResult = await provider.MoveAsync(renamedPath, movedPath);
        Assert.True(moveResult.IsSuccess, FormatError("Expected WebDAV move to succeed.", moveResult.Error));

        await using (var movedDownload = new MemoryStream())
        {
            var movedDownloadResult = await provider.DownloadAsync(movedPath, movedDownload);
            Assert.True(movedDownloadResult.IsSuccess, FormatError("Expected moved WebDAV download to succeed.", movedDownloadResult.Error));
            Assert.Equal(payload, movedDownload.ToArray());
        }

        var movedList = await provider.ListAsync(movedFolderPath);
        Assert.True(movedList.IsSuccess, FormatError("Expected WebDAV moved folder list to succeed.", movedList.Error));
        Assert.Contains(movedList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "renamed.txt");

        var createVisibleFolder = await provider.CreateFolderAsync(visibleFolderPath);
        Assert.True(createVisibleFolder.IsSuccess, FormatError("Expected WebDAV visible folder creation to succeed.", createVisibleFolder.Error));

        await using (var visibleUpload = new MemoryStream(payload))
        {
            var visibleUploadResult = await provider.UploadAsync(visibleFilePath, visibleUpload, visibleUpload.Length);
            Assert.True(visibleUploadResult.IsSuccess, FormatError("Expected WebDAV visible file upload to succeed.", visibleUploadResult.Error));
        }

        var rootTestList = await provider.ListAsync(rootPath);
        Assert.True(rootTestList.IsSuccess, FormatError("Expected WebDAV test root list to succeed.", rootTestList.Error));
        Assert.Contains(rootTestList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.Folder && item.Name == "nested");
        Assert.Contains(rootTestList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.Folder && item.Name == "moved");
        Assert.Contains(rootTestList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "webdav-visible-file.txt");
        Assert.Contains(rootTestList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.Folder && item.Name == "webdav-visible-folder");
    }

    private static async Task<IStorageProvider> CreateProviderAsync(WebDavTestEnvironment environment)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.WebDavProviderId);
        var creator = new WebDavStorageProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new WebDavIntegrationCredentialStore(environment)));
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.FileTransfer,
            StorageProviderRegistry.WebDavProviderId,
            "WebDAV Integration",
            environment.Endpoint,
            null,
            new CredentialRef("webdav-integration"),
            now,
            now,
            new Dictionary<string, string>
            {
                ["rootPath"] = environment.RootPath,
                ["authMode"] = environment.AuthMode,
                ["timeoutSeconds"] = environment.TimeoutSeconds.ToString()
            });

        var result = await creator.CreateAsync(account);
        Assert.True(result.IsSuccess, FormatError("Expected WebDAV provider creation to succeed.", result.Error));
        return result.GetValueOrThrow();
    }

    private static string FormatError(string message, StorageError? error)
    {
        return error is null
            ? message
            : $"{message} Code={error.Code}; Category={error.Category}; Retryable={error.IsRetryable}; ProviderErrorCode={error.ProviderErrorCode}; Message={error.Message}";
    }

    private sealed record WebDavTestEnvironment(
        string Endpoint,
        string RootPath,
        string AuthMode,
        string? Username,
        string? Password,
        int TimeoutSeconds,
        string TestPrefix)
    {
        public static WebDavTestEnvironment? TryRead()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_WEBDAV"), "1", StringComparison.Ordinal))
            {
                return null;
            }

            var endpoint = Environment.GetEnvironmentVariable("ATOMBOX_WEBDAV_ENDPOINT");
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return null;
            }

            var authMode = Normalize(Environment.GetEnvironmentVariable("ATOMBOX_WEBDAV_AUTH_MODE"), "password");
            var username = Environment.GetEnvironmentVariable("ATOMBOX_WEBDAV_USERNAME");
            var password = Environment.GetEnvironmentVariable("ATOMBOX_WEBDAV_PASSWORD");
            if (string.Equals(authMode, "password", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)))
            {
                return null;
            }

            return new WebDavTestEnvironment(
                endpoint.Trim(),
                Normalize(Environment.GetEnvironmentVariable("ATOMBOX_WEBDAV_ROOT_PATH"), "/"),
                authMode,
                username,
                password,
                ReadInt("ATOMBOX_WEBDAV_TIMEOUT_SECONDS", 30),
                Normalize(Environment.GetEnvironmentVariable("ATOMBOX_WEBDAV_TEST_PREFIX"), "atombox-tests"));
        }

        public string CreateTestRoot()
        {
            var explicitRoot = Environment.GetEnvironmentVariable("ATOMBOX_WEBDAV_MANUAL_UPLOAD_ROOT");
            if (!string.IsNullOrWhiteSpace(explicitRoot))
            {
                return explicitRoot.Trim('/');
            }

            return $"{TestPrefix.Trim('/')}/webdav-visible-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        }

        private static string Normalize(string? value, string defaultValue)
        {
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
        }

        private static int ReadInt(string name, int defaultValue)
        {
            return int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
                ? value
                : defaultValue;
        }
    }

    private sealed class WebDavIntegrationCredentialStore : ICredentialStore
    {
        private readonly WebDavTestEnvironment _environment;

        public WebDavIntegrationCredentialStore(WebDavTestEnvironment environment)
        {
            _environment = environment;
        }

        public Task<OperationResult<CredentialRef>> SaveAsync(
            CredentialSecretMaterial material,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialRef>.Failure(StorageError.Validation("Test credential store is read-only.")));
        }

        public Task<OperationResult<CredentialLease>> AcquireLeaseAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<CredentialLease>.Success(new TestCredentialLease(credentialRef)));
        }

        public Task<OperationResult<CredentialMaterialLease>> AcquireMaterialAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            var values = new Dictionary<string, string>
            {
                ["authMode"] = _environment.AuthMode
            };
            if (!string.Equals(_environment.AuthMode, "anonymous", StringComparison.OrdinalIgnoreCase))
            {
                values["username"] = _environment.Username!;
                values["password"] = _environment.Password!;
            }

            return Task.FromResult(OperationResult<CredentialMaterialLease>.Success(
                new CredentialMaterialLease(
                    new TestCredentialLease(credentialRef),
                    new CredentialSecretMaterial(values))));
        }

        public Task<OperationResult<bool>> ExistsAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<bool>.Success(true));
        }

        public Task<OperationResult> MarkPendingDeleteAsync(
            CredentialRef credentialRef,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class TestCredentialLease : CredentialLease
    {
        public TestCredentialLease(CredentialRef credentialRef)
            : base(credentialRef, "webdav-integration-test")
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
