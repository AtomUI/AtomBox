using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AtomBox.Application.Accounts;
using AtomBox.Application.DependencyInjection;
using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.DependencyInjection;
using AtomBox.Providers.Common;
using AtomBox.Providers.DependencyInjection;
using AtomBox.Transfer.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;

namespace AtomBox.EndToEnd.Tests;

internal sealed class GenericS3EndToEndContext : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly string _temporaryStateRoot;

    private GenericS3EndToEndContext(
        GenericS3EndToEndEnvironment environment,
        ServiceProvider services,
        string runId,
        string runPrefix,
        string artifactRoot,
        string temporaryStateRoot,
        StorageAccount account)
    {
        Environment = environment;
        _services = services;
        RunId = runId;
        RunPrefix = runPrefix;
        ArtifactRoot = artifactRoot;
        _temporaryStateRoot = temporaryStateRoot;
        Account = account;
    }

    public GenericS3EndToEndEnvironment Environment { get; }

    public string RunId { get; }

    public string RunPrefix { get; }

    public string ArtifactRoot { get; }

    public StorageAccount Account { get; internal set; }

    public T GetRequiredService<T>() where T : notnull => _services.GetRequiredService<T>();

    public static async Task<GenericS3EndToEndContext?> CreateAsync()
    {
        var environment = GenericS3EndToEndEnvironment.TryRead();
        if (environment is null)
        {
            return null;
        }

        var runId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var runPrefix = $"{environment.TestPrefix}e2e-{runId}/";
        var artifactRoot = Path.Combine(
            System.Environment.CurrentDirectory,
            ".artifacts",
            "generic-s3-e2e",
            runId);
        var temporaryStateRoot = Path.Combine(Path.GetTempPath(), "AtomBox", "generic-s3-e2e", runId);
        Directory.CreateDirectory(artifactRoot);

        var services = new ServiceCollection();
        services.AddAtomBoxInfrastructure();
        services.AddSingleton(new AtomBoxStoragePaths(temporaryStateRoot));
        services.AddAtomBoxProviders();
        services.AddAtomBoxTransfer();
        services.AddAtomBoxApplication();
        var serviceProvider = services.BuildServiceProvider(validateScopes: true);

        var initializeResult = serviceProvider.GetRequiredService<InfrastructureInitializer>().Initialize();
        AssertSuccess(initializeResult, "Initialize isolated Infrastructure state");

        var credentials = serviceProvider.GetRequiredService<ICredentialStore>();
        var credentialResult = await credentials.SaveAsync(new CredentialSecretMaterial(
            new Dictionary<string, string>
            {
                ["accessKeyId"] = environment.AccessKeyId,
                ["accessKeySecret"] = environment.AccessKeySecret
            }));
        AssertSuccess(credentialResult, "Persist Generic S3 credentials");

        var accountService = serviceProvider.GetRequiredService<AccountAppService>();
        var accountResult = await accountService.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            StorageProviderRegistry.S3CompatibleProviderId,
            $"Generic S3 E2E {runId}",
            environment.Endpoint,
            environment.Region,
            credentialResult.GetValueOrThrow(),
            new Dictionary<string, string> { ["region"] = environment.Region }));
        AssertSuccess(accountResult, "Persist Generic S3 account");

        var accountId = accountResult.GetValueOrThrow().Id;
        var storedAccountResult = await serviceProvider.GetRequiredService<IStorageAccountRepository>()
            .GetByIdAsync(accountId);
        AssertSuccess(storedAccountResult, "Reload Generic S3 account");

        var context = new GenericS3EndToEndContext(
            environment,
            serviceProvider,
            runId,
            runPrefix,
            artifactRoot,
            temporaryStateRoot,
            storedAccountResult.GetValueOrThrow());
        await context.WriteLocalReportAsync("started", new Dictionary<string, object?>());
        return context;
    }

    public async Task<IStorageProvider> CreateProviderAsync()
    {
        var result = await GetRequiredService<IStorageProviderFactory>().CreateAsync(Account);
        AssertSuccess(result, "Create Generic S3 provider");
        return result.GetValueOrThrow();
    }

    public RemotePath ObjectPath(string relativeKey)
    {
        return new RemotePath(
            $"{Environment.Bucket}/{RunPrefix}{relativeKey.TrimStart('/')}",
            RemotePathKind.ObjectPath);
    }

    public RemotePath FolderPath(string relativeKey)
    {
        return new RemotePath(
            $"{Environment.Bucket}/{RunPrefix}{relativeKey.Trim('/')}",
            RemotePathKind.Folder);
    }

    public RemotePath RunFolderPath => new(
        $"{Environment.Bucket}/{RunPrefix.TrimEnd('/')}",
        RemotePathKind.Folder);

    public async Task UploadAsync(IStorageProvider provider, RemotePath path, byte[] payload)
    {
        await using var source = new MemoryStream(payload, writable: false);
        var result = await provider.UploadAsync(path, source, source.Length);
        AssertSuccess(result, $"Upload {path.Value}");
    }

    public async Task<byte[]> DownloadAsync(IStorageProvider provider, RemotePath path)
    {
        await using var destination = new MemoryStream();
        var result = await provider.DownloadAsync(path, destination);
        AssertSuccess(result, $"Download {path.Value}");
        return destination.ToArray();
    }

    public async Task CreateAndDeleteTemporaryBucketAsync()
    {
        var bucketName = $"atombox-e2e-{DateTimeOffset.UtcNow:yyyyMMddhhmmss}-{Guid.NewGuid():N}"[..55];
        await using (var provider = await CreateProviderAsync())
        {
            var create = await provider.CreateFolderAsync(new RemotePath(bucketName, RemotePathKind.BucketRoot));
            AssertSuccess(create, "Create bucket through AtomBox provider");
            var list = await provider.ListAsync(RemotePath.Root);
            AssertSuccess(list, "List buckets after bucket creation");
            Assert.Contains(list.GetValueOrThrow(), item => item.Name == bucketName);
        }

        using var client = CreateAmazonClient();
        await client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucketName });
    }

    public async Task WriteEvidenceManifestAsync(IReadOnlyDictionary<string, object?> evidence)
    {
        var manifest = new Dictionary<string, object?>(evidence)
        {
            ["runId"] = RunId,
            ["endpoint"] = Environment.Endpoint,
            ["region"] = Environment.Region,
            ["bucket"] = Environment.Bucket,
            ["prefix"] = RunPrefix,
            ["completedAtUtc"] = DateTimeOffset.UtcNow
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true });
        await using var provider = await CreateProviderAsync();
        await UploadAsync(provider, ObjectPath("_evidence/manifest.json"), payload);
        await WriteLocalReportAsync("completed", manifest);
    }

    public AmazonS3Client CreateAmazonClient()
    {
        return new AmazonS3Client(
            new BasicAWSCredentials(Environment.AccessKeyId, Environment.AccessKeySecret),
            new AmazonS3Config
            {
                ServiceURL = Environment.Endpoint,
                AuthenticationRegion = Environment.Region,
                ForcePathStyle = true
            });
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        if (Directory.Exists(_temporaryStateRoot))
        {
            Directory.Delete(_temporaryStateRoot, recursive: true);
        }
    }

    private async Task WriteLocalReportAsync(string status, IReadOnlyDictionary<string, object?> details)
    {
        var report = new Dictionary<string, object?>(details)
        {
            ["status"] = status,
            ["runId"] = RunId,
            ["endpoint"] = Environment.Endpoint,
            ["region"] = Environment.Region,
            ["bucket"] = Environment.Bucket,
            ["prefix"] = RunPrefix,
            ["updatedAtUtc"] = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(ArtifactRoot, "report.json"), json, Encoding.UTF8);
    }

    internal static void AssertSuccess(OperationResult result, string operation)
    {
        Assert.True(result.IsSuccess, FormatError(operation, result.Error));
    }

    internal static void AssertSuccess<T>(OperationResult<T> result, string operation)
    {
        Assert.True(result.IsSuccess, FormatError(operation, result.Error));
    }

    private static string FormatError(string operation, AtomBox.Core.Errors.StorageError? error)
    {
        return error is null
            ? $"{operation} failed."
            : $"{operation} failed. Category={error.Category}; Code={error.Code}; ProviderCode={error.ProviderErrorCode}; Retryable={error.IsRetryable}; Message={error.Message}";
    }
}

