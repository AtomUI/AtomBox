using System.Text;
using System.Text.Json;
using AtomBox.Core.Credentials;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Credentials;

namespace AtomBox.Infrastructure.Tests;

public sealed class CredentialStoreDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CredentialKeyFile_IsExactly32Bytes()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var service = new CredentialProtectionService(paths);

        service.Protect([0x01]);

        var keyBytes = await File.ReadAllBytesAsync(paths.CredentialKeyFile);
        Assert.Equal(32, keyBytes.Length);
    }

    [Fact]
    public async Task CredentialKeyFile_IsPersistedBetweenInstances()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var service1 = new CredentialProtectionService(paths);
        var protected1 = service1.Protect([0x01, 0x02]);

        var service2 = new CredentialProtectionService(paths);
        var unprotected = service2.Unprotect(protected1);

        Assert.Equal([0x01, 0x02], unprotected);
    }

    [Fact]
    public async Task ProtectedPayload_StartsWithAesgcmPrefix()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var service = new CredentialProtectionService(paths);
        var result = service.Protect([0x00]);
        Assert.StartsWith("aesgcm-v1:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtectedPayload_IsValidBase64AfterPrefix()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var service = new CredentialProtectionService(paths);
        var result = service.Protect([0x00]);

        var base64Part = result["aesgcm-v1:".Length..];
        var bytes = Convert.FromBase64String(base64Part);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void ProtectedPayload_HasMinimumLength()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var service = new CredentialProtectionService(paths);
        var result = service.Protect(new byte[256]);

        var base64Part = result["aesgcm-v1:".Length..];
        var bytes = Convert.FromBase64String(base64Part);
        Assert.True(bytes.Length >= 12 + 16, $"Expected at least nonce(12)+tag(16) bytes, got {bytes.Length}");
    }

    [Fact]
    public async Task CredentialIndexFile_IsValidJson()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new CredentialStore(paths, new CredentialProtectionService(paths));

        await store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["key"] = "value"
        }));

        var raw = await File.ReadAllTextAsync(paths.CredentialIndexFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
        Assert.True(parsed.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task CredentialIndex_PayloadField_IsProtectedString()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new CredentialStore(paths, new CredentialProtectionService(paths));

        await store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["accessKey"] = "my-plaintext-key"
        }));

        var raw = await File.ReadAllTextAsync(paths.CredentialIndexFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);

        foreach (var entry in parsed.EnumerateArray())
        {
            var payload = entry.GetProperty("protectedPayload").GetString();
            Assert.NotNull(payload);
            Assert.StartsWith("aesgcm-v1:", payload, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("my-plaintext-key", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CredentialIndex_ContainsExpectedFields()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new CredentialStore(paths, new CredentialProtectionService(paths));
        var ref1 = (await store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["k"] = "v"
        }))).GetValueOrThrow();

        var raw = await File.ReadAllTextAsync(paths.CredentialIndexFile);
        var entries = JsonSerializer.Deserialize<JsonElement>(raw);

        var entry = entries.EnumerateArray().First(e =>
            e.GetProperty("credentialRef").GetString() == ref1.Value);

        Assert.Equal("credentialRef", entries[0].EnumerateObject().First().Name);
        Assert.Equal(JsonValueKind.String, entry.GetProperty("credentialRef").ValueKind);
        Assert.Equal(JsonValueKind.String, entry.GetProperty("protectedPayload").ValueKind);
        Assert.Equal(JsonValueKind.False, entry.GetProperty("pendingDelete").ValueKind);
        Assert.Equal(JsonValueKind.String, entry.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task MarkPendingDelete_SetsFlagToTrue()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new CredentialStore(paths, new CredentialProtectionService(paths));
        var credentialRef = (await store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["k"] = "v"
        }))).GetValueOrThrow();

        await store.MarkPendingDeleteAsync(credentialRef);

        var raw = await File.ReadAllTextAsync(paths.CredentialIndexFile);
        var entries = JsonSerializer.Deserialize<JsonElement>(raw);
        var entry = entries.EnumerateArray().First(e =>
            e.GetProperty("credentialRef").GetString() == credentialRef.Value);
        Assert.True(entry.GetProperty("pendingDelete").GetBoolean());
    }

    [Fact]
    public async Task MultipleCredentials_AreIndependentInIndex()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var store = new CredentialStore(paths, new CredentialProtectionService(paths));
        var ref1 = (await store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string> { ["k1"] = "v1" }))).GetValueOrThrow();
        var ref2 = (await store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string> { ["k2"] = "v2" }))).GetValueOrThrow();

        var raw = await File.ReadAllTextAsync(paths.CredentialIndexFile);
        var entries = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(2, entries.GetArrayLength());

        var refs = entries.EnumerateArray().Select(e => e.GetProperty("credentialRef").GetString()).ToHashSet();
        Assert.Contains(ref1.Value, refs);
        Assert.Contains(ref2.Value, refs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
