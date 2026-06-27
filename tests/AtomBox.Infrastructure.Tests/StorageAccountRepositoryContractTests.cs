using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.ValueObjects;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class StorageAccountRepositoryContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));
    private readonly StorageAccountRepository _repo;

    public StorageAccountRepositoryContractTests()
    {
        _repo = new StorageAccountRepository(new AtomBoxStoragePaths(_root));
    }

    [Fact]
    public async Task List_WhenEmpty_ReturnsEmptyList()
    {
        var result = await _repo.ListAsync();
        Assert.True(result.IsSuccess);
        Assert.Empty(result.GetValueOrThrow());
    }

    [Fact]
    public async Task AddAndGetById_Roundtrips()
    {
        var account = CreateAccount("ak-1");
        var addResult = await _repo.AddAsync(account);
        Assert.True(addResult.IsSuccess);

        var getResult = await _repo.GetByIdAsync(account.Id);
        Assert.True(getResult.IsSuccess);
        Assert.Equal(account.Id, getResult.GetValueOrThrow().Id);
        Assert.Equal("ak-1", getResult.GetValueOrThrow().GetProviderConfigValue("key"));
    }

    [Fact]
    public async Task Add_DuplicateId_ReturnsConflict()
    {
        var account = CreateAccount("dup");
        await _repo.AddAsync(account);
        var dupResult = await _repo.AddAsync(account);
        Assert.True(dupResult.IsFailure);
        Assert.Equal(StorageErrorCategory.Conflict, dupResult.Error?.Category);
    }

    [Fact]
    public async Task List_ReturnsAllAddedAccounts()
    {
        var a1 = CreateAccount("a1", "provider-1");
        var a2 = CreateAccount("a2", "provider-2");
        await _repo.AddAsync(a1);
        await _repo.AddAsync(a2);

        var listResult = await _repo.ListAsync();
        Assert.True(listResult.IsSuccess);
        Assert.Equal(2, listResult.GetValueOrThrow().Count);
    }

    [Fact]
    public async Task Update_ExistingAccount_PersistsChanges()
    {
        var account = CreateAccount("to-update");
        await _repo.AddAsync(account);

        var updated = account.UpdateConfiguration(
            displayName: account.DisplayName,
            endpoint: "new-endpoint.com",
            region: account.Region,
            credentialRef: account.CredentialRef,
            updatedAt: DateTimeOffset.UtcNow);
        var updateResult = await _repo.UpdateAsync(updated);
        Assert.True(updateResult.IsSuccess);

        var getResult = await _repo.GetByIdAsync(account.Id);
        Assert.True(getResult.IsSuccess);
        Assert.Equal("new-endpoint.com", getResult.GetValueOrThrow().Endpoint);
    }

    [Fact]
    public async Task Update_NonExistentAccount_ReturnsNotFound()
    {
        var account = CreateAccount("ghost");
        var result = await _repo.UpdateAsync(account);
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task Delete_ExistingAccount_RemovesIt()
    {
        var account = CreateAccount("to-delete");
        await _repo.AddAsync(account);

        var delResult = await _repo.DeleteAsync(account.Id);
        Assert.True(delResult.IsSuccess);

        var getResult = await _repo.GetByIdAsync(account.Id);
        Assert.True(getResult.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, getResult.Error?.Category);
    }

    [Fact]
    public async Task Delete_NonExistentAccount_ReturnsNotFound()
    {
        var result = await _repo.DeleteAsync(StorageAccountId.New());
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task GetById_NonExistentAccount_ReturnsNotFound()
    {
        var result = await _repo.GetByIdAsync(StorageAccountId.New());
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.NotFound, result.Error?.Category);
    }

    [Fact]
    public async Task AddThenDeleteThenAddSameId_Succeeds()
    {
        var account = CreateAccount("reuse");
        await _repo.AddAsync(account);
        await _repo.DeleteAsync(account.Id);

        var reAddResult = await _repo.AddAsync(account);
        Assert.True(reAddResult.IsSuccess);

        var getResult = await _repo.GetByIdAsync(account.Id);
        Assert.True(getResult.IsSuccess);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static StorageAccount CreateAccount(string key, string providerId = "test-provider")
    {
        var now = DateTimeOffset.UtcNow;
        return new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId(providerId),
            "Test",
            "endpoint.com",
            "region-1",
            new CredentialRef("cred-1"),
            now,
            now,
            new Dictionary<string, string> { ["key"] = key });
    }
}
