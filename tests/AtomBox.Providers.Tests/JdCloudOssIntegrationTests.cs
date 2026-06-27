using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.JdCloudOss;

namespace AtomBox.Providers.Tests;

public sealed class JdCloudOssIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task JdCloudOssIntegration_ListUploadCreateFolderRenameMoveDownloadAndKeepObjects()
    {
        var environment = S3CompatibleIntegrationEnvironment.TryRead(
            StorageProviderRegistry.JdCloudOssProviderId,
            "JDCloud OSS",
            "JDCLOUD_OSS");
        if (environment is null)
        {
            return;
        }

        await S3CompatibleIntegrationTestHarness.RunVisibleFunctionalTestAsync(
            environment,
            (descriptor, credentialResolver) => new JdCloudOssProviderCreator(descriptor, credentialResolver));
    }
}
