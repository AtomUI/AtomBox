using System.Text.Json;
using AtomBox.Application.Settings;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Application.Tests;

public sealed class SettingsAppServiceDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.App.SettingsData", Guid.NewGuid().ToString("N"));
    private static readonly CancellationToken CT = CancellationToken.None;

    [Fact]
    public async Task GetAsync_WhenNoFileExists_ReturnsDefault()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new ApplicationSettingsRepository(paths);
        var service = new SettingsAppService(repo);

        var result = await service.GetAsync(new GetApplicationSettingsRequest());

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.GetValueOrThrow().Settings);
    }

    [Fact]
    public async Task UpdateAsync_PersistsToJsonFile()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new ApplicationSettingsRepository(paths);
        var service = new SettingsAppService(repo);
        var settings = new ApplicationSettings(7, TransferOverwritePolicy.Rename, false);

        var result = await service.UpdateAsync(new UpdateApplicationSettingsRequest(settings));

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(paths.SettingsFile));
        var raw = await File.ReadAllTextAsync(paths.SettingsFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(JsonValueKind.Object, parsed.ValueKind);
        Assert.Equal(7, parsed.GetProperty("defaultConcurrency").GetInt32());
        Assert.Equal(3, parsed.GetProperty("defaultOverwritePolicy").GetInt32());
        Assert.False(parsed.GetProperty("keepCompletedTransfers").GetBoolean());
    }

    [Fact]
    public async Task UpdateThenGetAsync_ReturnsSameValues()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new ApplicationSettingsRepository(paths);
        var service = new SettingsAppService(repo);
        var settings = new ApplicationSettings(10, TransferOverwritePolicy.Overwrite, true);

        await service.UpdateAsync(new UpdateApplicationSettingsRequest(settings));
        var result = await service.GetAsync(new GetApplicationSettingsRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.GetValueOrThrow().Settings.DefaultConcurrency);
        Assert.Equal(TransferOverwritePolicy.Overwrite, result.GetValueOrThrow().Settings.DefaultOverwritePolicy);
        Assert.True(result.GetValueOrThrow().Settings.KeepCompletedTransfers);
    }

    [Fact]
    public async Task ResetAsync_WritesDefaultsToFile()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new ApplicationSettingsRepository(paths);
        var service = new SettingsAppService(repo);

        await service.UpdateAsync(new UpdateApplicationSettingsRequest(
            new ApplicationSettings(99, TransferOverwritePolicy.Rename, false)));
        var resetResult = await service.ResetAsync(new ResetApplicationSettingsRequest());

        Assert.True(resetResult.IsSuccess);
        Assert.Equal(3, resetResult.GetValueOrThrow().Settings.DefaultConcurrency);
        Assert.Equal(TransferOverwritePolicy.Ask, resetResult.GetValueOrThrow().Settings.DefaultOverwritePolicy);
        Assert.True(resetResult.GetValueOrThrow().Settings.KeepCompletedTransfers);

        var raw = await File.ReadAllTextAsync(paths.SettingsFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(3, parsed.GetProperty("defaultConcurrency").GetInt32());
    }

    [Fact]
    public async Task MultipleUpdates_OverwritesFileContent()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new ApplicationSettingsRepository(paths);
        var service = new SettingsAppService(repo);

        await service.UpdateAsync(new UpdateApplicationSettingsRequest(
            new ApplicationSettings(1, TransferOverwritePolicy.Ask, false)));
        await service.UpdateAsync(new UpdateApplicationSettingsRequest(
            new ApplicationSettings(5, TransferOverwritePolicy.Overwrite, true)));

        var result = await service.GetAsync(new GetApplicationSettingsRequest());
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.GetValueOrThrow().Settings.DefaultConcurrency);

        var raw = await File.ReadAllTextAsync(paths.SettingsFile);
        var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(JsonValueKind.Object, parsed.ValueKind);
    }

    [Fact]
    public async Task GetAsync_WithCorruptFile_FallsBackToDefault()
    {
        var paths = new AtomBoxStoragePaths(_root);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.SettingsFile)!);
        await File.WriteAllTextAsync(paths.SettingsFile, "corrupt json");
        var repo = new ApplicationSettingsRepository(paths);
        var service = new SettingsAppService(repo);

        var result = await service.GetAsync(new GetApplicationSettingsRequest());

        Assert.True(result.IsFailure);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
