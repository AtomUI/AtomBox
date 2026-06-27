using AtomBox.Core.Credentials;
using AtomBox.Core.Errors;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Credentials;

namespace AtomBox.Infrastructure.Tests;

public sealed class CredentialStoreContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));
    private readonly CredentialStore _store;

    public CredentialStoreContractTests()
    {
        var paths = new AtomBoxStoragePaths(_root);
        _store = new CredentialStore(paths, new CredentialProtectionService(paths));
    }

    [Fact]
    public async Task SaveAndAcquireMaterial_Roundtrips()
    {
        var material = new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["accessKeyId"] = "ak-123",
            ["accessKeySecret"] = "secret-456"
        });

        var saveResult = await _store.SaveAsync(material);
        Assert.True(saveResult.IsSuccess);
        var credentialRef = saveResult.GetValueOrThrow();

        await using var materialLease = (await _store.AcquireMaterialAsync(credentialRef)).GetValueOrThrow();
        Assert.Equal("ak-123", materialLease.Material.GetRequiredValue("accessKeyId"));
        Assert.Equal("secret-456", materialLease.Material.GetRequiredValue("accessKeySecret"));
    }

    [Fact]
    public async Task AcquireLease_NonExistentReturnsNotFound()
    {
        var result = await _store.AcquireLeaseAsync(new CredentialRef("missing"));
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task AcquireMaterial_NonExistentReturnsNotFound()
    {
        var result = await _store.AcquireMaterialAsync(new CredentialRef("ghost"));
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task Exists_AfterSave_ReturnsTrue()
    {
        var credentialRef = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["key"] = "value"
        }))).GetValueOrThrow();

        var exists = await _store.ExistsAsync(credentialRef);
        Assert.True(exists.IsSuccess);
        Assert.True(exists.GetValueOrThrow());
    }

    [Fact]
    public async Task Exists_NonExistent_ReturnsFalse()
    {
        var result = await _store.ExistsAsync(new CredentialRef("nope"));
        Assert.True(result.IsSuccess);
        Assert.False(result.GetValueOrThrow());
    }

    [Fact]
    public async Task MarkPendingDelete_ExistingCredential_Succeeds()
    {
        var credentialRef = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["token"] = "value"
        }))).GetValueOrThrow();

        var markResult = await _store.MarkPendingDeleteAsync(credentialRef);
        Assert.True(markResult.IsSuccess);
    }

    [Fact]
    public async Task MarkPendingDelete_NonExistent_ReturnsNotFound()
    {
        var result = await _store.MarkPendingDeleteAsync(new CredentialRef("not-here"));
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task AcquireMaterial_PendingDelete_ReturnsNotFound()
    {
        var credentialRef = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["token"] = "value"
        }))).GetValueOrThrow();
        await _store.MarkPendingDeleteAsync(credentialRef);

        var result = await _store.AcquireMaterialAsync(credentialRef);
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task AcquireLease_PendingDelete_ReturnsNotFound()
    {
        var credentialRef = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["token"] = "value"
        }))).GetValueOrThrow();
        await _store.MarkPendingDeleteAsync(credentialRef);

        var result = await _store.AcquireLeaseAsync(credentialRef);

        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task ExcludesPendingDeleteCredentialFromLeaseCheck()
    {
        var credentialRef = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["k"] = "v"
        }))).GetValueOrThrow();
        await _store.MarkPendingDeleteAsync(credentialRef);

        var exists = await _store.ExistsAsync(credentialRef);
        Assert.True(exists.IsSuccess);
        Assert.False(exists.GetValueOrThrow());
    }

    [Fact]
    public async Task Lease_Dispose_ReleasesActiveLease()
    {
        var credentialRef = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["k"] = "v"
        }))).GetValueOrThrow();

        var lease = (await _store.AcquireLeaseAsync(credentialRef)).GetValueOrThrow();

        Assert.True(_store.HasActiveLease(credentialRef));

        await lease.DisposeAsync();

        Assert.False(_store.HasActiveLease(credentialRef));
    }

    [Fact]
    public async Task AcquireLease_DoesNotInterleavePendingDeleteBeforeLeaseCreation()
    {
        var credentialRef = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["k"] = "v"
        }))).GetValueOrThrow();

        var enteredHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.BeforeCredentialLeaseCreatedAsync = async (_, _) =>
        {
            enteredHook.SetResult();
            await continueHook.Task;
        };

        var acquireTask = _store.AcquireLeaseAsync(credentialRef);
        await enteredHook.Task;

        var markTask = _store.MarkPendingDeleteAsync(credentialRef);
        var completedBeforeLease = await Task.WhenAny(markTask, Task.Delay(TimeSpan.FromMilliseconds(50))) == markTask;

        continueHook.SetResult();
        var leaseResult = await acquireTask;
        await markTask;

        Assert.False(completedBeforeLease);
        Assert.True(leaseResult.IsSuccess);
        await leaseResult.GetValueOrThrow().DisposeAsync();
    }

    [Fact]
    public async Task Lease_DisposeMultipleTimes_IsIdempotent()
    {
        var credentialRef = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["k"] = "v"
        }))).GetValueOrThrow();

        var lease = (await _store.AcquireLeaseAsync(credentialRef)).GetValueOrThrow();
        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.False(_store.HasActiveLease(credentialRef));
    }

    [Fact]
    public async Task MultipleLeases_SameCredential_TracksCount()
    {
        var credentialRef = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["k"] = "v"
        }))).GetValueOrThrow();

        var lease1 = (await _store.AcquireLeaseAsync(credentialRef)).GetValueOrThrow();
        var lease2 = (await _store.AcquireLeaseAsync(credentialRef)).GetValueOrThrow();
        Assert.True(_store.HasActiveLease(credentialRef));

        await lease1.DisposeAsync();
        Assert.True(_store.HasActiveLease(credentialRef));

        await lease2.DisposeAsync();
        Assert.False(_store.HasActiveLease(credentialRef));
    }

    [Fact]
    public async Task Save_DoesNotWritePlaintextToIndex()
    {
        await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["password"] = "super-secret-plaintext"
        }));

        var paths = new AtomBoxStoragePaths(_root);
        var indexContent = await File.ReadAllTextAsync(paths.CredentialIndexFile);

        Assert.DoesNotContain("super-secret-plaintext", indexContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultipleAcquireMaterial_CanBeDisposedIndependently()
    {
        var credentialRef = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["key"] = "val"
        }))).GetValueOrThrow();

        var lease1 = (await _store.AcquireMaterialAsync(credentialRef)).GetValueOrThrow();
        var lease2 = (await _store.AcquireMaterialAsync(credentialRef)).GetValueOrThrow();

        Assert.Equal("val", lease1.Material.GetRequiredValue("key"));
        Assert.Equal("val", lease2.Material.GetRequiredValue("key"));

        await lease1.DisposeAsync();
        await lease2.DisposeAsync();

        Assert.False(_store.HasActiveLease(credentialRef));
    }

    [Fact]
    public async Task AcquireMaterial_DoesNotInterleavePendingDeleteBeforeLeaseCreation()
    {
        var credentialRef = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["key"] = "val"
        }))).GetValueOrThrow();

        var enteredHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueHook = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.BeforeCredentialLeaseCreatedAsync = async (_, _) =>
        {
            enteredHook.SetResult();
            await continueHook.Task;
        };

        var acquireTask = _store.AcquireMaterialAsync(credentialRef);
        await enteredHook.Task;

        var markTask = _store.MarkPendingDeleteAsync(credentialRef);
        var completedBeforeLease = await Task.WhenAny(markTask, Task.Delay(TimeSpan.FromMilliseconds(50))) == markTask;

        continueHook.SetResult();
        var materialResult = await acquireTask;
        await markTask;

        Assert.False(completedBeforeLease);
        Assert.True(materialResult.IsSuccess);

        await using var materialLease = materialResult.GetValueOrThrow();
        Assert.Equal("val", materialLease.Material.GetRequiredValue("key"));
    }

    [Fact]
    public async Task MultipleCredentials_AreIndependent()
    {
        var ref1 = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["key"] = "value-1"
        }))).GetValueOrThrow();

        var ref2 = (await _store.SaveAsync(new CredentialSecretMaterial(new Dictionary<string, string>
        {
            ["key"] = "value-2"
        }))).GetValueOrThrow();

        await using var lease1 = (await _store.AcquireMaterialAsync(ref1)).GetValueOrThrow();
        await using var lease2 = (await _store.AcquireMaterialAsync(ref2)).GetValueOrThrow();

        Assert.Equal("value-1", lease1.Material.GetRequiredValue("key"));
        Assert.Equal("value-2", lease2.Material.GetRequiredValue("key"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
