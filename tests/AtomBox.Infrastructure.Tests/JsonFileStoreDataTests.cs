using System.Text.Json;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class JsonFileStoreDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));
    private static readonly CancellationToken CT = CancellationToken.None;

    [Fact]
    public async Task ReadThenWrite_PreservesExistingBackup()
    {
        var path = Path.Combine(_root, "backup-preserve.json");
        var store = new JsonFileStore<string>(path);
        await store.WriteAsync("first", CT);
        await store.WriteAsync("second", CT);

        Assert.True(File.Exists($"{path}.bak"));
        var backupContent = await File.ReadAllTextAsync($"{path}.bak");
        Assert.Contains("first", backupContent);
    }

    [Fact]
    public async Task CorruptFile_BacksUpWithTimestamp()
    {
        var path = Path.Combine(_root, "corrupt-timestamp.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "{ invalid }");

        var store = new JsonFileStore<Dictionary<string, string>>(path);
        var result = await store.ReadAsync(new Dictionary<string, string>(), CT);
        Assert.True(result.IsFailure);

        var corruptFiles = Directory.GetFiles(_root, "corrupt-timestamp.json.*.corrupt");
        Assert.Single(corruptFiles);
    }

    [Fact]
    public async Task EmptyList_Roundtrips()
    {
        var path = Path.Combine(_root, "empty-list.json");
        var store = new JsonFileStore<List<string>>(path);
        await store.WriteAsync([], CT);

        var raw = await File.ReadAllTextAsync(path);
        Assert.Equal("[]", raw.Trim());

        var result = await store.ReadAsync(["fallback"], CT);
        Assert.Empty(result.GetValueOrThrow());
    }

    [Fact]
    public async Task LargeDataSet_Roundtrips()
    {
        var path = Path.Combine(_root, "large.json");
        var store = new JsonFileStore<List<int>>(path);
        var data = Enumerable.Range(0, 5000).ToList();

        await store.WriteAsync(data, CT);

        var fileInfo = new FileInfo(path);
        Assert.True(fileInfo.Length > 10_000, $"File should be substantial, was {fileInfo.Length} bytes");

        var result = await store.ReadAsync([], CT);
        Assert.True(result.IsSuccess);
        Assert.Equal(5000, result.GetValueOrThrow().Count);
        Assert.Equal(4999, result.GetValueOrThrow()[^1]);
    }

    [Fact]
    public async Task UnicodeCharacters_ArePreserved()
    {
        var path = Path.Combine(_root, "unicode.json");
        var store = new JsonFileStore<Dictionary<string, string>>(path);
        var data = new Dictionary<string, string>
        {
            ["emoji"] = "🔥",
            ["chinese"] = "你好世界",
            ["japanese"] = "こんにちは",
            ["mixed"] = "aβγΔ🌟z"
        };

        await store.WriteAsync(data, CT);
        var result = await store.ReadAsync(new Dictionary<string, string>(), CT);
        Assert.True(result.IsSuccess);
        Assert.Equal("🔥", result.GetValueOrThrow()["emoji"]);
        Assert.Equal("你好世界", result.GetValueOrThrow()["chinese"]);
        Assert.Equal("こんにちは", result.GetValueOrThrow()["japanese"]);
        Assert.Equal("aβγΔ🌟z", result.GetValueOrThrow()["mixed"]);
    }

    [Fact]
    public async Task MultipleConsecutiveWrites_AllPersisted()
    {
        var path = Path.Combine(_root, "consecutive.json");
        var store = new JsonFileStore<List<int>>(path);

        for (var i = 1; i <= 10; i++)
        {
            await store.WriteAsync(Enumerable.Range(1, i).ToList(), CT);
        }

        var result = await store.ReadAsync([], CT);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], result.GetValueOrThrow());
    }

    [Fact]
    public async Task ConcurrentWrites_AllDataIntact()
    {
        var path = Path.Combine(_root, "concurrent-stress.json");
        var store = new JsonFileStore<List<int>>(path);

        var tasks = Enumerable.Range(1, 50).Select(i =>
            store.WriteAsync(Enumerable.Range(1, i).ToList(), CT));
        await Task.WhenAll(tasks);

        var result = await store.ReadAsync([], CT);
        Assert.True(result.IsSuccess);
        var data = result.GetValueOrThrow();
        Assert.NotEmpty(data);
        Assert.Equal(data.Count, data[^1]);
    }

    [Fact]
    public async Task ReadAfterWrite_ReturnsCompleteData()
    {
        var path = Path.Combine(_root, "read-after-write.json");
        var store = new JsonFileStore<List<int>>(path);

        var expected = Enumerable.Range(0, 1000).ToList();
        await store.WriteAsync(expected, CT);
        var result = await store.ReadAsync([], CT);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected.Count, result.GetValueOrThrow().Count);
        Assert.Equal(expected[^1], result.GetValueOrThrow()[^1]);
    }

    [Fact]
    public async Task BackupFile_IsValidJson()
    {
        var path = Path.Combine(_root, "backup-valid.json");
        var store = new JsonFileStore<string>(path);
        await store.WriteAsync("original", CT);
        await store.WriteAsync("updated", CT);

        var backupContent = await File.ReadAllTextAsync($"{path}.bak");
        var parsed = JsonSerializer.Deserialize<JsonElement>(backupContent);
        Assert.Equal("original", parsed.GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
