using System.Collections.ObjectModel;
using AtomBox.Application.Accounts;
using AtomBox.Application.State;
using AtomBox.Core.Accounts;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.ViewModels;
using AtomUI.City.Mvvm;
using AtomUI.City.State;
using CommunityToolkit.Mvvm.Input;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class AccountManagementViewModel : DesktopViewModelBase
{
    private readonly AccountAppService _accounts;
    private readonly IAtomBoxApplicationState _applicationState;
    private readonly IAccountDialogWorkflow _accountDialogWorkflow;
    private readonly IDialogService _dialogs;
    private readonly IAtomBoxLocalizedText _localization;
    private readonly CityUiDispatcher _dispatcher;
    private StorageAccountId? _selectedAccountId;
    private AccountRowViewModel? _selectedAccount;
    private LocalizedText? _statusMessage;
    private bool _isLoading;

    public AccountManagementViewModel(
        AccountAppService accounts,
        IAtomBoxApplicationState applicationState,
        IAccountDialogWorkflow accountDialogWorkflow,
        IDialogService dialogs,
        IAtomBoxLocalizedText localization,
        DesktopCommandFactory commands,
        CityUiDispatcher dispatcher)
    {
        _accounts = accounts;
        _applicationState = applicationState;
        _accountDialogWorkflow = accountDialogWorkflow;
        _dialogs = dialogs;
        _localization = localization;
        _dispatcher = dispatcher;
        RefreshCommand = commands.CreateAsync(
            this,
            nameof(RefreshCommand),
            RefreshAsync);
        AddAccountCommand = commands.CreateAsync(
            this,
            nameof(AddAccountCommand),
            AddAccountAsync);
        SelectAccountCommand = commands.Create<AccountRowViewModel>(
            this,
            nameof(SelectAccountCommand),
            account =>
        {
            if (account is not null)
            {
                SelectedAccount = account;
            }
        });
        EditAccountCommand = commands.CreateAsync<AccountRowViewModel>(
            this,
            nameof(EditAccountCommand),
            EditSelectedAccountAsync,
            account => account is not null || SelectedAccount is not null);
        DeleteAccountCommand = commands.CreateAsync<AccountRowViewModel>(
            this,
            nameof(DeleteAccountCommand),
            DeleteSelectedAccountAsync,
            account => account is not null || SelectedAccount is not null);
    }

    public string Title => _localization.GetString("Account", "Title");

    public ObservableCollection<AccountRowViewModel> Accounts { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand AddAccountCommand { get; }

    public IRelayCommand<AccountRowViewModel> SelectAccountCommand { get; }

    public IAsyncRelayCommand<AccountRowViewModel> EditAccountCommand { get; }

    public IAsyncRelayCommand<AccountRowViewModel> DeleteAccountCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage?.Resolve(_localization) ?? string.Empty;
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasAccounts => Accounts.Count > 0;

    public StorageAccountId? SelectedAccountId
    {
        get => _selectedAccountId;
        private set => SetProperty(ref _selectedAccountId, value);
    }

    public AccountRowViewModel? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                SelectedAccountId = value?.Id;
                EditAccountCommand.NotifyCanExecuteChanged();
                DeleteAccountCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public void ApplyNavigationParameter(AccountManagementNavigationParameter parameter)
    {
        SelectedAccountId = parameter.SelectedAccountId;
        SelectRequestedAccount();
    }

    protected override ValueTask OnActivatedAsync(
        ActivationContext context,
        CancellationToken cancellationToken)
    {
        _localization.LanguageChanged += HandleLanguageChanged;
        context.Scope.Add(EventSubscription.Create(
            () => _localization.LanguageChanged -= HandleLanguageChanged));
        context.Scope.Add(_applicationState.Accounts.Observe(
            args => ApplyAccountCatalog(args.NewValue),
            StateSubscriptionOptions.Dispatcher(_dispatcher)));
        return ValueTask.CompletedTask;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        SetStatus(null);

        var result = await _accounts.ListAsync(new ListStorageAccountsRequest(), cancellationToken).ConfigureAwait(true);
        if (result.IsFailure)
        {
            return;
        }
    }

    private async Task AddAccountAsync(CancellationToken cancellationToken)
    {
        var result = await _accountDialogWorkflow.AddAccountAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        if (result.IsFailure)
        {
            SetStatus(new("Account", "AccountSaveFailed"));
            return;
        }

        var account = result.GetValueOrThrow();

        SelectedAccountId = account.Id;
        SelectRequestedAccount();
        SetStatus(new("Account", "AccountAdded", account.DisplayName));
    }

    private async Task EditSelectedAccountAsync(
        AccountRowViewModel? parameter,
        CancellationToken cancellationToken)
    {
        if (parameter is AccountRowViewModel accountToEdit)
        {
            SelectedAccount = accountToEdit;
        }

        if (SelectedAccount is null)
        {
            return;
        }

        var result = await _accountDialogWorkflow
            .EditAccountAsync(SelectedAccount.Account, cancellationToken)
            .ConfigureAwait(true);
        if (result is null)
        {
            return;
        }

        if (result.IsFailure)
        {
            SetStatus(new("Account", "AccountSaveFailed"));
            return;
        }

        var account = result.GetValueOrThrow();

        SelectedAccountId = account.Id;
        SelectRequestedAccount();
        SetStatus(new("Account", "AccountUpdated", account.DisplayName));
    }

    private async Task DeleteSelectedAccountAsync(
        AccountRowViewModel? parameter,
        CancellationToken cancellationToken)
    {
        if (parameter is AccountRowViewModel accountToDelete)
        {
            SelectedAccount = accountToDelete;
        }

        if (SelectedAccount is null)
        {
            return;
        }

        var account = SelectedAccount.Account;
        var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
            _localization.GetString("Account", "DeleteConnectionTitle"),
            _localization.Format("Account", "DeleteConnectionMessage", account.DisplayName, account.ProviderId),
            _localization.GetString("Common", "Delete"),
            _localization.GetString("Common", "Cancel"))).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var result = await _accountDialogWorkflow.DeleteAccountAsync(account, cancellationToken).ConfigureAwait(true);
        if (result.IsFailure)
        {
            SetStatus(new("Account", "AccountDeleteFailed"));
            return;
        }

        SelectedAccountId = null;
        SelectedAccount = null;
        SetStatus(new("Account", "AccountDeleted", account.DisplayName));
    }

    private void ApplyAccountCatalog(AccountCatalogState state)
    {
        Accounts.Clear();

        if (state.Status == ApplicationProjectionStatus.Faulted)
        {
            SetStatus(new("Account", "AccountListLoadFailed"));
            IsLoading = false;
            OnPropertyChanged(nameof(HasAccounts));
            return;
        }

        if (state.Status == ApplicationProjectionStatus.Uninitialized)
        {
            IsLoading = true;
            OnPropertyChanged(nameof(HasAccounts));
            return;
        }

        foreach (var account in state.Accounts)
        {
            Accounts.Add(AccountRowViewModel.From(account));
        }

        SelectRequestedAccount();
        SetStatus(null);
        IsLoading = false;
        OnPropertyChanged(nameof(HasAccounts));
    }

    private void SelectRequestedAccount()
    {
        if (SelectedAccountId is null)
        {
            return;
        }

        SelectedAccount = Accounts.FirstOrDefault(account => account.Id == SelectedAccountId.Value);
    }

    private void SetStatus(LocalizedText? value)
    {
        _statusMessage = value;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void HandleLanguageChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusMessage));
    }
}

