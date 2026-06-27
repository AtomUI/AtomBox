using System.Collections.ObjectModel;
using AtomBox.Application.Accounts;
using AtomBox.Core.Accounts;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.ViewModels;
using Avalonia.Threading;

namespace AtomBox.Desktop.Shell;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IAccountDialogWorkflow _accountDialogWorkflow;
    private readonly AccountAppService _accounts;
    private readonly IDesktopPreferencesService _preferences;
    private readonly Dictionary<StorageAccountId, StorageAccountSummary> _navigationAccounts = [];
    private object? _currentViewModel;
    private string? _currentMenuKey;

    public MainWindowViewModel(
        INavigationService navigationService,
        IAccountDialogWorkflow accountDialogWorkflow,
        AccountAppService accounts,
        IDesktopPreferencesService preferences,
        StatusBarViewModel statusBar)
    {
        _navigationService = navigationService;
        _accountDialogWorkflow = accountDialogWorkflow;
        _accounts = accounts;
        _preferences = preferences;
        StatusBar = statusBar;

        _accountDialogWorkflow.AccountsChanged += (_, _) => RefreshRemoteAccountMenuOnUiThread();

        _navigationService.CurrentViewModelChanged += (_, _) =>
        {
            CurrentViewModel = _navigationService.CurrentViewModel;
        };
        _navigationService.CurrentMenuKeyChanged += (_, _) =>
        {
            CurrentMenuKey = _navigationService.CurrentMenuKey;
        };

        _ = InitializeAsync();
    }

    public object? CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
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

    public Task NavigateByMenuKeyAsync(string? itemKey)
    {
        if (TryParseRemoteAccountMenuKey(itemKey, out var accountId) &&
            _navigationAccounts.TryGetValue(accountId, out var account))
        {
            return NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(
                    ToResourceGroup(account),
                    account.Id,
                    GetInitialPath(account)));
        }

        return itemKey switch
        {
            NavigationMenuKeys.Home => NavigateAsync(NavigationTarget.Home),
            NavigationMenuKeys.RemoteStorage => NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(NavigationResourceGroup.All)),
            NavigationMenuKeys.RemoteObjectStorage => NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(NavigationResourceGroup.ObjectStorage)),
            NavigationMenuKeys.RemoteFileTransfer => NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(NavigationResourceGroup.FileTransfer)),
            NavigationMenuKeys.RemoteWebDav => NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(NavigationResourceGroup.WebDav)),
            NavigationMenuKeys.AddRemote => AddAccountAndNavigateAsync(null),
            NavigationMenuKeys.TransferQueue => NavigateAsync(NavigationTarget.TransferQueue),
            NavigationMenuKeys.TransferHistory => NavigateAsync(NavigationTarget.TransferHistory),
            NavigationMenuKeys.Settings => NavigateAsync(NavigationTarget.Settings),
            NavigationMenuKeys.AccountManagement => NavigateAsync(NavigationTarget.AccountManagement),
            _ => Task.CompletedTask
        };
    }

    public async Task RefreshRemoteAccountMenuAsync()
    {
        var result = await _accounts.ListAsync(new ListStorageAccountsRequest()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            ObjectStorageAccounts.Clear();
            FileTransferAccounts.Clear();
            WebDavAccounts.Clear();
            _navigationAccounts.Clear();
            RemoteAccountMenuRefreshed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var accounts = result.GetValueOrThrow()
            .OrderBy(account => account.ProviderCategory)
            .ThenBy(account => account.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

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

    private void RefreshRemoteAccountMenuOnUiThread()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await RefreshRemoteAccountMenuAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        });
    }

    private async Task InitializeAsync()
    {
        await RefreshRemoteAccountMenuAsync().ConfigureAwait(true);
        var preferences = await _preferences.GetAsync().ConfigureAwait(true);
        await NavigateStartupPageAsync(preferences.StartupPage).ConfigureAwait(true);
    }

    private Task NavigateStartupPageAsync(StartupPageOption startupPage)
    {
        return startupPage switch
        {
            StartupPageOption.Home => NavigateAsync(NavigationTarget.Home),
            StartupPageOption.TransferQueue => NavigateAsync(NavigationTarget.TransferQueue),
            StartupPageOption.AccountManagement => NavigateAsync(NavigationTarget.AccountManagement),
            _ => NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(NavigationResourceGroup.All))
        };
    }

    private async Task AddAccountAndNavigateAsync(StorageProviderCategory? category)
    {
        var result = await _accountDialogWorkflow.AddAccountAsync(category).ConfigureAwait(true);
        if (result.IsFailure || result.GetValueOrThrow() is not { } account)
        {
            return;
        }

        await NavigateAsync(
            NavigationTarget.RemoteBrowser,
            new RemoteBrowserNavigationParameter(
                ToResourceGroup(account),
                account.Id,
                GetInitialPath(account))).ConfigureAwait(true);
        await RefreshRemoteAccountMenuAsync().ConfigureAwait(true);
    }

    private Task NavigateAsync(NavigationTarget target, object? parameter = null)
    {
        return _navigationService.NavigateAsync(target, parameter);
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

