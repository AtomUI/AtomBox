using System.Globalization;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.ViewModels.Pages;
using AtomUI.City.Presentation;
using AtomUI.City.Routing;
using Microsoft.Extensions.DependencyInjection;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.Navigation;

public sealed class NavigationService : INavigationService, IAsyncDisposable
{
    private const string ResourceGroupParameter = "resourceGroup";
    private const string StorageAccountIdParameter = "storageAccountId";
    private const string RemotePathParameter = "remotePath";
    private const string RemotePathKindParameter = "remotePathKind";
    private const string RemotePathSeparatorParameter = "remotePathSeparator";
    private const string PageIndexParameter = "pageIndex";
    private const string SelectedAccountIdParameter = "selectedAccountId";

    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _navigationGate = new(1, 1);
    private readonly AsyncServiceScope _routingScope;
    private readonly IServiceProvider _services;
    private readonly IRouter _router;
    private readonly IViewModelFactory _viewModelFactory;
    private readonly IViewLocator _viewLocator;
    private readonly AtomUI.City.Presentation.ViewFactory _viewFactory;
    private readonly ViewBinder _viewBinder;
    private readonly CityUiDispatcher _dispatcher;
    private IRouteOutlet? _outlet;
    private string? _currentMenuKey;
    private bool _disposed;

    public NavigationService(
        IServiceProvider services,
        IServiceScopeFactory scopeFactory,
        IViewModelFactory viewModelFactory,
        IViewLocator viewLocator,
        AtomUI.City.Presentation.ViewFactory viewFactory,
        ViewBinder viewBinder,
        CityUiDispatcher dispatcher)
    {
        _services = services;
        _routingScope = scopeFactory.CreateAsyncScope();
        _router = _routingScope.ServiceProvider.GetRequiredService<IRouter>();
        _viewModelFactory = viewModelFactory;
        _viewLocator = viewLocator;
        _viewFactory = viewFactory;
        _viewBinder = viewBinder;
        _dispatcher = dispatcher;
    }

    public event EventHandler? CurrentMenuKeyChanged;

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

    internal void AttachOutlet(IRouteOutlet outlet)
    {
        ArgumentNullException.ThrowIfNull(outlet);
        if (!string.Equals(outlet.Name, AtomBoxRouteCatalog.PrimaryOutlet, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Expected the '{AtomBoxRouteCatalog.PrimaryOutlet}' route outlet.",
                nameof(outlet));
        }

        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_outlet is not null && !ReferenceEquals(_outlet, outlet))
            {
                throw new InvalidOperationException("AtomBox navigation is already attached to another outlet.");
            }

