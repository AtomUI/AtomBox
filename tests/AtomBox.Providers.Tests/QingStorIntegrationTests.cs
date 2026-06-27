using AtomBox.Providers.Common;
using AtomBox.Providers.ObjectStorage.QingStor;

namespace AtomBox.Providers.Tests;

public sealed class QingStorIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task QingStorIntegration_ListUploadCreateFolderRenameMoveDownloadAndKeepObjects()
    {
        var environment = S3CompatibleIntegrationEnvironment.TryRead(
            StorageProviderRegistry.QingStorProviderId,
            "QingStor",
            "QINGSTOR",
            regionVariableName: "ZONE");
        if (environment is null)
        {
            return;
        }

        await S3CompatibleIntegrationTestHarness.RunVisibleFunctionalTestAsync(
            environment,
            (descriptor, credentialResolver) => new QingStorProviderCreator(descriptor, credentialResolver));
    }
}