public sealed record AccountRowViewModel(
    StorageAccountSummary Account,
    StorageAccountId Id,
    string DisplayName,
    string ProviderType,
    string ProviderTagColor,
    string AddressOrProvider)
{
    public static AccountRowViewModel From(StorageAccountSummary account)
    {
        return new AccountRowViewModel(
            account,
            account.Id,
            account.DisplayName,
            FormatCategory(account),
            FormatCategoryTagColor(account),
            FormatAddressOrProvider(account));
    }

    private static string FormatCategory(StorageAccountSummary account)
    {
        if (account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            account.ProviderId.Value.Equals("webdav", StringComparison.OrdinalIgnoreCase))
        {
            return "WebDAV";
        }

        return account.ProviderCategory switch
        {
            StorageProviderCategory.ObjectStorage => "OSS",
            StorageProviderCategory.FileTransfer => "FTP / SFTP",
            _ => account.ProviderCategory.ToString()
        };
    }

    private static string FormatAddressOrProvider(StorageAccountSummary account)
    {
        if (account.ProviderCategory == StorageProviderCategory.ObjectStorage)
        {
            return string.IsNullOrWhiteSpace(account.Region)
                ? account.ProviderId.ToString()
                : $"{account.ProviderId} / {account.Region}";
        }

        if (!string.IsNullOrWhiteSpace(account.Endpoint))
        {
            return account.ProviderConfig.TryGetValue("port", out var port) && !string.IsNullOrWhiteSpace(port)
                ? $"{account.Endpoint}:{port}"
                : account.Endpoint;
        }

        return account.ProviderId.ToString();
    }

    private static string FormatCategoryTagColor(StorageAccountSummary account)
    {
        if (account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            account.ProviderId.Value.Equals("webdav", StringComparison.OrdinalIgnoreCase))
        {
            return "Purple";
        }

        return account.ProviderCategory switch
        {
            StorageProviderCategory.ObjectStorage => "Blue",
            StorageProviderCategory.FileTransfer => "Cyan",
            _ => "Info"
        };
    }
}
