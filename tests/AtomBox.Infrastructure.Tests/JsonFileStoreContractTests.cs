using System.Text.Json;
using AtomBox.Core.Errors;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class JsonFileStoreContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));
    private static readonly CancellationToken CT = CancellationToken.None;

    public JsonFileStoreContractTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Read_FileDoesNotExist_ReturnsDefaultValue()
    {
        var store = new JsonFileStore<List<string>>(Path.Combine(_root, "missing.json"));
        var result = await store.ReadAsync(["default"], CT);
        Assert.True(result.IsSuccess);
        Assert.Equal(["default"], result.GetValueOrThrow());
    }

    [Fact]
    public async Task WriteThenRead_Roundtrips()
    {
        var path = Path.Combine(_root, "data.json");
        var store = new JsonFileStore<List<int>>(path);
        var value = new List<int> { 1, 2, 3 };

        var writeResult = await store.WriteAsync(value, CT);
        Assert.True(writeResult.IsSuccess);

        var readResult = await store.ReadAsync([], CT);
        Assert.True(readResult.IsSuccess);
        Assert.Equal([1, 2, 3], readResult.GetValueOrThrow());
    }

    [Fact]
    public async Task Read_CorruptJson_ReturnsFailureAndCreatesCorruptBackup()
    {
        var path = Path.Combine(_root, "corrupt.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "{ not valid json }");

        var store = new JsonFileStore<Dictionary<string, string>>(path);
        var result = await store.ReadAsync(new Dictionary<string, string>(), CT);
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Infrastructure, result.Error?.Category);
        Assert.True(Directory.GetFiles(_root, "corrupt.json.*.corrupt").Any());
    }

    [Fact]
    public async Task Write_CreatesDirectory()
    {
        var nested = Path.Combine(_root, "a", "b", "c", "nested.json");
        var store = new JsonFileStore<string>(nested);

        var result = await store.WriteAsync("hello", CT);
        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public async Task Write_CreatesBackup_WhenOverwriting()
    {
        var path = Path.Combine(_root, "backup.json");
        var store = new JsonFileStore<string>(path);
        await store.WriteAsync("first", CT);
        await store.WriteAsync("second", CT);

        Assert.True(File.Exists(path));
        Assert.True(File.Exists($"{path}.bak"));
    }

    [Fact]
    public async Task Write_ReplacesFileContent()
    {
        var path = Path.Combine(_root, "replace.json");
        var store = new JsonFileStore<string>(path);
        await store.WriteAsync("old-value", CT);

        await store.WriteAsync("new-value", CT);

        var result = await store.ReadAsync("", CT);
        Assert.Equal("new-value", result.GetValueOrThrow());
    }

    [Fact]
    public async Task ConcurrentReads_AreSafe()
    {
        var path = Path.Combine(_root, "concurrent.json");
        var store = new JsonFileStore<int>(path);
        await store.WriteAsync(42, CT);

        var tasks = Enumerable.Range(0, 10).Select(_ => store.ReadAsync(0, CT));
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(42, r.GetValueOrThrow()));
    }

    [Fact]
    public async Task ConcurrentWriteRead_IsSafe()
    {
        var path = Path.Combine(_root, "concurrent2.json");
        var store = new JsonFileStore<int>(path);

        var writes = Enumerable.Range(0, 10).Select(i => store.WriteAsync(i, CT));
        await Task.WhenAll(writes);

        var result = await store.ReadAsync(-1, CT);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Read_NonExistentDirectory_ReturnsDefaultValue()
    {
        var path = Path.Combine(_root, "nonexistent", "file.json");
        var store = new JsonFileStore<string>(path);

        var result = await store.ReadAsync("fallback", CT);
        Assert.True(result.IsSuccess);
        Assert.Equal("fallback", result.GetValueOrThrow());
    }

    [Fact]
    public async Task Write_EmptyJsonObject_SucceedsAndCanReadBack()
    {
        var path = Path.Combine(_root, "empty.json");
        var store = new JsonFileStore<Dictionary<string, string>>(path);

        await store.WriteAsync(new Dictionary<string, string>(), CT);
        var result = await store.ReadAsync(new Dictionary<string, string>(), CT);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.GetValueOrThrow());
    }

    [Fact]
    public async Task Read_EmptyJsonArray_ReturnsEmptyList()
    {
        var path = Path.Combine(_root, "empty-array.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "[]");

        var store = new JsonFileStore<List<string>>(path);
        var result = await store.ReadAsync(["fallback"], CT);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.GetValueOrThrow());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