internal sealed record GenericS3EndToEndEnvironment(
    string Endpoint,
    string Region,
    string Bucket,
    string AccessKeyId,
    string AccessKeySecret,
    string TestPrefix)
{
    public static GenericS3EndToEndEnvironment? TryRead()
    {
        if (!string.Equals(System.Environment.GetEnvironmentVariable("ATOMBOX_TEST_GENERIC_S3"), "1", StringComparison.Ordinal))
        {
            return null;
        }

        var endpoint = System.Environment.GetEnvironmentVariable("ATOMBOX_GENERIC_S3_ENDPOINT");
        var region = System.Environment.GetEnvironmentVariable("ATOMBOX_GENERIC_S3_REGION");
        var bucket = System.Environment.GetEnvironmentVariable("ATOMBOX_GENERIC_S3_BUCKET");
        var accessKeyId = System.Environment.GetEnvironmentVariable("ATOMBOX_GENERIC_S3_ACCESS_KEY_ID");
        var accessKeySecret = System.Environment.GetEnvironmentVariable("ATOMBOX_GENERIC_S3_ACCESS_KEY_SECRET");
        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(region) ||
            string.IsNullOrWhiteSpace(bucket) ||
            string.IsNullOrWhiteSpace(accessKeyId) ||
            string.IsNullOrWhiteSpace(accessKeySecret))
        {
            return null;
        }

        var prefix = System.Environment.GetEnvironmentVariable("ATOMBOX_GENERIC_S3_TEST_PREFIX");
        prefix = string.IsNullOrWhiteSpace(prefix) ? "atombox/v1.0.1" : prefix.Trim();
        prefix = prefix.Trim('/').Replace('\\', '/') + "/";
        if (!prefix.Contains("atombox", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Generic S3 E2E test prefix must contain 'atombox'.");
        }

        return new GenericS3EndToEndEnvironment(
            endpoint.Trim(),
            region.Trim(),
            bucket.Trim(),
            accessKeyId.Trim(),
            accessKeySecret.Trim(),
            prefix);
    }
}
