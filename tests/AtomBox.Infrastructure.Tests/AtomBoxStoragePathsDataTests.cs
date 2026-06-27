using System.Text;
using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Infrastructure.Tests;

public sealed class AtomBoxStoragePathsDataTests
{
    [Fact]
    public void DefaultConstructor_UsesAppDataDirectory()
    {
        var paths = new AtomBoxStoragePaths();
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AtomBox");
        Assert.Equal(expected, paths.RootDirectory);
    }

    [Fact]
    public void CustomRoot_PropagatesCorrectPaths()
    {
        var root = @"D:\test-atombox";
        var paths = new AtomBoxStoragePaths(root);
        Assert.Equal(root, paths.RootDirectory);
        Assert.Equal(Path.Combine(root, "config"), paths.ConfigurationDirectory);
        Assert.Equal(Path.Combine(root, "state"), paths.StateDirectory);
        Assert.Equal(Path.Combine(root, "credentials"), paths.CredentialDirectory);
        Assert.Equal(Path.Combine(root, "cache"), paths.CacheDirectory);
        Assert.Equal(Path.Combine(root, "logs"), paths.LogDirectory);
    }

    [Fact]
    public void AllFilePaths_AreUnderCorrectSubdirectories()
    {
        var root = @"D:\test-atombox";
        var paths = new AtomBoxStoragePaths(root);
        Assert.Equal(Path.Combine(root, "config", "accounts.json"), paths.AccountsFile);
        Assert.Equal(Path.Combine(root, "config", "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(root, "config", "schema-version.json"), paths.SchemaVersionFile);
        Assert.Equal(Path.Combine(root, "state", "transfer-tasks.json"), paths.TransferTasksFile);
        Assert.Equal(Path.Combine(root, "state", "transfer-progress.json"), paths.TransferProgressFile);
        Assert.Equal(Path.Combine(root, "credentials", "credential-index.json"), paths.CredentialIndexFile);
        Assert.Equal(Path.Combine(root, "credentials", "credential-key.bin"), paths.CredentialKeyFile);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void EmptyRoot_Throws(string? root)
    {
        Assert.Throws<ArgumentException>(() => new AtomBoxStoragePaths(root!));
    }

    [Fact]
    public async Task StorageWrites_CreateDirectoriesAutomatically()
    {
        var root = Path.Combine(Path.GetTempPath(), "AtomBox.Infra.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AtomBoxStoragePaths(root);

        Assert.False(Directory.Exists(paths.ConfigurationDirectory));
        Assert.False(Directory.Exists(paths.StateDirectory));
        Assert.False(Directory.Exists(paths.CredentialDirectory));

        var store = new AtomBox.Infrastructure.Storage.ApplicationSettingsRepository(paths);
        await store.SaveAsync(new AtomBox.Core.Settings.ApplicationSettings(3, default, true));

        Assert.True(Directory.Exists(paths.ConfigurationDirectory));
    }
}
