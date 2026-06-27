using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.BaiduBos;

namespace AtomBox.Providers.Tests;

public sealed class BaiduBosIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task BaiduBosIntegration_ListUploadCreateFolderRenameMoveDownloadAndKeepObjects()
    {
        var environment = S3CompatibleIntegrationEnvironment.TryRead(
            StorageProviderRegistry.BaiduBosProviderId,
            "Baidu BOS",
            "BAIDU_BOS");
        if (environment is null)
        {
            return;
        }

        await S3CompatibleIntegrationTestHarness.RunVisibleFunctionalTestAsync(
            environment,
            (descriptor, credentialResolver) => new BaiduBosProviderCreator(descriptor, credentialResolver));
    }
}
