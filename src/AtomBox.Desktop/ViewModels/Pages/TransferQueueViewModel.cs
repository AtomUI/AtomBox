using System.Collections.ObjectModel;
using System.Windows.Input;
using AtomBox.Application.Accounts;
using AtomBox.Application.Transfers;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.Shell;
using AtomBox.Providers.Common;
using Avalonia.Threading;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class TransferQueueViewModel : LocalizedViewModelBase
{
    private readonly TransferAppService _transfers;
    private readonly AccountAppService _accounts;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly StatusBarViewModel _statusBar;
    private readonly DispatcherTimer _refreshTimer;
    private static readonly Lock CompletionNotificationLock = new();
    private static readonly HashSet<TransferTaskId> CompletionNotificationsShown = [];
    private readonly Dictionary<TransferTaskId, TransferTaskRowViewModel> _lastObservedTasks = [];
    private readonly Dictionary<StorageAccountId, StorageProviderId> _providerIdsByAccount = [];
    private LocalizedText _statusMessage = new("Transfer", "LoadingQueue");
    private IReadOnlyList<TransferStateSnapshot> _currentSnapshots = [];
    private bool _isLoading;
    private bool _isRefreshing;
    private TransferTaskRowViewModel? _selectedTask;

    public TransferQueueViewModel(
        TransferAppService transfers,
        AccountAppService accounts,
        IMessageService messages,
        IDialogService dialogs,
        StatusBarViewModel statusBar,
        ILocalizationService localization)
        : base(localization)
    {
        _transfers = transfers;
        _accounts = accounts;
        _messages = messages;
        _dialogs = dialogs;
        _statusBar = statusBar;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        ExecuteTaskActionCommand = new AsyncRelayCommand(
            ExecuteTaskActionAsync,
            parameter => parameter is TransferTaskRowViewModel row && row.CanExecuteAction,
            allowConcurrentExecutions: true);
        CancelPendingTasksCommand = new AsyncRelayCommand(_ => CancelPendingTasksAsync(), _ => HasCancelableTasks);
        RetryFailedTasksCommand = new AsyncRelayCommand(_ => RetryFailedTasksAsync(), _ => HasRetryableTasks);
        ShowTaskDetailsCommand = new AsyncRelayCommand(
            ShowTaskDetailsAsync,
            parameter => parameter is TransferTaskRowViewModel { HasDetails: true });
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(showLoading: false).ConfigureAwait(true);
        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    public string Title => Localization.GetString("Transfer", "QueueTitle");

    public ObservableCollection<TransferTaskRowViewModel> Tasks { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ExecuteTaskActionCommand { get; }

    public AsyncRelayCommand CancelPendingTasksCommand { get; }

    public AsyncRelayCommand RetryFailedTasksCommand { get; }

    public AsyncRelayCommand ShowTaskDetailsCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage.Resolve(Localization);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasTasks => Tasks.Count > 0;

    public bool IsEmptyStateVisible => !IsLoading && !HasTasks;

    public bool HasCancelableTasks => Tasks.Any(task => task.Status is TransferStatus.Pending or TransferStatus.Running);

    public bool HasRetryableTasks => Tasks.Any(task => task.Status is TransferStatus.Failed or TransferStatus.Interrupted);

    public TransferTaskRowViewModel? SelectedTask
    {
        get => _selectedTask;
        set => SetProperty(ref _selectedTask, value);
    }

    public bool IsTaskListVisible => !IsLoading && HasTasks;

    private async Task RefreshAsync()
    {
        await RefreshAsync(showLoading: true).ConfigureAwait(true);
    }

    private async Task RefreshAsync(bool showLoading)
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        if (showLoading)
        {
            IsLoading = true;
            SetStatus(new("Transfer", "LoadingQueue"));
        }

        try
        {
            var result = await _transfers.GetQueueAsync(new GetTransferQueueRequest()).ConfigureAwait(true);
            Tasks.Clear();

            if (result.IsFailure)
            {
                SetStatus(new("Transfer", "QueueLoadFailed"));
                IsLoading = false;
                RaiseTaskCollectionStateChanged();
                return;
            }

            var snapshots = result.GetValueOrThrow().Tasks;
            _currentSnapshots = snapshots;
            await EnsureProviderIdsAsync(snapshots).ConfigureAwait(true);
            await NotifyCompletedTasksAsync(snapshots).ConfigureAwait(true);

            foreach (var task in snapshots)
            {
                Tasks.Add(CreateTaskRow(task));
            }

            SetStatus(Tasks.Count == 0
                ? new("Transfer", "NoQueueTasksLong")
                : new("Transfer", "QueueCount", Tasks.Count));
            IsLoading = false;
            RememberObservedTasks();
            RaiseTaskCollectionStateChanged();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task NotifyCompletedTasksAsync(IReadOnlyList<TransferStateSnapshot> currentSnapshots)
    {
        if (_lastObservedTasks.Count == 0)
        {
            return;
        }

        var currentTaskIds = currentSnapshots
            .Select(snapshot => snapshot.Task.Id)
            .ToHashSet();
        var completedCandidates = _lastObservedTasks.Values
            .Where(task => (task.Status is TransferStatus.Pending or TransferStatus.Running or TransferStatus.Canceling) &&
                           !currentTaskIds.Contains(task.TaskId) &&
                           !IsCompletionNotificationShown(task.TaskId))
            .ToArray();
        if (completedCandidates.Length == 0)
        {
            return;
        }

        var historyResult = await _transfers.GetHistoryAsync(new GetTransferHistoryRequest()).ConfigureAwait(true);
        if (historyResult.IsFailure)
        {
            return;
        }

        var succeededTasks = historyResult.GetValueOrThrow().Tasks
            .Where(snapshot => snapshot.Task.Status == TransferStatus.Succeeded)
            .ToDictionary(snapshot => snapshot.Task.Id);
        var completedTasks = new List<TransferTaskRowViewModel>();
        foreach (var candidate in completedCandidates)
        {
            if (!succeededTasks.TryGetValue(candidate.TaskId, out var snapshot))
            {
                continue;
            }

            if (!TryMarkCompletionNotificationShown(candidate.TaskId))
            {
                continue;
            }

            var completed = CreateTaskRow(snapshot);
            completedTasks.Add(completed);
        }

        foreach (var group in completedTasks.GroupBy(task => task.Direction))
        {
            var tasks = group.ToArray();
            var direction = group.Key;
            var message = tasks.Length == 1
                ? Localization.Format("Transfer", "TaskCompletedSingle", tasks[0].FileName, direction)
                : Localization.Format("Transfer", "TaskCompletedMultiple", tasks.Length, direction);
            _messages.Success(message);
        }
    }

    private void RememberObservedTasks()
    {
        _lastObservedTasks.Clear();
        foreach (var task in Tasks)
        {
            _lastObservedTasks[task.TaskId] = task;
        }
    }

    private static bool IsCompletionNotificationShown(TransferTaskId taskId)
    {
        lock (CompletionNotificationLock)
        {
            return CompletionNotificationsShown.Contains(taskId);
        }
    }

    private static bool TryMarkCompletionNotificationShown(TransferTaskId taskId)
    {
        lock (CompletionNotificationLock)
        {
            return CompletionNotificationsShown.Add(taskId);
        }
    }

    private void RaiseTaskCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(IsTaskListVisible));
        OnPropertyChanged(nameof(HasCancelableTasks));
        OnPropertyChanged(nameof(HasRetryableTasks));
        CancelPendingTasksCommand.RaiseCanExecuteChanged();
        RetryFailedTasksCommand.RaiseCanExecuteChanged();
    }

    private async Task ExecuteTaskActionAsync(object? parameter)
    {
        if (parameter is not TransferTaskRowViewModel row)
        {
            return;
        }

        if (row.Status == TransferStatus.Running && row.UsesBestEffortCancellation)
        {
            var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
                Localization.GetString("Transfer", "TryCancelTitle"),
                Localization.GetString("Transfer", "TryCancelMessage"),
                Localization.GetString("Transfer", "ContinueTryCancel"),
                Localization.GetString("Transfer", "Return"))).ConfigureAwait(true);
            if (!confirmed)
            {
                return;
            }
        }

        var result = row.Status switch
        {
            TransferStatus.Pending => await _transfers.CancelAsync(new CancelTransferTaskRequest(row.TaskId)).ConfigureAwait(true),
            TransferStatus.Running => await _transfers.CancelAsync(new CancelTransferTaskRequest(row.TaskId)).ConfigureAwait(true),
            TransferStatus.Failed or TransferStatus.Interrupted => await _transfers.RetryAsync(new RetryTransferTaskRequest(row.TaskId)).ConfigureAwait(true),
            _ => null
        };

        if (result is null)
        {
            _messages.Info(Localization.GetString("Transfer", "UnsupportedTaskAction"));
            return;
        }

        if (result.IsFailure)
        {
            SetStatus(new("Transfer", "TaskActionFailed"));
            return;
        }

        _statusBar.RequestQueueRefresh();
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task CancelPendingTasksAsync()
    {
        var taskIds = Tasks
            .Where(task => task.Status == TransferStatus.Pending)
            .Select(task => task.TaskId)
            .ToArray();
        if (taskIds.Length == 0)
        {
            _messages.Info(Localization.GetString("Transfer", "NoPendingTasks"));
            return;
        }

        foreach (var taskId in taskIds)
        {
            var result = await _transfers.CancelAsync(new CancelTransferTaskRequest(taskId)).ConfigureAwait(true);
            if (result.IsFailure)
            {
                SetStatus(new("Transfer", "CancelTaskFailed"));
                _messages.Error(StatusMessage);
                break;
            }
        }

        _statusBar.RequestQueueRefresh();
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task RetryFailedTasksAsync()
    {
        var taskIds = Tasks
            .Where(task => task.Status is TransferStatus.Failed or TransferStatus.Interrupted)
            .Select(task => task.TaskId)
            .ToArray();
        if (taskIds.Length == 0)
        {
            _messages.Info(Localization.GetString("Transfer", "NoRetryableTasks"));
            return;
        }

        foreach (var taskId in taskIds)
        {
            var result = await _transfers.RetryAsync(new RetryTransferTaskRequest(taskId)).ConfigureAwait(true);
            if (result.IsFailure)
            {
                SetStatus(new("Transfer", "RetryTaskFailed"));
                _messages.Error(StatusMessage);
                break;
            }
        }

        _statusBar.RequestQueueRefresh();
        await RefreshAsync().ConfigureAwait(true);
    }

    private Task ShowTaskDetailsAsync(object? parameter)
    {
        if (parameter is not TransferTaskRowViewModel row)
        {
            return Task.CompletedTask;
        }

        return _dialogs.ShowErrorDetailsAsync(new ErrorDialogRequest(
            Localization.GetString("Transfer", "TaskDetails"),
            row.Reason ?? row.Progress,
            Details: new Dictionary<string, string>
            {
                [Localization.GetString("Common", "Task")] = row.FileName,
                [Localization.GetString("Common", "Direction")] = row.Direction,
                [Localization.GetString("Common", "TargetSource")] = row.TargetOrSource,
                [Localization.GetString("Common", "Status")] = row.Progress,
                [Localization.GetString("Common", "Reason")] = row.Reason ?? "-",
                [Localization.GetString("Common", "Retryable")] = Localization.GetString("Common", row.CanExecuteAction ? "Yes" : "No")
            }));
    }

    private void SetStatus(LocalizedText value)
    {
        _statusMessage = value;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private async Task EnsureProviderIdsAsync(IReadOnlyList<TransferStateSnapshot> snapshots)
    {
        if (snapshots.All(snapshot => _providerIdsByAccount.ContainsKey(snapshot.Task.StorageAccountId)))
        {
            return;
        }

        var accountsResult = await _accounts.ListAsync(new ListStorageAccountsRequest()).ConfigureAwait(true);
        if (accountsResult.IsFailure)
        {
            return;
        }

        foreach (var account in accountsResult.GetValueOrThrow())
        {
            _providerIdsByAccount[account.Id] = account.ProviderId;
        }
    }

    private TransferTaskRowViewModel CreateTaskRow(TransferStateSnapshot snapshot)
    {
        _providerIdsByAccount.TryGetValue(snapshot.Task.StorageAccountId, out var providerId);
        return TransferTaskRowViewModel.From(
            snapshot,
            providerId,
            ExecuteTaskActionCommand,
            ShowTaskDetailsCommand,
            Localization);
    }

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusMessage));
        Tasks.Clear();
        foreach (var task in _currentSnapshots)
        {
            Tasks.Add(CreateTaskRow(task));
        }
        RaiseTaskCollectionStateChanged();
    }

    public override void Dispose()
    {
        _refreshTimer.Stop();
        base.Dispose();
    }
}

