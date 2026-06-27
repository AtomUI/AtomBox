using AtomBox.Core.Accounts;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Settings;
using AtomBox.Core.ValueObjects;

namespace AtomBox.Core.Tests;

public sealed class CoreModelValidationTests
{
    [Fact]
    public void StringValueObjects_RejectEmptyInputAndExposeSafeDefaultState()
    {
        Assert.Throws<ArgumentException>(() => new StorageProviderId(" "));
        Assert.Throws<ArgumentException>(() => new CredentialRef(" "));
        Assert.Throws<ArgumentException>(() => new LocalPath(" "));

        Assert.True(default(StorageProviderId).IsEmpty);
        Assert.True(default(CredentialRef).IsEmpty);
        Assert.True(default(LocalPath).IsEmpty);
    }

    [Fact]
    public void StorageAccount_UpdateConfiguration_ReturnsValidatedSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "Aliyun",
            null,
            null,
            new CredentialRef("cred-1"),
            now,
            now);

        var updated = account.UpdateConfiguration("  Aliyun OSS  ", " endpoint ", " cn-hangzhou ", new CredentialRef("cred-2"), now.AddSeconds(1));

        Assert.Equal("Aliyun OSS", updated.DisplayName);
        Assert.Equal("endpoint", updated.Endpoint);
        Assert.Equal("cn-hangzhou", updated.Region);
        Assert.Throws<ArgumentException>(() => account.UpdateConfiguration(" ", null, null, account.CredentialRef, now.AddSeconds(1)));
    }

    [Fact]
    public void StorageAccount_NormalizesProviderConfigWithoutSecrets()
    {
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            new StorageProviderId("aliyun-oss"),
            "Aliyun",
            " endpoint ",
            " cn-hangzhou ",
            new CredentialRef("cred-1"),
            now,
            now,
            new Dictionary<string, string>
            {
                [" bucket "] = " assets ",
                ["endpoint"] = "ignored-endpoint",
                ["empty"] = " "
            });

        Assert.Equal("endpoint", account.GetProviderConfigValue("endpoint"));
        Assert.Equal("cn-hangzhou", account.GetProviderConfigValue("region"));
        Assert.Equal("assets", account.GetProviderConfigValue("bucket"));
        Assert.False(account.ProviderConfig.ContainsKey("endpoint"));
        Assert.False(account.ProviderConfig.ContainsKey("empty"));
    }

    [Fact]
    public void SnapshotModels_RejectInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => new RemoteItem(" ", RemotePath.Root, RemoteItemKind.Folder, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteItem("file.txt", RemotePath.Root.Combine("file.txt"), RemoteItemKind.File, -1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(0, TransferOverwritePolicy.Skip, true));
    }
}