            _outlet = outlet;
        }
    }

    public async Task NavigateAsync(
        NavigationTarget target,
        object? parameter = null,
        CancellationToken cancellationToken = default)
    {
        await _navigationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var outlet = GetOutlet();
            var parameters = EncodeParameters(target, parameter);
            var routeResult = await _router.NavigateAsync(
                AtomBoxRouteCatalog.GetReference(target),
                parameters,
                new NavigationOptions { OutletName = AtomBoxRouteCatalog.PrimaryOutlet },
                cancellationToken)
                .ConfigureAwait(true);

            if (routeResult.Status != NavigationResultStatus.Success || routeResult.ActiveRoute is null)
            {
                throw new InvalidOperationException(
                    routeResult.Error is null
                        ? $"City navigation to '{target}' did not produce an active route."
                        : $"City navigation failed ({routeResult.Error.Code}): {routeResult.Error.Message}",
                    routeResult.Error?.Exception);
            }

            var navigationParameter = DecodeParameter(target, routeResult.Parameters);
            await CommitRouteAsync(outlet, routeResult.Route, navigationParameter, cancellationToken).ConfigureAwait(true);
            await _dispatcher.InvokeAsync(
                () => CurrentMenuKey = ResolveMenuKey(target, navigationParameter),
                cancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _navigationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_stateGate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _outlet = null;
            }

            await _routingScope.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    private IRouteOutlet GetOutlet()
    {
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _outlet ?? throw new InvalidOperationException(
                "AtomBox navigation is not attached to the main window route outlet.");
        }
    }

    private async Task CommitRouteAsync(
        IRouteOutlet outlet,
        RouteDescriptor route,
        object? navigationParameter,
        CancellationToken cancellationToken)
    {
        var viewModelType = route.ViewModelTarget?.ViewModelType ?? throw new InvalidOperationException(
            $"Route '{route.RouteId}' does not declare a ViewModel target.");
        ViewModelLease? viewModelLease = null;
        BoundViewHandle? handle = null;
        var ownershipTransferred = false;

        try
        {
            viewModelLease = await _viewModelFactory.AcquireAsync(
                new ViewModelAcquisitionRequest(viewModelType, _services),
                cancellationToken).ConfigureAwait(true);
            await _dispatcher.InvokeAsync(
                () => ApplyNavigationParameter(viewModelLease.Instance, navigationParameter),
                cancellationToken)
                .ConfigureAwait(true);

            var descriptor = _viewLocator.Locate(viewModelType);
            var view = await _viewFactory.CreateAsync(descriptor, cancellationToken).ConfigureAwait(true);
            handle = await _dispatcher.InvokeAsync(
                () => _viewBinder.Bind(descriptor, view, viewModelLease.Instance),
                cancellationToken)
                .ConfigureAwait(true);

            var plan = RouteOutletCommitPlan.Replace(
                AtomBoxRouteCatalog.PrimaryOutlet,
                handle,
                viewModelLease,
                routeId: route.RouteId,
                reuseKey: route.ViewModelTarget.ReuseKey);
            ownershipTransferred = true;
            var commitResult = await outlet.CommitAsync(plan, cancellationToken).ConfigureAwait(true);
            if (!commitResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"City route outlet commit failed ({commitResult.Error}): {commitResult.Message}");
            }
        }
        catch
        {
            if (!ownershipTransferred)
            {
                if (handle is not null)
                {
                    await _dispatcher.InvokeAsync(handle.Dispose).ConfigureAwait(false);
                }

                if (viewModelLease is not null)
                {
                    await viewModelLease.DisposeAsync().ConfigureAwait(false);
                }
            }

            throw;
        }
    }

    internal static IReadOnlyDictionary<string, string> EncodeParameters(
        NavigationTarget target,
        object? parameter)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        switch (target, parameter)
        {
            case (NavigationTarget.RemoteBrowser, RemoteBrowserNavigationParameter remote):
                values[ResourceGroupParameter] = remote.ResourceGroup.ToString();
                if (remote.StorageAccountId is { } accountId)
                {
                    values[StorageAccountIdParameter] = accountId.ToString();
                }

                if (remote.RemotePath is { } path)
                {
                    values[RemotePathParameter] = path.Value;
                    values[RemotePathKindParameter] = ((int)path.Kind).ToString(CultureInfo.InvariantCulture);
                    values[RemotePathSeparatorParameter] = ((int)path.Separator).ToString(CultureInfo.InvariantCulture);
                }

                break;
            case (NavigationTarget.TransferHistory, TransferHistoryNavigationParameter { PageIndex: { } pageIndex }):
                values[PageIndexParameter] = pageIndex.ToString(CultureInfo.InvariantCulture);
                break;
            case (NavigationTarget.AccountManagement, AccountManagementNavigationParameter { SelectedAccountId: { } selectedId }):
                values[SelectedAccountIdParameter] = selectedId.ToString();
                break;
            case (_, null):
                break;
            case (NavigationTarget.RemoteBrowser, _):
                throw new ArgumentException("Remote browser navigation requires a RemoteBrowserNavigationParameter.", nameof(parameter));
            case (NavigationTarget.TransferHistory, _):
                throw new ArgumentException("Transfer history navigation requires a TransferHistoryNavigationParameter.", nameof(parameter));
            case (NavigationTarget.AccountManagement, _):
                throw new ArgumentException("Account management navigation requires an AccountManagementNavigationParameter.", nameof(parameter));
        }

        return values;
    }

    internal static object? DecodeParameter(
        NavigationTarget target,
        IReadOnlyDictionary<string, string> parameters)
    {
        return target switch
        {
            NavigationTarget.RemoteBrowser when parameters.TryGetValue(ResourceGroupParameter, out var resourceGroup) =>
                new RemoteBrowserNavigationParameter(
                    Enum.Parse<NavigationResourceGroup>(resourceGroup),
                    ParseAccountId(parameters, StorageAccountIdParameter),
                    ParseRemotePath(parameters)),
            NavigationTarget.TransferHistory when parameters.TryGetValue(PageIndexParameter, out var pageIndex) =>
                new TransferHistoryNavigationParameter(int.Parse(pageIndex, CultureInfo.InvariantCulture)),
            NavigationTarget.AccountManagement =>
                new AccountManagementNavigationParameter(ParseAccountId(parameters, SelectedAccountIdParameter)),
            _ => null,
        };
    }

    private static StorageAccountId? ParseAccountId(
        IReadOnlyDictionary<string, string> parameters,
        string key)
    {
        return parameters.TryGetValue(key, out var value)
            ? StorageAccountId.From(Guid.Parse(value))
            : null;
    }

    private static RemotePath? ParseRemotePath(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue(RemotePathParameter, out var value))
        {
            return null;
        }

        var kind = parameters.TryGetValue(RemotePathKindParameter, out var kindText)
            ? (RemotePathKind)int.Parse(kindText, CultureInfo.InvariantCulture)
            : RemotePathKind.ObjectPath;
        var separator = parameters.TryGetValue(RemotePathSeparatorParameter, out var separatorText)
            ? (char)int.Parse(separatorText, CultureInfo.InvariantCulture)
            : '/';
        return new RemotePath(value, kind, separator);
    }

    private static void ApplyNavigationParameter(object viewModel, object? parameter)
    {
        switch (viewModel, parameter)
        {
            case (RemoteBrowserViewModel remoteBrowser, RemoteBrowserNavigationParameter remote):
                remoteBrowser.ApplyNavigationParameter(remote);
                break;
            case (TransferHistoryViewModel history, TransferHistoryNavigationParameter historyParameter):
                history.ApplyNavigationParameter(historyParameter);
                break;
            case (AccountManagementViewModel accounts, AccountManagementNavigationParameter accountParameter):
                accounts.ApplyNavigationParameter(accountParameter);
                break;
        }
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
            NavigationTarget.RemoteBrowser when parameter is RemoteBrowserNavigationParameter { ResourceGroup: NavigationResourceGroup.WebDav } =>
                NavigationMenuKeys.RemoteWebDav,
            _ => null,
        };
    }
}
