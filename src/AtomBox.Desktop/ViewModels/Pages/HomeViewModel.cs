using System.Collections.ObjectModel;
using AtomBox.Application.Accounts;
using AtomBox.Application.Transfers;
using AtomBox.Core.Accounts;
using AtomBox.Core.Transfers;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.ViewModels;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class HomeViewModel : LocalizedViewModelBase
{
    private readonly AccountAppService _accounts;
    private readonly TransferAppService _transfers;
    private readonly INavigationService _navigation;
    private LocalizedText _accountSummary = new("Shell", "AccountCount", 0);
    private LocalizedText _queueSummary = new("Transfer", "RunningPendingSummary", 0, 0);
    private LocalizedText _historySummary = new("Transfer", "RecentlyCompleted", 0);
    private IReadOnlyList<TransferStateSnapshot> _recentSnapshots = [];
    private bool _isRecentHistoryVisible;
    private bool _isRecentHistoryEmpty = true;

    public HomeViewModel(
        AccountAppService accounts,
        TransferAppService transfers,
        INavigationService navigation,
        ILocalizationService localization)
        : base(localization)
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

    public string Subtitle => Localization.GetString("Shell", "HomeSubtitle");

    public string AccountSummary
    {
        get => _accountSummary.Resolve(Localization);
    }

    public string QueueSummary
    {
        get => _queueSummary.Resolve(Localization);
    }

    public string HistorySummary
    {
        get => _historySummary.Resolve(Localization);
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
            SetAccountSummary(new("Shell", "AccountsUnavailable"));
            return;
        }

        var accounts = result.GetValueOrThrow();
        var objectStorageCount = accounts.Count(account => account.ProviderCategory == StorageProviderCategory.ObjectStorage);
        var webDavCount = accounts.Count(account => account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            account.ProviderId.Value.Equals("webdav", StringComparison.OrdinalIgnoreCase));
        var fileTransferCount = accounts.Count(account => account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            !account.ProviderId.Value.Equals("webdav", StringComparison.OrdinalIgnoreCase));
        SetAccountSummary(new("Shell", "AccountSummary", accounts.Count, objectStorageCount, fileTransferCount, webDavCount));
    }

    private async Task LoadTransferSummaryAsync()
    {
        var queueResult = await _transfers.GetQueueAsync(new GetTransferQueueRequest()).ConfigureAwait(true);
        if (queueResult.IsFailure)
        {
            SetQueueSummary(new("Transfer", "QueueUnavailable"));
        }
        else
        {
            var queue = queueResult.GetValueOrThrow().Tasks;
            var runningCount = queue.Count(task => task.Task.Status == TransferStatus.Running);
            var pendingCount = queue.Count(task => task.Task.Status == TransferStatus.Pending);
            SetQueueSummary(new("Transfer", "RunningPendingSummary", runningCount, pendingCount));
        }

        var historyResult = await _transfers.GetHistoryAsync(new GetTransferHistoryRequest()).ConfigureAwait(true);
        RecentTransfers.Clear();
        if (historyResult.IsFailure)
        {
            SetHistorySummary(new("Transfer", "HistoryUnavailable"));
            IsRecentHistoryVisible = false;
            IsRecentHistoryEmpty = true;
            return;
        }

        var history = historyResult.GetValueOrThrow().Tasks;
        var succeededCount = history.Count(task => task.Task.Status == TransferStatus.Succeeded);
        SetHistorySummary(new("Transfer", "RecentlyCompleted", succeededCount));
        _recentSnapshots = history.Take(5).ToArray();

        foreach (var snapshot in _recentSnapshots)
        {
            RecentTransfers.Add(HomeRecentTransferViewModel.From(snapshot, Localization));
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

    private void SetAccountSummary(LocalizedText value) { _accountSummary = value; OnPropertyChanged(nameof(AccountSummary)); }
    private void SetQueueSummary(LocalizedText value) { _queueSummary = value; OnPropertyChanged(nameof(QueueSummary)); }
    private void SetHistorySummary(LocalizedText value) { _historySummary = value; OnPropertyChanged(nameof(HistorySummary)); }

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(AccountSummary));
        OnPropertyChanged(nameof(QueueSummary));
        OnPropertyChanged(nameof(HistorySummary));
        RecentTransfers.Clear();
        foreach (var snapshot in _recentSnapshots)
        {
            RecentTransfers.Add(HomeRecentTransferViewModel.From(snapshot, Localization));
        }
    }
}

public sealed record HomeRecentTransferViewModel(
    string FileName,
    string Direction,
    string Result,
    string ResultTagColor,
    string CompletedAt)
{
    public static HomeRecentTransferViewModel From(TransferStateSnapshot snapshot, ILocalizationService localization)
    {
        var task = snapshot.Task;
        var fileName = task.Direction == TransferDirection.Upload
            ? task.LocalPath.GetFileName()
            : task.RemotePath.Name;

        return new HomeRecentTransferViewModel(
            string.IsNullOrWhiteSpace(fileName) ? localization.GetString("Transfer", "Unnamed") : fileName,
            localization.GetString("Transfer", task.Direction == TransferDirection.Upload ? "Upload" : "Download"),
            FormatStatus(task.Status, localization),
            FormatStatusTagColor(task.Status),
            task.UpdatedAt.LocalDateTime.ToString("MM-dd HH:mm"));
    }

    private static string FormatStatus(TransferStatus status, ILocalizationService localization)
    {
        return status switch
        {
            TransferStatus.Succeeded => localization.GetString("Transfer", "Succeeded"),
            TransferStatus.Failed => localization.GetString("Transfer", "Failed"),
            TransferStatus.Canceled => localization.GetString("Transfer", "Canceled"),
            TransferStatus.Interrupted => localization.GetString("Transfer", "Interrupted"),
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
