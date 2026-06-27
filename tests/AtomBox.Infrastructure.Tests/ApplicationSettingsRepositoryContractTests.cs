using AtomBox.Core.Errors;
using AtomBox.Core.Settings;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Storage;

namespace AtomBox.Infrastructure.Tests;

public sealed class ApplicationSettingsRepositoryContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Get_WhenFileDoesNotExist_ReturnsDefaultSettings()
    {
        var repo = new ApplicationSettingsRepository(new AtomBoxStoragePaths(_root));
        var result = await repo.GetAsync();
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.GetValueOrThrow().DefaultConcurrency);
    }

    [Fact]
    public async Task SaveAndGet_Roundtrips()
    {
        var repo = new ApplicationSettingsRepository(new AtomBoxStoragePaths(_root));
        var settings = new ApplicationSettings(5, TransferOverwritePolicy.Overwrite, false);

        var saveResult = await repo.SaveAsync(settings);
        Assert.True(saveResult.IsSuccess);

        var getResult = await repo.GetAsync();
        Assert.True(getResult.IsSuccess);
        Assert.Equal(5, getResult.GetValueOrThrow().DefaultConcurrency);
        Assert.Equal(TransferOverwritePolicy.Overwrite, getResult.GetValueOrThrow().DefaultOverwritePolicy);
        Assert.False(getResult.GetValueOrThrow().KeepCompletedTransfers);
    }

    [Fact]
    public async Task Save_CreatesBackup_WhenOverwriting()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var repo = new ApplicationSettingsRepository(paths);
        await repo.SaveAsync(new ApplicationSettings(3, TransferOverwritePolicy.Ask, true));

        await repo.SaveAsync(new ApplicationSettings(5, TransferOverwritePolicy.Overwrite, false));

        Assert.True(File.Exists(paths.SettingsFile));
        Assert.True(File.Exists($"{paths.SettingsFile}.bak"));
    }

    [Fact]
    public async Task Get_CorruptJson_BacksUpAndReturnsFailure()
    {
        var paths = new AtomBoxStoragePaths(_root);
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        await File.WriteAllTextAsync(paths.SettingsFile, "{ broken json");

        var repo = new ApplicationSettingsRepository(paths);
        var result = await repo.GetAsync();
        Assert.True(result.IsFailure);
        Assert.True(Directory.GetFiles(paths.ConfigurationDirectory, "settings.json.*.corrupt").Any());
    }

    [Fact]
    public async Task SaveThenGet_MultipleTimes_ReturnsLatest()
    {
        var repo = new ApplicationSettingsRepository(new AtomBoxStoragePaths(_root));

        for (var i = 1; i <= 3; i++)
        {
            var result = await repo.SaveAsync(new ApplicationSettings(i, TransferOverwritePolicy.Ask, true));
            Assert.True(result.IsSuccess);
        }

        var getResult = await repo.GetAsync();
        Assert.True(getResult.IsSuccess);
        Assert.Equal(3, getResult.GetValueOrThrow().DefaultConcurrency);
    }

    [Fact]
    public async Task Get_EmptyJsonObject_ReturnsFailure()
    {
        var paths = new AtomBoxStoragePaths(_root);
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        await File.WriteAllTextAsync(paths.SettingsFile, "{}");

        var repo = new ApplicationSettingsRepository(paths);
        var result = await repo.GetAsync();
        Assert.True(result.IsFailure);
        Assert.Equal(StorageErrorCategory.Infrastructure, result.Error?.Category);
        Assert.True(Directory.GetFiles(paths.ConfigurationDirectory, "settings.json.*.corrupt").Any());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
