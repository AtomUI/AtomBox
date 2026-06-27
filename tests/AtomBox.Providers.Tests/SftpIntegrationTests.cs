using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.FileTransfer.Sftp;
using System.Text;

namespace AtomBox.Providers.Tests;

public sealed class SftpIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SftpIntegration_ListCreateUploadDownloadRenameMove_LeavesArtifacts()
    {
        var environment = SftpTestEnvironment.TryRead();
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
        var visibleFilePath = new RemotePath($"{testRoot}/sftp-visible-file.txt", RemotePathKind.ObjectPath);
        var visibleFolderPath = new RemotePath($"{testRoot}/sftp-visible-folder", RemotePathKind.Folder);
        var payload = Encoding.UTF8.GetBytes(
            $"AtomBox SFTP integration probe{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");

        var initialList = await provider.ListAsync(RemotePath.Root);
        Assert.True(initialList.IsSuccess, FormatError("Expected SFTP root list to succeed.", initialList.Error));

        var createNested = await provider.CreateFolderAsync(nestedPath);
        Assert.True(createNested.IsSuccess, FormatError("Expected SFTP nested folder creation to succeed.", createNested.Error));

        await using (var upload = new MemoryStream(payload))
        {
            var uploadResult = await provider.UploadAsync(sourcePath, upload, upload.Length);
            Assert.True(uploadResult.IsSuccess, FormatError("Expected SFTP upload to succeed.", uploadResult.Error));
        }

        var nestedList = await provider.ListAsync(nestedPath);
        Assert.True(nestedList.IsSuccess, FormatError("Expected SFTP nested list to succeed.", nestedList.Error));
        Assert.Contains(nestedList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "source.txt");

        await using (var download = new MemoryStream())
        {
            var downloadResult = await provider.DownloadAsync(sourcePath, download);
            Assert.True(downloadResult.IsSuccess, FormatError("Expected SFTP download to succeed.", downloadResult.Error));
            Assert.Equal(payload, download.ToArray());
        }

        var renameResult = await provider.RenameAsync(sourcePath, "renamed.txt");
        Assert.True(renameResult.IsSuccess, FormatError("Expected SFTP rename to succeed.", renameResult.Error));

        var listAfterRename = await provider.ListAsync(nestedPath);
        Assert.True(listAfterRename.IsSuccess, FormatError("Expected SFTP renamed folder list to succeed.", listAfterRename.Error));
        Assert.Contains(listAfterRename.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "renamed.txt");

        var moveResult = await provider.MoveAsync(renamedPath, movedPath);
        Assert.True(moveResult.IsSuccess, FormatError("Expected SFTP move to succeed.", moveResult.Error));

        await using (var movedDownload = new MemoryStream())
        {
            var movedDownloadResult = await provider.DownloadAsync(movedPath, movedDownload);
            Assert.True(movedDownloadResult.IsSuccess, FormatError("Expected moved SFTP download to succeed.", movedDownloadResult.Error));
            Assert.Equal(payload, movedDownload.ToArray());
        }

        var movedList = await provider.ListAsync(movedFolderPath);
        Assert.True(movedList.IsSuccess, FormatError("Expected SFTP moved folder list to succeed.", movedList.Error));
        Assert.Contains(movedList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "renamed.txt");

        var createVisibleFolder = await provider.CreateFolderAsync(visibleFolderPath);
        Assert.True(createVisibleFolder.IsSuccess, FormatError("Expected SFTP visible folder creation to succeed.", createVisibleFolder.Error));

        await using (var visibleUpload = new MemoryStream(payload))
        {
            var visibleUploadResult = await provider.UploadAsync(visibleFilePath, visibleUpload, visibleUpload.Length);
            Assert.True(visibleUploadResult.IsSuccess, FormatError("Expected SFTP visible file upload to succeed.", visibleUploadResult.Error));
        }

        var rootTestList = await provider.ListAsync(rootPath);
        Assert.True(rootTestList.IsSuccess, FormatError("Expected SFTP test root list to succeed.", rootTestList.Error));
        Assert.Contains(rootTestList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.Folder && item.Name == "nested");
        Assert.Contains(rootTestList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.Folder && item.Name == "moved");
        Assert.Contains(rootTestList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "sftp-visible-file.txt");
        Assert.Contains(rootTestList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.Folder && item.Name == "sftp-visible-folder");
    }

    private static async Task<IStorageProvider> CreateProviderAsync(SftpTestEnvironment environment)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.SftpProviderId);
        var creator = new SftpStorageProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new SftpIntegrationCredentialStore(environment)));
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.FileTransfer,
            StorageProviderRegistry.SftpProviderId,
            "SFTP Integration",
            environment.Host,
            null,
            new CredentialRef("sftp-integration"),
            now,
            now,
            new Dictionary<string, string>
            {
                ["port"] = environment.Port.ToString(),
                ["rootPath"] = environment.RootPath,
                ["authMode"] = environment.AuthMode,
                ["hostKeyPolicy"] = environment.HostKeyPolicy,
                ["hostKeyFingerprint"] = environment.HostKeyFingerprint ?? string.Empty
            });

        var result = await creator.CreateAsync(account);
        Assert.True(result.IsSuccess, FormatError("Expected SFTP provider creation to succeed.", result.Error));
        return result.GetValueOrThrow();
    }

    private static string FormatError(string message, StorageError? error)
    {
        return error is null
            ? message
            : $"{message} Code={error.Code}; Category={error.Category}; Retryable={error.IsRetryable}; ProviderErrorCode={error.ProviderErrorCode}; Message={error.Message}";
    }

    private sealed record SftpTestEnvironment(
        string Host,
        int Port,
        string RootPath,
        string AuthMode,
        string Username,
        string? Password,
        string? PrivateKey,
        string? PrivateKeyPassphrase,
        string HostKeyPolicy,
        string? HostKeyFingerprint,
        string TestPrefix)
    {
        public static SftpTestEnvironment? TryRead()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_SFTP"), "1", StringComparison.Ordinal))
            {
                return null;
            }

            var host = Environment.GetEnvironmentVariable("ATOMBOX_SFTP_HOST");
            var username = Environment.GetEnvironmentVariable("ATOMBOX_SFTP_USERNAME");
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            var authMode = Normalize(Environment.GetEnvironmentVariable("ATOMBOX_SFTP_AUTH_MODE"), "password");
            var password = Environment.GetEnvironmentVariable("ATOMBOX_SFTP_PASSWORD");
            var privateKey = Environment.GetEnvironmentVariable("ATOMBOX_SFTP_PRIVATE_KEY");
            if (string.Equals(authMode, "password", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            if (string.Equals(authMode, "privateKey", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(privateKey))
            {
                return null;
            }

            return new SftpTestEnvironment(
                host,
                ReadInt("ATOMBOX_SFTP_PORT", 22),
                Normalize(Environment.GetEnvironmentVariable("ATOMBOX_SFTP_ROOT_PATH"), "/"),
                authMode,
                username,
                password,
                privateKey,
                Environment.GetEnvironmentVariable("ATOMBOX_SFTP_PRIVATE_KEY_PASSPHRASE"),
                Normalize(Environment.GetEnvironmentVariable("ATOMBOX_SFTP_HOST_KEY_POLICY"), "acceptAny"),
                Environment.GetEnvironmentVariable("ATOMBOX_SFTP_HOST_KEY_FINGERPRINT"),
                Normalize(Environment.GetEnvironmentVariable("ATOMBOX_SFTP_TEST_PREFIX"), "atombox-tests"));
        }

        public string CreateTestRoot()
        {
            var explicitRoot = Environment.GetEnvironmentVariable("ATOMBOX_SFTP_MANUAL_UPLOAD_ROOT");
            if (!string.IsNullOrWhiteSpace(explicitRoot))
            {
                return explicitRoot.Trim('/');
            }

            return $"{TestPrefix.Trim('/')}/sftp-visible-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
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

    private sealed class SftpIntegrationCredentialStore : ICredentialStore
    {
        private readonly SftpTestEnvironment _environment;

        public SftpIntegrationCredentialStore(SftpTestEnvironment environment)
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
                ["authMode"] = _environment.AuthMode,
                ["username"] = _environment.Username
            };
            if (string.Equals(_environment.AuthMode, "privateKey", StringComparison.OrdinalIgnoreCase))
            {
                values["privateKey"] = _environment.PrivateKey!;
                if (!string.IsNullOrWhiteSpace(_environment.PrivateKeyPassphrase))
                {
                    values["privateKeyPassphrase"] = _environment.PrivateKeyPassphrase;
                }
            }
            else
            {
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
            : base(credentialRef, "sftp-integration-test")
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
