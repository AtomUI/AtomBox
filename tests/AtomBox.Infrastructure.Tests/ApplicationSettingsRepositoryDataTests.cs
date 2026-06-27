using System.Text.Json;
using AtomBox.Core.Settings;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class ApplicationSettingsRepositoryDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SettingsFile_ContainsCorrectValues()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new ApplicationSettingsRepository(paths);
        await repo.SaveAsync(new ApplicationSettings(7, TransferOverwritePolicy.Rename, true));

        var raw = await File.ReadAllTextAsync(paths.SettingsFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(7, parsed.GetProperty("defaultConcurrency").GetInt32());
        Assert.Equal(3, parsed.GetProperty("defaultOverwritePolicy").GetInt32());
        Assert.True(parsed.GetProperty("keepCompletedTransfers").GetBoolean());
    }

    [Fact]
    public async Task BackupFile_HasPreviousContent()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new ApplicationSettingsRepository(paths);
        await repo.SaveAsync(new ApplicationSettings(3, TransferOverwritePolicy.Ask, true));
        await repo.SaveAsync(new ApplicationSettings(10, TransferOverwritePolicy.Overwrite, false));

        var backupRaw = await File.ReadAllTextAsync($"{paths.SettingsFile}.bak");
        var parsed = JsonSerializer.Deserialize<JsonElement>(backupRaw);
        Assert.Equal(3, parsed.GetProperty("defaultConcurrency").GetInt32());
    }

    [Fact]
    public async Task DefaultSettings_AreUsed_WhenFileMissing()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new ApplicationSettingsRepository(paths);

        Assert.False(File.Exists(paths.SettingsFile));
        var result = await repo.GetAsync();
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.GetValueOrThrow().DefaultConcurrency);
        Assert.Equal(TransferOverwritePolicy.Ask, result.GetValueOrThrow().DefaultOverwritePolicy);
        Assert.True(result.GetValueOrThrow().KeepCompletedTransfers);
    }

    [Fact]
    public async Task OverwritePolicy_Ask_IsPersistedAsZero()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new ApplicationSettingsRepository(paths);
        await repo.SaveAsync(new ApplicationSettings(3, TransferOverwritePolicy.Ask, true));

        var raw = await File.ReadAllTextAsync(paths.SettingsFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(0, parsed.GetProperty("defaultOverwritePolicy").GetInt32());
    }

    [Fact]
    public async Task OverwritePolicy_Overwrite_IsPersistedAsTwo()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new ApplicationSettingsRepository(paths);
        await repo.SaveAsync(new ApplicationSettings(3, TransferOverwritePolicy.Overwrite, true));

        var raw = await File.ReadAllTextAsync(paths.SettingsFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(2, parsed.GetProperty("defaultOverwritePolicy").GetInt32());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