public sealed record TransferTaskRowViewModel(
    AtomBox.Core.ValueObjects.TransferTaskId TaskId,
    TransferStatus Status,
    string FileName,
    string Direction,
    string TargetOrSource,
    string Progress,
    double ProgressValue,
    bool IsProgressBarVisible,
    string Speed,
    string? Reason,
    bool HasDetails,
    bool UsesBestEffortCancellation,
    string Action,
    bool CanExecuteAction,
    ICommand ActionCommand,
    ICommand DetailsCommand)
{
    public bool IsProgressTextVisible => !IsProgressBarVisible;

    public static TransferTaskRowViewModel From(
        TransferStateSnapshot snapshot,
        StorageProviderId providerId,
        ICommand actionCommand,
        ICommand detailsCommand,
        ILocalizationService localization)
    {
        var task = snapshot.Task;
        var targetOrSource = task.Direction == TransferDirection.Upload
            ? task.RemotePath.ToString()
            : task.LocalPath.ToString();
        var usesBestEffortCancellation = UsesBestEffortCancellationFor(providerId, task.Direction);

        return new TransferTaskRowViewModel(
            task.Id,
            task.Status,
            GetFileName(task, localization),
            FormatDirection(task.Direction, localization),
            string.IsNullOrWhiteSpace(targetOrSource) ? "/" : targetOrSource,
            FormatProgress(task.Status, snapshot.Progress, usesBestEffortCancellation, localization),
            snapshot.Progress?.Percent ?? 0,
            task.Status == TransferStatus.Running && snapshot.Progress?.Percent is not null,
            FormatSpeed(task.Status, snapshot.Progress),
            task.StatusReason,
            !string.IsNullOrWhiteSpace(task.StatusReason) || task.Status is TransferStatus.Failed or TransferStatus.Interrupted,
            usesBestEffortCancellation,
            FormatAction(task.Status, usesBestEffortCancellation, localization),
            CanExecuteActionFor(task.Status, snapshot.CanRetry),
            actionCommand,
            detailsCommand);
    }

    private static string GetFileName(TransferTask task, ILocalizationService localization)
    {
        var name = task.Direction == TransferDirection.Upload
            ? task.LocalPath.GetFileName()
            : task.RemotePath.Name;

        return string.IsNullOrWhiteSpace(name) ? localization.GetString("Transfer", "Unnamed") : name;
    }

    private static string FormatDirection(TransferDirection direction, ILocalizationService localization)
    {
        return localization.GetString("Transfer", direction == TransferDirection.Upload ? "Upload" : "Download");
    }

    private static string FormatProgress(
        TransferStatus status,
        TransferProgress? progress,
        bool usesBestEffortCancellation,
        ILocalizationService localization)
    {
        return status switch
        {
            TransferStatus.Pending => localization.GetString("Transfer", "Pending"),
            TransferStatus.Running when progress?.Percent is { } percent => $"{percent:0}%",
            TransferStatus.Running => localization.GetString("Transfer", "Running"),
            TransferStatus.Canceling => localization.GetString(
                "Transfer",
                usesBestEffortCancellation ? "AttemptingCancellation" : "Canceling"),
            TransferStatus.Paused when progress?.Percent is { } percent => localization.Format("Transfer", "PausedPercent", percent),
            TransferStatus.Paused => localization.GetString("Transfer", "Paused"),
            TransferStatus.Interrupted => localization.GetString("Transfer", "Interrupted"),
            TransferStatus.Succeeded => "100%",
            TransferStatus.Failed => localization.GetString("Transfer", "Failed"),
            TransferStatus.Canceled => localization.GetString("Transfer", "Canceled"),
            _ => status.ToString()
        };
    }

    private static string FormatSpeed(TransferStatus status, TransferProgress? progress)
    {
        if (status != TransferStatus.Running || progress?.SpeedBytesPerSecond is not { } speed)
        {
            return "-";
        }

        return FormatBytesPerSecond(speed);
    }

    private static string FormatBytesPerSecond(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
        {
            return $"{bytesPerSecond / 1024 / 1024:0.##} MB/s";
        }

        if (bytesPerSecond >= 1024)
        {
            return $"{bytesPerSecond / 1024:0.##} KB/s";
        }

        return $"{bytesPerSecond:0} B/s";
    }

    private static string FormatAction(
        TransferStatus status,
        bool usesBestEffortCancellation,
        ILocalizationService localization)
    {
        return status switch
        {
            TransferStatus.Pending => localization.GetString("Common", "Cancel"),
            TransferStatus.Running => usesBestEffortCancellation
                ? localization.GetString("Transfer", "TryCancel")
                : localization.GetString("Common", "Cancel"),
            TransferStatus.Canceling => "-",
            TransferStatus.Paused => "-",
            TransferStatus.Interrupted => localization.GetString("Common", "Retry"),
            TransferStatus.Succeeded => "-",
            TransferStatus.Failed => localization.GetString("Common", "Retry"),
            TransferStatus.Canceled => "-",
            _ => "-"
        };
    }

    private static bool UsesBestEffortCancellationFor(
        StorageProviderId providerId,
        TransferDirection direction)
    {
        if (providerId == StorageProviderRegistry.S3CompatibleProviderId ||
            providerId == StorageProviderRegistry.TencentCosProviderId ||
            providerId == StorageProviderRegistry.FtpProviderId ||
            providerId == StorageProviderRegistry.SftpProviderId ||
            providerId == StorageProviderRegistry.WebDavProviderId)
        {
            return false;
        }

        return providerId != StorageProviderRegistry.QiniuKodoProviderId ||
               direction != TransferDirection.Download;
    }

    private static bool CanExecuteActionFor(TransferStatus status, bool canRetry)
    {
        return status is TransferStatus.Pending or TransferStatus.Running ||
               (status is TransferStatus.Failed or TransferStatus.Interrupted && canRetry);
    }
}
