using System.Collections.ObjectModel;
using AtomBox.Application.Accounts;
using AtomBox.Application.State;
using AtomBox.Core.Accounts;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.ViewModels;
using AtomUI.City.Mvvm;
using AtomUI.City.State;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.Shell;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IAccountDialogWorkflow _accountDialogWorkflow;
    private readonly IAtomBoxApplicationState _applicationState;
    private readonly IDesktopPreferencesService _preferences;
    private readonly CityUiDispatcher _dispatcher;
    private readonly Dictionary<StorageAccountId, StorageAccountSummary> _navigationAccounts = [];
    private readonly object _menuNavigationGate = new();
    private CancellationTokenSource? _menuNavigationCancellation;
    private string? _currentMenuKey;

    public MainWindowViewModel(
        INavigationService navigationService,
        IAccountDialogWorkflow accountDialogWorkflow,
        IAtomBoxApplicationState applicationState,
        IDesktopPreferencesService preferences,
        StatusBarViewModel statusBar,
        CityUiDispatcher dispatcher)
    {
        _navigationService = navigationService;
        _accountDialogWorkflow = accountDialogWorkflow;
        _applicationState = applicationState;
        _preferences = preferences;
        _dispatcher = dispatcher;
        StatusBar = statusBar;
    }

    public string? CurrentMenuKey
    {
        get => _currentMenuKey;
        private set
        {
            if (SetProperty(ref _currentMenuKey, value))
            {
                CurrentMenuKeyChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? CurrentMenuKeyChanged;

    public event EventHandler? RemoteAccountMenuRefreshed;

    public StatusBarViewModel StatusBar { get; }

    public ObservableCollection<RemoteAccountNavigationItem> ObjectStorageAccounts { get; } = [];

    public ObservableCollection<RemoteAccountNavigationItem> FileTransferAccounts { get; } = [];

    public ObservableCollection<RemoteAccountNavigationItem> WebDavAccounts { get; } = [];

    public async Task NavigateByMenuKeyAsync(
        string? itemKey,
        CancellationToken cancellationToken = default)
    {
        var lifetimeCancellationToken = cancellationToken.CanBeCanceled
            ? cancellationToken
            : CurrentActivationScope?.CancellationToken ?? CancellationToken.None;

        using var supersession = new CancellationTokenSource();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            lifetimeCancellationToken,
            supersession.Token);

        lock (_menuNavigationGate)
        {
            _menuNavigationCancellation?.Cancel();
            _menuNavigationCancellation = supersession;
        }

        try
        {
            await NavigateByMenuKeyCoreAsync(itemKey, linkedCancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (
            supersession.IsCancellationRequested &&
            !lifetimeCancellationToken.IsCancellationRequested)
        {
            // A newer menu selection superseded this navigation intent.
        }
        finally
        {
            lock (_menuNavigationGate)
            {
                if (ReferenceEquals(_menuNavigationCancellation, supersession))
                {
                    _menuNavigationCancellation = null;
                }
            }
        }
    }

    private Task NavigateByMenuKeyCoreAsync(
        string? itemKey,
        CancellationToken cancellationToken)
    {
        if (TryParseRemoteAccountMenuKey(itemKey, out var accountId) &&
            _navigationAccounts.TryGetValue(accountId, out var account))
        {
            return NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(
                    ToResourceGroup(account),
                    account.Id,
                    GetInitialPath(account)),
                cancellationToken);
        }

        return itemKey switch
        {
            NavigationMenuKeys.Home => NavigateAsync(NavigationTarget.Home, cancellationToken: cancellationToken),
            NavigationMenuKeys.RemoteStorage => NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(NavigationResourceGroup.All),
                cancellationToken),
            NavigationMenuKeys.RemoteObjectStorage => NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(NavigationResourceGroup.ObjectStorage),
                cancellationToken),
            NavigationMenuKeys.RemoteFileTransfer => NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(NavigationResourceGroup.FileTransfer),
                cancellationToken),
            NavigationMenuKeys.RemoteWebDav => NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(NavigationResourceGroup.WebDav),
                cancellationToken),
            NavigationMenuKeys.AddRemote => AddAccountAndNavigateAsync(null, cancellationToken),
            NavigationMenuKeys.TransferQueue => NavigateAsync(NavigationTarget.TransferQueue, cancellationToken: cancellationToken),
            NavigationMenuKeys.TransferHistory => NavigateAsync(NavigationTarget.TransferHistory, cancellationToken: cancellationToken),
            NavigationMenuKeys.Settings => NavigateAsync(NavigationTarget.Settings, cancellationToken: cancellationToken),
            NavigationMenuKeys.AccountManagement => NavigateAsync(NavigationTarget.AccountManagement, cancellationToken: cancellationToken),
            _ => Task.CompletedTask
        };
    }

    public Task RefreshRemoteAccountMenuAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyAccountCatalog(_applicationState.Accounts.Value);
        return Task.CompletedTask;
    }

    private void ApplyAccountCatalog(AccountCatalogState state)
    {
        var accounts = state.Status == ApplicationProjectionStatus.Ready
            ? state.Accounts
            .OrderBy(account => account.ProviderCategory)
            .ThenBy(account => account.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray()
            : [];

        _navigationAccounts.Clear();
        foreach (var account in accounts)
        {
            _navigationAccounts[account.Id] = account;
        }

        ReplaceNavigationItems(
            ObjectStorageAccounts,
            accounts.Where(account => account.ProviderCategory == StorageProviderCategory.ObjectStorage));
        ReplaceNavigationItems(
            FileTransferAccounts,
            accounts.Where(account => IsFtpOrSftp(account)));
        ReplaceNavigationItems(
            WebDavAccounts,
            accounts.Where(account => IsWebDav(account)));
        RemoteAccountMenuRefreshed?.Invoke(this, EventArgs.Empty);
    }

    protected override ValueTask OnActivatedAsync(
        ActivationContext context,
        CancellationToken cancellationToken)
    {
        context.Scope.Add(_applicationState.Accounts.Observe(
            args => ApplyAccountCatalog(args.NewValue),
            StateSubscriptionOptions.Dispatcher(_dispatcher)));
        _navigationService.CurrentMenuKeyChanged += HandleCurrentMenuKeyChanged;
        context.Scope.Add(EventSubscription.Create(
            () => _navigationService.CurrentMenuKeyChanged -= HandleCurrentMenuKeyChanged));

        return _dispatcher.PostAsync(
            async token =>
            {
                var preferences = await _preferences.GetAsync(token).ConfigureAwait(true);
                await NavigateStartupPageAsync(preferences.StartupPage, token).ConfigureAwait(true);
            },
            cancellationToken);
    }

    private void HandleCurrentMenuKeyChanged(object? sender, EventArgs args)
    {
        CurrentMenuKey = _navigationService.CurrentMenuKey;
    }

    private Task NavigateStartupPageAsync(
        StartupPageOption startupPage,
        CancellationToken cancellationToken)
    {
        return startupPage switch
        {
            StartupPageOption.Home => NavigateAsync(NavigationTarget.Home, cancellationToken: cancellationToken),
            StartupPageOption.TransferQueue => NavigateAsync(NavigationTarget.TransferQueue, cancellationToken: cancellationToken),
            StartupPageOption.AccountManagement => NavigateAsync(NavigationTarget.AccountManagement, cancellationToken: cancellationToken),
            _ => NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(NavigationResourceGroup.All),
                cancellationToken)
        };
    }

    private async Task AddAccountAndNavigateAsync(
        StorageProviderCategory? category,
        CancellationToken cancellationToken)
    {
        var result = await _accountDialogWorkflow
            .AddAccountAsync(category, cancellationToken: cancellationToken)
            .ConfigureAwait(true);
        if (result.IsFailure || result.GetValueOrThrow() is not { } account)
        {
            return;
        }

        await NavigateAsync(
            NavigationTarget.RemoteBrowser,
            new RemoteBrowserNavigationParameter(
                ToResourceGroup(account),
                account.Id,
                GetInitialPath(account)),
            cancellationToken).ConfigureAwait(true);
    }

    private Task NavigateAsync(
        NavigationTarget target,
        object? parameter = null,
        CancellationToken cancellationToken = default)
    {
        return _navigationService.NavigateAsync(target, parameter, cancellationToken);
    }

    private static NavigationResourceGroup ToResourceGroup(StorageAccountSummary account)
    {
        return IsWebDav(account)
            ? NavigationResourceGroup.WebDav
            : ToResourceGroup(account.ProviderCategory);
    }

    private static NavigationResourceGroup ToResourceGroup(StorageProviderCategory category)
    {
        return category switch
        {
            StorageProviderCategory.ObjectStorage => NavigationResourceGroup.ObjectStorage,
            StorageProviderCategory.FileTransfer => NavigationResourceGroup.FileTransfer,
            _ => NavigationResourceGroup.ObjectStorage
        };
    }

    private static bool TryParseRemoteAccountMenuKey(string? itemKey, out StorageAccountId accountId)
    {
        accountId = default;
        if (string.IsNullOrWhiteSpace(itemKey) ||
            !itemKey.StartsWith(NavigationMenuKeys.RemoteAccountPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var idText = itemKey[NavigationMenuKeys.RemoteAccountPrefix.Length..];
        if (!Guid.TryParse(idText, out var id))
        {
            return false;
        }

        accountId = StorageAccountId.From(id);
        return true;
    }

    private static void ReplaceNavigationItems(
        ObservableCollection<RemoteAccountNavigationItem> target,
        IEnumerable<StorageAccountSummary> accounts)
    {
        target.Clear();
        foreach (var account in accounts)
        {
            target.Add(RemoteAccountNavigationItem.From(account, NavigationMenuKeys.RemoteAccountPrefix));
        }
    }

    private static RemotePath GetInitialPath(StorageAccountSummary account)
    {
        if (account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            account.ProviderId.Value.Equals("sftp", StringComparison.OrdinalIgnoreCase) &&
            account.ProviderConfig.TryGetValue("homePath", out var homePath) &&
            !string.IsNullOrWhiteSpace(homePath) &&
            homePath.Trim() != "/")
        {
            return new RemotePath(homePath, RemotePathKind.Folder);
        }

        return RemotePath.Root;
    }

    private static bool IsFtpOrSftp(StorageAccountSummary account)
    {
        return account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            (account.ProviderId.Value.Equals("ftp", StringComparison.OrdinalIgnoreCase) ||
             account.ProviderId.Value.Equals("sftp", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWebDav(StorageAccountSummary account)
    {
        return account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            account.ProviderId.Value.Equals("webdav", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record RemoteAccountNavigationItem(string Header, string ItemKey)
{
    public static RemoteAccountNavigationItem From(StorageAccountSummary account, string keyPrefix)
    {
        var header = account.ProviderCategory == StorageProviderCategory.ObjectStorage
            ? $"{account.DisplayName} ({account.ProviderId})"
            : $"{account.DisplayName} ({FormatFileTransferProvider(account.ProviderId)})";

        return new RemoteAccountNavigationItem(header, $"{keyPrefix}{account.Id}");
    }

    private static string FormatFileTransferProvider(StorageProviderId providerId)
    {
        var value = providerId.ToString();
        return value.Equals("sftp", StringComparison.OrdinalIgnoreCase)
            ? "SFTP"
            : value.Equals("ftp", StringComparison.OrdinalIgnoreCase)
                ? "FTP"
                : value.Equals("webdav", StringComparison.OrdinalIgnoreCase)
                    ? "WebDAV"
                    : value;
    }
}

