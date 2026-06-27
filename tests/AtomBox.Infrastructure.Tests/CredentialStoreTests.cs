using AtomBox.Core.Credentials;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Credentials;

namespace AtomBox.Infrastructure.Tests;

public sealed class CredentialStoreTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), "AtomBox.Infrastructure.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AcquireLeaseAsync_ReturnsNotFound_WhenCredentialDoesNotExist()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.CredentialDirectory);
        var store = CreateStore(paths);

        var result = await store.AcquireLeaseAsync(new CredentialRef("missing"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SaveAndAcquireMaterialAsync_RoundTripsProtectedCredential()
    {
        var paths = CreatePaths();
        var store = CreateStore(paths);

        var saveResult = await store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["accessKeyId"] = "ak-test",
            ["accessKeySecret"] = "secret-test"
        }));

        var credentialRef = saveResult.GetValueOrThrow();
        await using var materialLease = (await store.AcquireMaterialAsync(credentialRef)).GetValueOrThrow();

        Assert.Equal("ak-test", materialLease.Material.GetRequiredValue("accessKeyId"));
        Assert.Equal("secret-test", materialLease.Material.GetRequiredValue("accessKeySecret"));
    }

    [Fact]
    public async Task SaveAsync_DoesNotWritePlaintextSecretToCredentialIndex()
    {
        var paths = CreatePaths();
        var store = CreateStore(paths);

        await store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["password"] = "plain-password"
        }));

        var credentialIndex = await File.ReadAllTextAsync(paths.CredentialIndexFile);

        Assert.DoesNotContain("plain-password", credentialIndex, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcquireMaterialAsync_ReturnsNotFound_WhenCredentialIsPendingDelete()
    {
        var store = CreateStore(CreatePaths());
        var credentialRef = (await store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["token"] = "token-value"
        }))).GetValueOrThrow();

        await store.MarkPendingDeleteAsync(credentialRef);
        var result = await store.AcquireMaterialAsync(credentialRef);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task MaterialLease_ReleasesActiveLease_WhenDisposed()
    {
        var store = CreateStore(CreatePaths());
        var credentialRef = (await store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["token"] = "token-value"
        }))).GetValueOrThrow();

        var lease = (await store.AcquireMaterialAsync(credentialRef)).GetValueOrThrow();

        Assert.True(store.HasActiveLease(credentialRef));

        await lease.DisposeAsync();

        Assert.False(store.HasActiveLease(credentialRef));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private AtomBoxStoragePaths CreatePaths()
    {
        return new AtomBoxStoragePaths(_rootDirectory);
    }

    private static CredentialStore CreateStore(AtomBoxStoragePaths paths)
    {
        return new CredentialStore(paths, new CredentialProtectionService(paths));
    }
}
