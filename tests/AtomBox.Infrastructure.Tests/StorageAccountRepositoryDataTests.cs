using System.Text.Json;
using AtomBox.Core.Accounts;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class StorageAccountRepositoryDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AccountsFile_IsValidJsonArray()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        await repo.AddAsync(CreateAccount("test-key"));

        var raw = await File.ReadAllTextAsync(paths.AccountsFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
    }

    [Fact]
    public async Task AccountsFile_PersistsEndpointAndRegion()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        await repo.AddAsync(CreateAccount("key", endpoint: "custom.endpoint.com", region: "us-east-1"));

        var raw = await File.ReadAllTextAsync(paths.AccountsFile);
        Assert.Contains("custom.endpoint.com", raw);
        Assert.Contains("us-east-1", raw);
    }

    [Fact]
    public async Task AccountsFile_ProviderConfigExcludesEndpointAndRegion()
    {
        var now = DateTimeOffset.UtcNow;
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("test"),
            "Test",
            "ep.com",
            "region-1",
            new CredentialRef("cred-1"),
            now,
            now,
            new Dictionary<string, string>
            {
                ["bucket"] = "my-bucket",
                ["endpoint"] = "should-be-excluded",
                ["region"] = "should-also-be-excluded"
            });
        await repo.AddAsync(account);

        var read = (await repo.GetByIdAsync(account.Id)).GetValueOrThrow();
        Assert.Equal("ep.com", read.Endpoint);
        Assert.Equal("region-1", read.Region);
        Assert.Equal("my-bucket", read.GetProviderConfigValue("bucket"));

        var raw = await File.ReadAllTextAsync(paths.AccountsFile);
        var entries = JsonSerializer.Deserialize<JsonElement>(raw);
        var config = entries[0].GetProperty("providerConfig");
        Assert.True(config.TryGetProperty("bucket", out _));
        Assert.False(config.TryGetProperty("endpoint", out _));
        Assert.False(config.TryGetProperty("region", out _));
    }

    [Fact]
    public async Task Update_ModifiesFieldsInPlace()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var account = CreateAccount("orig");
        await repo.AddAsync(account);

        var updated = account.UpdateConfiguration(
            "Updated Name",
            "new-ep.com",
            "new-region",
            account.CredentialRef,
            DateTimeOffset.UtcNow);
        await repo.UpdateAsync(updated);

        var raw = await File.ReadAllTextAsync(paths.AccountsFile);
        var entries = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Single(entries.EnumerateArray());
        Assert.Equal("Updated Name", entries[0].GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task DisplayName_IsTrimmed()
    {
        var now = DateTimeOffset.UtcNow;
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("test"),
            "  Spacey Name  ",
            "ep.com",
            null,
            new CredentialRef("cred-1"),
            now,
            now);
        await repo.AddAsync(account);

        var read = (await repo.GetByIdAsync(account.Id)).GetValueOrThrow();
        Assert.Equal("Spacey Name", read.DisplayName);
    }

    [Fact]
    public async Task ProviderConfig_EmptyDict_StoredAsEmptyObject()
    {
        var now = DateTimeOffset.UtcNow;
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new StorageAccountRepository(paths);
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("test"),
            "NoConfig",
            null,
            null,
            new CredentialRef("cred-1"),
            now,
            now,
            new Dictionary<string, string>());
        await repo.AddAsync(account);

        var raw = await File.ReadAllTextAsync(paths.AccountsFile);
        var entries = JsonSerializer.Deserialize<JsonElement>(raw);
        var config = entries[0].GetProperty("providerConfig");
        Assert.Empty(config.EnumerateObject());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static StorageAccount CreateAccount(string key, string? endpoint = null, string? region = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("test-provider"),
            "Test",
            endpoint ?? "endpoint.com",
            region,
            new CredentialRef("cred-1"),
            now,
            now,
            new Dictionary<string, string> { ["key"] = key });
    }
}
