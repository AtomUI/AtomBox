using System.Collections.ObjectModel;
using AtomBox.Application.Accounts;
using AtomBox.Core.Accounts;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Navigation;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class AccountManagementViewModel : ViewModelBase
{
    private readonly AccountAppService _accounts;
    private readonly IAccountDialogWorkflow _accountDialogWorkflow;
    private readonly IDialogService _dialogs;
    private StorageAccountId? _selectedAccountId;
    private AccountRowViewModel? _selectedAccount;
    private string _statusMessage = string.Empty;
    private bool _isLoading;

    public AccountManagementViewModel(
        AccountAppService accounts,
        IAccountDialogWorkflow accountDialogWorkflow,
        IDialogService dialogs)
    {
        _accounts = accounts;
        _accountDialogWorkflow = accountDialogWorkflow;
        _dialogs = dialogs;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        AddAccountCommand = new AsyncRelayCommand(_ => AddAccountAsync());
        SelectAccountCommand = new AsyncRelayCommand(parameter =>
        {
            if (parameter is AccountRowViewModel account)
            {
                SelectedAccount = account;
            }

            return Task.CompletedTask;
        });
        EditAccountCommand = new AsyncRelayCommand(EditSelectedAccountAsync, parameter => parameter is AccountRowViewModel || SelectedAccount is not null);
        DeleteAccountCommand = new AsyncRelayCommand(DeleteSelectedAccountAsync, parameter => parameter is AccountRowViewModel || SelectedAccount is not null);
        _ = RefreshAsync();
    }

    public string Title => "账号管理";

    public ObservableCollection<AccountRowViewModel> Accounts { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand AddAccountCommand { get; }

    public AsyncRelayCommand SelectAccountCommand { get; }

    public AsyncRelayCommand EditAccountCommand { get; }

    public AsyncRelayCommand DeleteAccountCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
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
                EditAccountCommand.RaiseCanExecuteChanged();
                DeleteAccountCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void ApplyNavigationParameter(AccountManagementNavigationParameter parameter)
    {
        SelectedAccountId = parameter.SelectedAccountId;
        SelectRequestedAccount();
    }

    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = string.Empty;

        var result = await _accounts.ListAsync(new ListStorageAccountsRequest()).ConfigureAwait(true);
        Accounts.Clear();

        if (result.IsFailure)
        {
            StatusMessage = result.Error?.Message ?? "账号列表加载失败。";
            IsLoading = false;
            OnPropertyChanged(nameof(HasAccounts));
            return;
        }

        foreach (var account in result.GetValueOrThrow())
        {
            Accounts.Add(AccountRowViewModel.From(account));
        }

        SelectRequestedAccount();
        StatusMessage = string.Empty;
        IsLoading = false;
        OnPropertyChanged(nameof(HasAccounts));
    }

    private async Task AddAccountAsync()
    {
        var result = await _accountDialogWorkflow.AddAccountAsync().ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = result.Error?.Message ?? "账号保存失败。";
            return;
        }

        var account = result.GetValueOrThrow();
        if (account is null)
        {
            return;
        }

        SelectedAccountId = account.Id;
        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = $"已新增账号：{account.DisplayName}";
    }

    private async Task EditSelectedAccountAsync(object? parameter)
    {
        if (parameter is AccountRowViewModel accountToEdit)
        {
            SelectedAccount = accountToEdit;
        }

        if (SelectedAccount is null)
        {
            return;
        }

        var result = await _accountDialogWorkflow.EditAccountAsync(SelectedAccount.Account).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = result.Error?.Message ?? "账号保存失败。";
            return;
        }

        var account = result.GetValueOrThrow();
        if (account is null)
        {
            return;
        }

        SelectedAccountId = account.Id;
        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = $"已更新账号：{account.DisplayName}";
    }

    private async Task DeleteSelectedAccountAsync(object? parameter)
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
            "删除远程连接",
            $"确定要删除账号 \"{account.DisplayName}\" 吗？\n\nProvider：{account.ProviderId}\n此操作会移除账号配置，并安排对应凭据清理。",
            "删除",
            "取消")).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var result = await _accountDialogWorkflow.DeleteAccountAsync(account).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = result.Error?.Message ?? "账号删除失败。";
            return;
        }

        SelectedAccountId = null;
        SelectedAccount = null;
        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = $"已删除账号：{account.DisplayName}";
    }

    private void SelectRequestedAccount()
    {
        if (SelectedAccountId is null)
        {
            return;
        }

        SelectedAccount = Accounts.FirstOrDefault(account => account.Id == SelectedAccountId.Value);
    }
}

public sealed record AccountRowViewModel(
    StorageAccountSummary Account,
    StorageAccountId Id,
    string DisplayName,
    string ProviderType,
    string AddressOrProvider)
{
    public static AccountRowViewModel From(StorageAccountSummary account)
    {
        return new AccountRowViewModel(
            account,
            account.Id,
            account.DisplayName,
            FormatCategory(account),
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
}
