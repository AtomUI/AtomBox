using System.Collections.ObjectModel;
using AtomBox.Application.Accounts;
using AtomBox.Application.Transfers;
using AtomBox.Core.Accounts;
using AtomBox.Core.Transfers;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.ViewModels;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class HomeViewModel : ViewModelBase
{
    private readonly AccountAppService _accounts;
    private readonly TransferAppService _transfers;
    private readonly INavigationService _navigation;
    private string _accountSummary = "账号 0 个";
    private string _queueSummary = "运行中 0 个";
    private string _historySummary = "最近完成 0 个";
    private bool _isRecentHistoryVisible;
    private bool _isRecentHistoryEmpty = true;

    public HomeViewModel(
        AccountAppService accounts,
        TransferAppService transfers,
        INavigationService navigation)
    {
        _accounts = accounts;
        _transfers = transfers;
        _navigation = navigation;
        OpenRemoteStorageCommand = new AsyncRelayCommand(_ => NavigateAsync(NavigationTarget.RemoteBrowser));
        OpenTransferQueueCommand = new AsyncRelayCommand(_ => NavigateAsync(NavigationTarget.TransferQueue));
        OpenAccountManagementCommand = new AsyncRelayCommand(_ => NavigateAsync(NavigationTarget.AccountManagement));
        _ = RefreshAsync();
    }

    public string Title => "AtomBox";

    public string Subtitle => "远程存储与文件传输工作台";

    public string AccountSummary
    {
        get => _accountSummary;
        private set => SetProperty(ref _accountSummary, value);
    }

    public string QueueSummary
    {
        get => _queueSummary;
        private set => SetProperty(ref _queueSummary, value);
    }

    public string HistorySummary
    {
        get => _historySummary;
        private set => SetProperty(ref _historySummary, value);
    }

    public bool IsRecentHistoryVisible
    {
        get => _isRecentHistoryVisible;
        private set => SetProperty(ref _isRecentHistoryVisible, value);
    }

    public bool IsRecentHistoryEmpty
    {
        get => _isRecentHistoryEmpty;
        private set => SetProperty(ref _isRecentHistoryEmpty, value);
    }

    public ObservableCollection<HomeRecentTransferViewModel> RecentTransfers { get; } = [];

    public AsyncRelayCommand OpenRemoteStorageCommand { get; }

    public AsyncRelayCommand OpenTransferQueueCommand { get; }

    public AsyncRelayCommand OpenAccountManagementCommand { get; }

    private async Task RefreshAsync()
    {
        await LoadAccountSummaryAsync().ConfigureAwait(true);
        await LoadTransferSummaryAsync().ConfigureAwait(true);
    }

    private async Task LoadAccountSummaryAsync()
    {
        var result = await _accounts.ListAsync(new ListStorageAccountsRequest()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            AccountSummary = "账号暂不可用";
            return;
        }

        var accounts = result.GetValueOrThrow();
        var objectStorageCount = accounts.Count(account => account.ProviderCategory == StorageProviderCategory.ObjectStorage);
        var webDavCount = accounts.Count(account => account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            account.ProviderId.Value.Equals("webdav", StringComparison.OrdinalIgnoreCase));
        var fileTransferCount = accounts.Count(account => account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            !account.ProviderId.Value.Equals("webdav", StringComparison.OrdinalIgnoreCase));
        AccountSummary = $"账号 {accounts.Count} 个 · OSS {objectStorageCount} 个 · FTP/SFTP {fileTransferCount} 个 · WebDAV {webDavCount} 个";
    }

    private async Task LoadTransferSummaryAsync()
    {
        var queueResult = await _transfers.GetQueueAsync(new GetTransferQueueRequest()).ConfigureAwait(true);
        if (queueResult.IsFailure)
        {
            QueueSummary = "队列暂不可用";
        }
        else
        {
            var queue = queueResult.GetValueOrThrow().Tasks;
            var runningCount = queue.Count(task => task.Task.Status == TransferStatus.Running);
            var pendingCount = queue.Count(task => task.Task.Status == TransferStatus.Pending);
            QueueSummary = $"运行中 {runningCount} 个 · 等待中 {pendingCount} 个";
        }

        var historyResult = await _transfers.GetHistoryAsync(new GetTransferHistoryRequest()).ConfigureAwait(true);
        RecentTransfers.Clear();
        if (historyResult.IsFailure)
        {
            HistorySummary = "历史暂不可用";
            IsRecentHistoryVisible = false;
            IsRecentHistoryEmpty = true;
            return;
        }

        var history = historyResult.GetValueOrThrow().Tasks;
        var succeededCount = history.Count(task => task.Task.Status == TransferStatus.Succeeded);
        HistorySummary = $"最近完成 {succeededCount} 个";

        foreach (var snapshot in history.Take(5))
        {
            RecentTransfers.Add(HomeRecentTransferViewModel.From(snapshot));
        }

        IsRecentHistoryVisible = RecentTransfers.Count > 0;
        IsRecentHistoryEmpty = RecentTransfers.Count == 0;
    }

    private Task NavigateAsync(NavigationTarget target)
    {
        return target == NavigationTarget.RemoteBrowser
            ? _navigation.NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(NavigationResourceGroup.All))
            : _navigation.NavigateAsync(target);
    }
}

public sealed record HomeRecentTransferViewModel(
    string FileName,
    string Direction,
    string Result,
    string ResultTagColor,
    string CompletedAt)
{
    public static HomeRecentTransferViewModel From(TransferStateSnapshot snapshot)
    {
        var task = snapshot.Task;
        var fileName = task.Direction == TransferDirection.Upload
            ? task.LocalPath.GetFileName()
            : task.RemotePath.Name;

        return new HomeRecentTransferViewModel(
            string.IsNullOrWhiteSpace(fileName) ? "(未命名)" : fileName,
            task.Direction == TransferDirection.Upload ? "上传" : "下载",
            FormatStatus(task.Status),
            FormatStatusTagColor(task.Status),
            task.UpdatedAt.LocalDateTime.ToString("MM-dd HH:mm"));
    }

    private static string FormatStatus(TransferStatus status)
    {
        return status switch
        {
            TransferStatus.Succeeded => "成功",
            TransferStatus.Failed => "失败",
            TransferStatus.Canceled => "取消",
            TransferStatus.Interrupted => "已中断",
            _ => status.ToString()
        };
    }

    private static string FormatStatusTagColor(TransferStatus status)
    {
        return status switch
        {
            TransferStatus.Succeeded => "Success",
            TransferStatus.Failed => "Error",
            TransferStatus.Interrupted => "Warning",
            TransferStatus.Canceled => "Grey",
            _ => "Info"
        };
    }
}
