using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Navigation;

namespace AtomBox.Desktop.Tests;

public sealed class NavigationParameterCodecTests
{
    [Fact]
    public void RemoteBrowserParameterRoundTripsThroughCityRouteValues()
    {
        var parameter = new RemoteBrowserNavigationParameter(
            NavigationResourceGroup.FileTransfer,
            StorageAccountId.New(),
            new RemotePath("home/reports", RemotePathKind.Folder, '/'));

        var encoded = NavigationService.EncodeParameters(NavigationTarget.RemoteBrowser, parameter);
        var decoded = Assert.IsType<RemoteBrowserNavigationParameter>(
            NavigationService.DecodeParameter(NavigationTarget.RemoteBrowser, encoded));

        Assert.Equal(parameter, decoded);
    }

    [Fact]
    public void TransferHistoryPageRoundTripsThroughCityRouteValues()
    {
        var parameter = new TransferHistoryNavigationParameter(7);

        var encoded = NavigationService.EncodeParameters(NavigationTarget.TransferHistory, parameter);
        var decoded = Assert.IsType<TransferHistoryNavigationParameter>(
            NavigationService.DecodeParameter(NavigationTarget.TransferHistory, encoded));

        Assert.Equal(parameter, decoded);
    }

    [Fact]
    public void AccountSelectionRoundTripsThroughCityRouteValues()
    {
        var parameter = new AccountManagementNavigationParameter(StorageAccountId.New());

        var encoded = NavigationService.EncodeParameters(NavigationTarget.AccountManagement, parameter);
        var decoded = Assert.IsType<AccountManagementNavigationParameter>(
            NavigationService.DecodeParameter(NavigationTarget.AccountManagement, encoded));

        Assert.Equal(parameter, decoded);
    }
}
