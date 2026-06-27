using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.Providers;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Providers.Common;
using AtomBox.Providers.FileTransfer.Ftp;
using System.Text;

namespace AtomBox.Providers.Tests;

public sealed class FtpIntegrationTests
{
    [Fact]
    [Trait("Category", "ManualIntegration")]
    public async Task FtpIntegration_ManualUploadFileAndFolder_WhenExplicitlyEnabled()
    {
        var environment = FtpTestEnvironment.TryRead();
        if (environment is null ||
            !string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_FTP_MANUAL_UPLOAD"), "1", StringComparison.Ordinal))
        {
            return;
        }

        await using var provider = await CreateProviderAsync(environment);
        var testRoot = environment.CreateManualUploadRoot();
        var fileName = ReadManualName("ATOMBOX_FTP_MANUAL_FILE_NAME", "manual-file.txt");
        var folderName = ReadManualName("ATOMBOX_FTP_MANUAL_FOLDER_NAME", "manual-folder");
        var folderPath = new RemotePath($"{testRoot}/{folderName}", RemotePathKind.Folder);
        var standaloneFilePath = new RemotePath($"{testRoot}/{fileName}", RemotePathKind.ObjectPath);
        var folderFilePath = new RemotePath($"{testRoot}/{folderName}/inside-folder.txt", RemotePathKind.ObjectPath);

        var createFolder = await provider.CreateFolderAsync(folderPath);
        Assert.True(createFolder.IsSuccess, FormatError("Expected FTP manual folder creation to succeed.", createFolder.Error));

        await UploadTextAsync(provider, standaloneFilePath, "AtomBox FTP manual standalone file");
        await UploadTextAsync(provider, folderFilePath, "AtomBox FTP manual file inside uploaded folder");

        var rootList = await provider.ListAsync(new RemotePath(testRoot, RemotePathKind.Folder));
        Assert.True(rootList.IsSuccess, FormatError("Expected FTP manual root list to succeed.", rootList.Error));
        Assert.Contains(rootList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == fileName);
        Assert.Contains(rootList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.Folder && item.Name == folderName);

        var folderList = await provider.ListAsync(folderPath);
        Assert.True(folderList.IsSuccess, FormatError("Expected FTP manual folder list to succeed.", folderList.Error));
        Assert.Contains(folderList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "inside-folder.txt");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FtpIntegration_ListCreateUploadDownloadRenameMoveDelete()
    {
        var environment = FtpTestEnvironment.TryRead();
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
        var payload = Encoding.UTF8.GetBytes(
            $"AtomBox FTP integration probe{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");

        try
        {
            var initialList = await provider.ListAsync(RemotePath.Root);
            Assert.True(initialList.IsSuccess, FormatError("Expected FTP root list to succeed.", initialList.Error));

            var createFolder = await provider.CreateFolderAsync(nestedPath);
            Assert.True(createFolder.IsSuccess, FormatError("Expected FTP folder creation to succeed.", createFolder.Error));

            await using (var upload = new MemoryStream(payload))
            {
                var uploadResult = await provider.UploadAsync(sourcePath, upload, upload.Length);
                Assert.True(uploadResult.IsSuccess, FormatError("Expected FTP upload to succeed.", uploadResult.Error));
            }

            var nestedList = await provider.ListAsync(nestedPath);
            Assert.True(nestedList.IsSuccess, FormatError("Expected FTP nested list to succeed.", nestedList.Error));
            Assert.Contains(nestedList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "source.txt");

            await using (var download = new MemoryStream())
            {
                var downloadResult = await provider.DownloadAsync(sourcePath, download);
                Assert.True(downloadResult.IsSuccess, FormatError("Expected FTP download to succeed.", downloadResult.Error));
                Assert.Equal(payload, download.ToArray());
            }

            var renameResult = await provider.RenameAsync(sourcePath, "renamed.txt");
            Assert.True(renameResult.IsSuccess, FormatError("Expected FTP rename to succeed.", renameResult.Error));

            var listAfterRename = await provider.ListAsync(nestedPath);
            Assert.True(listAfterRename.IsSuccess, FormatError("Expected FTP renamed folder list to succeed.", listAfterRename.Error));
            Assert.Contains(listAfterRename.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "renamed.txt");

            var moveResult = await provider.MoveAsync(renamedPath, movedPath);
            Assert.True(moveResult.IsSuccess, FormatError("Expected FTP move to succeed.", moveResult.Error));

            await using (var movedDownload = new MemoryStream())
            {
                var movedDownloadResult = await provider.DownloadAsync(movedPath, movedDownload);
                Assert.True(movedDownloadResult.IsSuccess, FormatError("Expected moved FTP download to succeed.", movedDownloadResult.Error));
                Assert.Equal(payload, movedDownload.ToArray());
            }

            var movedList = await provider.ListAsync(movedFolderPath);
            Assert.True(movedList.IsSuccess, FormatError("Expected FTP moved folder list to succeed.", movedList.Error));
            Assert.Contains(movedList.GetValueOrThrow(), item => item.Kind == RemoteItemKind.File && item.Name == "renamed.txt");

            var deleteFile = await provider.DeleteAsync(movedPath);
            Assert.True(deleteFile.IsSuccess, FormatError("Expected FTP file delete to succeed.", deleteFile.Error));

            var deleteMovedFolder = await provider.DeleteAsync(movedFolderPath);
            Assert.True(deleteMovedFolder.IsSuccess, FormatError("Expected FTP moved folder delete to succeed.", deleteMovedFolder.Error));

            var deleteNestedFolder = await provider.DeleteAsync(nestedPath);
            Assert.True(deleteNestedFolder.IsSuccess, FormatError("Expected FTP nested folder delete to succeed.", deleteNestedFolder.Error));

            var deleteRootFolder = await provider.DeleteAsync(rootPath);
            Assert.True(deleteRootFolder.IsSuccess, FormatError("Expected FTP root test folder delete to succeed.", deleteRootFolder.Error));
        }
        finally
        {
            await provider.DeleteAsync(movedPath);
            await provider.DeleteAsync(renamedPath);
            await provider.DeleteAsync(sourcePath);
            await provider.DeleteAsync(movedFolderPath);
            await provider.DeleteAsync(nestedPath);
            await provider.DeleteAsync(rootPath);
        }
    }

    private static async Task<IStorageProvider> CreateProviderAsync(FtpTestEnvironment environment)
    {
        var descriptor = StorageProviderRegistry.CreateDefaultDescriptors()
            .Single(item => item.Id == StorageProviderRegistry.FtpProviderId);
        var creator = new FtpStorageProviderCreator(
            descriptor,
            new ProviderCredentialResolver(new FtpIntegrationCredentialStore(environment)));
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.FileTransfer,
            StorageProviderRegistry.FtpProviderId,
            "FTP Integration",
            environment.Host,
            null,
            new CredentialRef("ftp-integration"),
            now,
            now,
            new Dictionary<string, string>
            {
                ["port"] = environment.Port.ToString(),
                ["rootPath"] = environment.RootPath,
                ["authMode"] = environment.AuthMode,
                ["transferMode"] = environment.TransferMode,
                ["timeoutSeconds"] = environment.TimeoutSeconds.ToString()
            });

        var result = await creator.CreateAsync(account);
        Assert.True(result.IsSuccess, FormatError("Expected FTP provider creation to succeed.", result.Error));
        return result.GetValueOrThrow();
    }

    private static async Task UploadTextAsync(IStorageProvider provider, RemotePath path, string label)
    {
        var payload = Encoding.UTF8.GetBytes(
            $"{label}{Environment.NewLine}CreatedAtUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}");
        await using var upload = new MemoryStream(payload);

        var result = await provider.UploadAsync(path, upload, upload.Length);
        Assert.True(result.IsSuccess, FormatError("Expected FTP manual upload to succeed.", result.Error));
    }

    private static string ReadManualName(string environmentVariableName, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var normalized = value.Trim();
        return normalized.Contains('/', StringComparison.Ordinal) || normalized.Contains('\\', StringComparison.Ordinal)
            ? defaultValue
            : normalized;
    }

    private static string FormatError(string message, StorageError? error)
    {
        return error is null
            ? message
            : $"{message} Code={error.Code}; Category={error.Category}; Retryable={error.IsRetryable}; ProviderErrorCode={error.ProviderErrorCode}; Message={error.Message}";
    }

    private sealed record FtpTestEnvironment(
        string Host,
        int Port,
        string RootPath,
        string AuthMode,
        string? Username,
        string? Password,
        string TransferMode,
        int TimeoutSeconds,
        string TestPrefix)
    {
        public static FtpTestEnvironment? TryRead()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("ATOMBOX_TEST_FTP"), "1", StringComparison.Ordinal))
            {
                return null;
            }

