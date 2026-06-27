namespace AtomBox.Desktop.Navigation;

public sealed class NavigationService : INavigationService
{
    private readonly IPageViewModelFactory _pageViewModelFactory;
    private object? _currentViewModel;
    private string? _currentMenuKey;

    public NavigationService(IPageViewModelFactory pageViewModelFactory)
    {
        _pageViewModelFactory = pageViewModelFactory;
    }

    public event EventHandler? CurrentViewModelChanged;

    public event EventHandler? CurrentMenuKeyChanged;

    public object? CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
            if (ReferenceEquals(_currentViewModel, value))
            {
                return;
            }

            _currentViewModel = value;
            CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? CurrentMenuKey
    {
        get => _currentMenuKey;
        private set
        {
            if (string.Equals(_currentMenuKey, value, StringComparison.Ordinal))
            {
                return;
            }

            _currentMenuKey = value;
            CurrentMenuKeyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task NavigateAsync(NavigationTarget target, object? parameter = null)
    {
        CurrentViewModel = _pageViewModelFactory.Create(target, parameter);
        CurrentMenuKey = ResolveMenuKey(target, parameter);
        return Task.CompletedTask;
    }

    private static string? ResolveMenuKey(NavigationTarget target, object? parameter)
    {
        return target switch
        {
            NavigationTarget.Home => NavigationMenuKeys.Home,
            NavigationTarget.TransferQueue => NavigationMenuKeys.TransferQueue,
            NavigationTarget.TransferHistory => NavigationMenuKeys.TransferHistory,
            NavigationTarget.Settings => NavigationMenuKeys.Settings,
            NavigationTarget.AccountManagement => NavigationMenuKeys.AccountManagement,
            NavigationTarget.RemoteBrowser when parameter is RemoteBrowserNavigationParameter { StorageAccountId: { } accountId } =>
                NavigationMenuKeys.RemoteAccount(accountId),
            NavigationTarget.RemoteBrowser when parameter is RemoteBrowserNavigationParameter { ResourceGroup: NavigationResourceGroup.All } =>
                NavigationMenuKeys.RemoteStorage,
            NavigationTarget.RemoteBrowser when parameter is RemoteBrowserNavigationParameter { ResourceGroup: NavigationResourceGroup.ObjectStorage } =>
                NavigationMenuKeys.RemoteObjectStorage,
            NavigationTarget.RemoteBrowser when parameter is RemoteBrowserNavigationParameter { ResourceGroup: NavigationResourceGroup.FileTransfer } =>
                NavigationMenuKeys.RemoteFileTransfer,
            _ => null
        };
    }
}
