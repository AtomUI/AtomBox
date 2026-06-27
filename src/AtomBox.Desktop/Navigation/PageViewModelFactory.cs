using AtomBox.Desktop.ViewModels.Pages;

namespace AtomBox.Desktop.Navigation;

public sealed class PageViewModelFactory : IPageViewModelFactory
{
    private readonly Func<HomeViewModel> _homeFactory;
    private readonly Func<RemoteBrowserViewModel> _remoteBrowserFactory;
    private readonly Func<TransferQueueViewModel> _transferQueueFactory;
    private readonly Func<TransferHistoryViewModel> _transferHistoryFactory;
    private readonly Func<SettingsViewModel> _settingsFactory;
    private readonly Func<AccountManagementViewModel> _accountManagementFactory;

    public PageViewModelFactory(
        Func<HomeViewModel> homeFactory,
        Func<RemoteBrowserViewModel> remoteBrowserFactory,
        Func<TransferQueueViewModel> transferQueueFactory,
        Func<TransferHistoryViewModel> transferHistoryFactory,
        Func<SettingsViewModel> settingsFactory,
        Func<AccountManagementViewModel> accountManagementFactory)
    {
        _homeFactory = homeFactory;
        _remoteBrowserFactory = remoteBrowserFactory;
        _transferQueueFactory = transferQueueFactory;
        _transferHistoryFactory = transferHistoryFactory;
        _settingsFactory = settingsFactory;
        _accountManagementFactory = accountManagementFactory;
    }

    public object Create(NavigationTarget target, object? parameter)
    {
        return target switch
        {
            NavigationTarget.Home => _homeFactory(),
            NavigationTarget.RemoteBrowser => CreateRemoteBrowser(parameter),
            NavigationTarget.TransferQueue => _transferQueueFactory(),
            NavigationTarget.TransferHistory => CreateTransferHistory(parameter),
            NavigationTarget.Settings => _settingsFactory(),
            NavigationTarget.AccountManagement => CreateAccountManagement(parameter),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown navigation target.")
        };
    }

    private RemoteBrowserViewModel CreateRemoteBrowser(object? parameter)
    {
        var viewModel = _remoteBrowserFactory();
        if (parameter is RemoteBrowserNavigationParameter remoteParameter)
        {
            viewModel.ApplyNavigationParameter(remoteParameter);
        }

        return viewModel;
    }

    private TransferHistoryViewModel CreateTransferHistory(object? parameter)
    {
        var viewModel = _transferHistoryFactory();
        if (parameter is TransferHistoryNavigationParameter historyParameter)
        {
            viewModel.ApplyNavigationParameter(historyParameter);
        }

        return viewModel;
    }

    private AccountManagementViewModel CreateAccountManagement(object? parameter)
    {
        var viewModel = _accountManagementFactory();
        if (parameter is AccountManagementNavigationParameter accountParameter)
        {
            viewModel.ApplyNavigationParameter(accountParameter);
        }

        return viewModel;
    }
}