            var host = Environment.GetEnvironmentVariable("ATOMBOX_FTP_HOST");
            if (string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            var authMode = Normalize(Environment.GetEnvironmentVariable("ATOMBOX_FTP_AUTH_MODE"), "password");
            var username = Environment.GetEnvironmentVariable("ATOMBOX_FTP_USERNAME");
            var password = Environment.GetEnvironmentVariable("ATOMBOX_FTP_PASSWORD");
            if (string.Equals(authMode, "password", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)))
            {
                return null;
            }

            return new FtpTestEnvironment(
                host,
                ReadInt("ATOMBOX_FTP_PORT", 21),
                Normalize(Environment.GetEnvironmentVariable("ATOMBOX_FTP_ROOT_PATH"), "/"),
                authMode,
                username,
                password,
                Normalize(Environment.GetEnvironmentVariable("ATOMBOX_FTP_TRANSFER_MODE"), "passive"),
                ReadInt("ATOMBOX_FTP_TIMEOUT_SECONDS", 30),
                Normalize(Environment.GetEnvironmentVariable("ATOMBOX_FTP_TEST_PREFIX"), "atombox-tests"));
        }

        public string CreateTestRoot()
        {
            return $"{TestPrefix.Trim('/')}/ftp-smoke-{Guid.NewGuid():N}";
        }

        public string CreateManualUploadRoot()
        {
            var explicitRoot = Environment.GetEnvironmentVariable("ATOMBOX_FTP_MANUAL_UPLOAD_ROOT");
            if (!string.IsNullOrWhiteSpace(explicitRoot))
            {
                return explicitRoot.Trim('/');
            }

            return $"{TestPrefix.Trim('/')}/ftp-manual-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
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

    private sealed class FtpIntegrationCredentialStore : ICredentialStore
    {
        private readonly FtpTestEnvironment _environment;

        public FtpIntegrationCredentialStore(FtpTestEnvironment environment)
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
            : base(credentialRef, "ftp-integration-test")
        {
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
