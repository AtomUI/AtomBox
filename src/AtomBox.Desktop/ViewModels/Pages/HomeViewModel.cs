using System.Collections.ObjectModel;
using AtomBox.Application.Accounts;
using AtomBox.Application.Transfers;
using AtomBox.Core.Accounts;
using AtomBox.Core.Transfers;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.ViewModels;
using AtomUI.City.Mvvm;
using CommunityToolkit.Mvvm.Input;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class HomeViewModel : ViewModelBase
{
    private readonly AccountAppService _accounts;
    private readonly TransferAppService _transfers;
    private readonly INavigationService _navigation;
    private readonly IAtomBoxLocalizedText _localization;
    private readonly CityUiDispatcher _dispatcher;
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
        IAtomBoxLocalizedText localization,
        DesktopCommandFactory commands,
        CityUiDispatcher dispatcher)
    {
        _accounts = accounts;
        _transfers = transfers;
        _navigation = navigation;
        _localization = localization;
        _dispatcher = dispatcher;
        OpenRemoteStorageCommand = commands.CreateAsync(
            this,
            nameof(OpenRemoteStorageCommand),
            cancellationToken => NavigateAsync(NavigationTarget.RemoteBrowser, cancellationToken));
        OpenTransferQueueCommand = commands.CreateAsync(
            this,
            nameof(OpenTransferQueueCommand),
            cancellationToken => NavigateAsync(NavigationTarget.TransferQueue, cancellationToken));
        OpenAccountManagementCommand = commands.CreateAsync(
            this,
            nameof(OpenAccountManagementCommand),
            cancellationToken => NavigateAsync(NavigationTarget.AccountManagement, cancellationToken));
    }

    public string Title => "AtomBox";

    public string Subtitle => _localization.GetString("Shell", "HomeSubtitle");

    public string AccountSummary
    {
        get => _accountSummary.Resolve(_localization);
    }

    public string QueueSummary
    {
        get => _queueSummary.Resolve(_localization);
    }

    public string HistorySummary
    {
        get => _historySummary.Resolve(_localization);
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

    public IAsyncRelayCommand OpenRemoteStorageCommand { get; }

    public IAsyncRelayCommand OpenTransferQueueCommand { get; }

    public IAsyncRelayCommand OpenAccountManagementCommand { get; }

    protected override ValueTask OnActivatedAsync(
        ActivationContext context,
        CancellationToken cancellationToken)
    {
        _localization.LanguageChanged += HandleLanguageChanged;
        context.Scope.Add(EventSubscription.Create(
            () => _localization.LanguageChanged -= HandleLanguageChanged));
        return _dispatcher.PostAsync(
            async token => await RefreshAsync(token).ConfigureAwait(true),
            cancellationToken);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await LoadAccountSummaryAsync(cancellationToken).ConfigureAwait(true);
        await LoadTransferSummaryAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadAccountSummaryAsync(CancellationToken cancellationToken)
    {
        var result = await _accounts.ListAsync(new ListStorageAccountsRequest(), cancellationToken).ConfigureAwait(true);
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

    private async Task LoadTransferSummaryAsync(CancellationToken cancellationToken)
    {
        var queueResult = await _transfers.GetQueueAsync(new GetTransferQueueRequest(), cancellationToken).ConfigureAwait(true);
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

        var historyResult = await _transfers.GetHistoryAsync(new GetTransferHistoryRequest(), cancellationToken).ConfigureAwait(true);
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
            RecentTransfers.Add(HomeRecentTransferViewModel.From(snapshot, _localization));
        }

        IsRecentHistoryVisible = RecentTransfers.Count > 0;
        IsRecentHistoryEmpty = RecentTransfers.Count == 0;
    }

    private Task NavigateAsync(NavigationTarget target, CancellationToken cancellationToken)
    {
        return target == NavigationTarget.RemoteBrowser
            ? _navigation.NavigateAsync(
                NavigationTarget.RemoteBrowser,
                new RemoteBrowserNavigationParameter(NavigationResourceGroup.All),
                cancellationToken)
            : _navigation.NavigateAsync(target, cancellationToken: cancellationToken);
    }

    private void SetAccountSummary(LocalizedText value) { _accountSummary = value; OnPropertyChanged(nameof(AccountSummary)); }
    private void SetQueueSummary(LocalizedText value) { _queueSummary = value; OnPropertyChanged(nameof(QueueSummary)); }
    private void SetHistorySummary(LocalizedText value) { _historySummary = value; OnPropertyChanged(nameof(HistorySummary)); }

    private void HandleLanguageChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(AccountSummary));
        OnPropertyChanged(nameof(QueueSummary));
        OnPropertyChanged(nameof(HistorySummary));
        RecentTransfers.Clear();
        foreach (var snapshot in _recentSnapshots)
        {
            RecentTransfers.Add(HomeRecentTransferViewModel.From(snapshot, _localization));
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
    public static HomeRecentTransferViewModel From(TransferStateSnapshot snapshot, IAtomBoxLocalizedText localization)
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

    private static string FormatStatus(TransferStatus status, IAtomBoxLocalizedText localization)
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
