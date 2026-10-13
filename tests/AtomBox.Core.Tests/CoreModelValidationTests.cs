using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Credentials;
using AtomBox.Core.Providers;
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
    public void StorageAccount_ProviderConfigIsAnImmutableSnapshot()
    {
        var providerConfig = new Dictionary<string, string>
        {
            ["bucket"] = "assets"
        };
        var now = DateTimeOffset.UtcNow;
        var account = new StorageAccount(
            StorageAccountId.New(),
            StorageProviderCategory.ObjectStorage,
            StorageProviderId.From("s3"),
            "Primary",
            null,
            null,
            CredentialRef.From("cred-1"),
            now,
            now,
            providerConfig);

        providerConfig["bucket"] = "changed-at-source";

        Assert.Equal("assets", account.ProviderConfig["bucket"]);
        var exposed = Assert.IsAssignableFrom<IDictionary<string, string>>(account.ProviderConfig);
        Assert.Throws<NotSupportedException>(() => exposed["bucket"] = "changed-through-property");
        Assert.Equal("assets", account.ProviderConfig["bucket"]);
    }

    [Fact]
    public void CredentialSecretMaterial_ValuesAreAnImmutableSnapshot()
    {
        var values = new Dictionary<string, string>
        {
            ["accessKey"] = "secret"
        };
        var material = new CredentialSecretMaterial(values);

        values["accessKey"] = "changed-at-source";

        Assert.Equal("secret", material.GetRequiredValue("accessKey"));
        var exposed = Assert.IsAssignableFrom<IDictionary<string, string>>(material.Values);
        Assert.Throws<NotSupportedException>(() => exposed["accessKey"] = "changed-through-property");
        Assert.Equal("secret", material.GetRequiredValue("accessKey"));
    }

    [Fact]
    public void ProviderDescriptor_ConfigFieldsAreAnImmutableSnapshot()
    {
        var originalField = new ProviderConfigFieldDescriptor(
            "bucket",
            "Bucket",
            ProviderConfigFieldKind.Bucket,
            true);
        var fields = new List<ProviderConfigFieldDescriptor> { originalField };
        var descriptor = new ProviderDescriptor(
            StorageProviderId.From("s3"),
            StorageProviderCategory.ObjectStorage,
            "S3",
            "S3 provider",
            StorageCapabilitySet.Empty,
            fields);

        fields.Clear();

        Assert.Equal(originalField, Assert.Single(descriptor.ConfigFields));
        var exposed = Assert.IsAssignableFrom<IList<ProviderConfigFieldDescriptor>>(descriptor.ConfigFields);
        Assert.Throws<NotSupportedException>(() => exposed.Clear());
        Assert.Equal(originalField, Assert.Single(descriptor.ConfigFields));
    }

    [Fact]
    public void RemoteItemPage_ItemsAreAnImmutableSnapshot()
    {
        var originalItem = new RemoteItem(
            "file.txt",
            RemotePath.Root.Combine("file.txt"),
            RemoteItemKind.File,
            1,
            null);
        var items = new List<RemoteItem> { originalItem };
        var page = new RemoteItemPage(RemotePath.Root, items, null, null, 50);

        items.Clear();

        Assert.Equal(originalItem, Assert.Single(page.Items));
        var exposed = Assert.IsAssignableFrom<IList<RemoteItem>>(page.Items);
        Assert.Throws<NotSupportedException>(() => exposed.Clear());
        Assert.Equal(originalItem, Assert.Single(page.Items));
    }

    [Fact]
    public void SnapshotModels_RejectInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => new RemoteItem(" ", RemotePath.Root, RemoteItemKind.Folder, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RemoteItem("file.txt", RemotePath.Root.Combine("file.txt"), RemoteItemKind.File, -1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationSettings(0, TransferOverwritePolicy.Skip, true));
    }
}
