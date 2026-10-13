using System.Collections.ObjectModel;
using System.Windows.Input;
using AtomBox.Application.Transfers;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.Shell;
using AtomUI.City.Mvvm;
using CommunityToolkit.Mvvm.Input;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class TransferQueueViewModel : ViewModelBase
{
    private readonly TransferAppService _transfers;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly StatusBarViewModel _statusBar;
    private readonly IAtomBoxLocalizedText _localization;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CityUiDispatcher _dispatcher;
    private static readonly Lock CompletionNotificationLock = new();
    private static readonly HashSet<TransferTaskId> CompletionNotificationsShown = [];
    private readonly Dictionary<TransferTaskId, TransferTaskRowViewModel> _lastObservedTasks = [];
    private LocalizedText _statusMessage = new("Transfer", "LoadingQueue");
    private IReadOnlyList<TransferStateSnapshot> _currentSnapshots = [];
    private bool _isLoading;
    private Task? _refreshLoop;
    private TransferTaskRowViewModel? _selectedTask;

    public TransferQueueViewModel(
        TransferAppService transfers,
        IMessageService messages,
        IDialogService dialogs,
        StatusBarViewModel statusBar,
        IAtomBoxLocalizedText localization,
        DesktopCommandFactory commands,
        CityUiDispatcher dispatcher)
    {
        _transfers = transfers;
        _messages = messages;
        _dialogs = dialogs;
        _statusBar = statusBar;
        _localization = localization;
        _dispatcher = dispatcher;
        RefreshCommand = commands.CreateAsync(this, nameof(RefreshCommand), RefreshAsync);
        ExecuteTaskActionCommand = commands.CreateAsync<TransferTaskRowViewModel>(
            this,
            nameof(ExecuteTaskActionCommand),
            ExecuteTaskActionAsync,
            row => row is { CanExecuteAction: true },
            CommandConcurrencyPolicy.RejectByKey,
            row => row?.TaskId);
        CancelPendingTasksCommand = commands.CreateAsync(
            this,
            nameof(CancelPendingTasksCommand),
            CancelPendingTasksAsync,
            () => HasCancelableTasks);
        RetryFailedTasksCommand = commands.CreateAsync(
            this,
            nameof(RetryFailedTasksCommand),
            RetryFailedTasksAsync,
            () => HasRetryableTasks);
        ShowTaskDetailsCommand = commands.CreateAsync<TransferTaskRowViewModel>(
            this,
            nameof(ShowTaskDetailsCommand),
            ShowTaskDetailsAsync,
            row => row is { HasDetails: true });
    }

    public string Title => _localization.GetString("Transfer", "QueueTitle");

    public ObservableCollection<TransferTaskRowViewModel> Tasks { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<TransferTaskRowViewModel> ExecuteTaskActionCommand { get; }

    public IAsyncRelayCommand CancelPendingTasksCommand { get; }

    public IAsyncRelayCommand RetryFailedTasksCommand { get; }

    public IAsyncRelayCommand<TransferTaskRowViewModel> ShowTaskDetailsCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage.Resolve(_localization);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasTasks => Tasks.Count > 0;

    public bool IsEmptyStateVisible => !IsLoading && !HasTasks;

    public bool HasCancelableTasks => Tasks.Any(task => task.Status == TransferStatus.Pending);

    public bool HasRetryableTasks => Tasks.Any(task => task.Status is TransferStatus.Failed or TransferStatus.Interrupted);

    public TransferTaskRowViewModel? SelectedTask
    {
        get => _selectedTask;
        set => SetProperty(ref _selectedTask, value);
    }

    public bool IsTaskListVisible => !IsLoading && HasTasks;

    protected override ValueTask OnActivatedAsync(
        ActivationContext context,
        CancellationToken cancellationToken)
    {
        _localization.LanguageChanged += HandleLanguageChanged;
        context.Scope.Add(EventSubscription.Create(
            () => _localization.LanguageChanged -= HandleLanguageChanged));
        context.Scope.Add(EventSubscription.Create(
            () => _refreshLoop = null));

        return _dispatcher.PostAsync(
            async token =>
            {
                await RefreshAsync(showLoading: true, token).ConfigureAwait(true);
                _refreshLoop = context.Scope.StartUniqueAsync(
                    "transfer-queue-periodic-refresh",
                    RunRefreshLoopAsync,
                    token);
            },
            cancellationToken);
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        _refreshLoop = null;
        return ValueTask.CompletedTask;
    }

    protected override void OnDisposed()
    {
        _refreshGate.Dispose();
    }

    private Task RefreshAsync(CancellationToken cancellationToken)
    {
        return RefreshAsync(showLoading: true, cancellationToken);
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
        {
            await RefreshAsync(showLoading: false, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task RefreshAsync(bool showLoading, CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(true);

        try
        {
            if (showLoading)
            {
                IsLoading = true;
                SetStatus(new("Transfer", "LoadingQueue"));
            }

            var result = await _transfers.GetQueueAsync(
                new GetTransferQueueRequest(),
                cancellationToken).ConfigureAwait(true);
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
            await NotifyCompletedTasksAsync(snapshots, cancellationToken).ConfigureAwait(true);

            foreach (var task in snapshots)
            {
                Tasks.Add(TransferTaskRowViewModel.From(task, ExecuteTaskActionCommand, ShowTaskDetailsCommand, _localization));
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
            _refreshGate.Release();
        }
    }

    private async Task NotifyCompletedTasksAsync(
        IReadOnlyList<TransferStateSnapshot> currentSnapshots,
        CancellationToken cancellationToken)
    {
        if (_lastObservedTasks.Count == 0)
        {
            return;
        }

        var currentTaskIds = currentSnapshots
            .Select(snapshot => snapshot.Task.Id)
            .ToHashSet();
        var completedCandidates = _lastObservedTasks.Values
            .Where(task => task.Status is TransferStatus.Pending or TransferStatus.Running &&
                           !currentTaskIds.Contains(task.TaskId) &&
                           !IsCompletionNotificationShown(task.TaskId))
            .ToArray();
        if (completedCandidates.Length == 0)
        {
            return;
        }

        var historyResult = await _transfers.GetHistoryAsync(
            new GetTransferHistoryRequest(),
            cancellationToken).ConfigureAwait(true);
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

            var completed = TransferTaskRowViewModel.From(snapshot, ExecuteTaskActionCommand, ShowTaskDetailsCommand, _localization);
            completedTasks.Add(completed);
        }

        foreach (var group in completedTasks.GroupBy(task => task.Direction))
        {
            var tasks = group.ToArray();
            var direction = group.Key;
            var message = tasks.Length == 1
                ? _localization.Format("Transfer", "TaskCompletedSingle", tasks[0].FileName, direction)
                : _localization.Format("Transfer", "TaskCompletedMultiple", tasks.Length, direction);
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
        CancelPendingTasksCommand.NotifyCanExecuteChanged();
        RetryFailedTasksCommand.NotifyCanExecuteChanged();
    }

    private async Task ExecuteTaskActionAsync(
        TransferTaskRowViewModel? parameter,
        CancellationToken cancellationToken)
    {
        if (parameter is not TransferTaskRowViewModel row)
        {
            return;
        }

        var result = row.Status switch
        {
            TransferStatus.Pending => await _transfers.CancelAsync(new CancelTransferTaskRequest(row.TaskId), cancellationToken).ConfigureAwait(true),
            TransferStatus.Running => await _transfers.CancelAsync(new CancelTransferTaskRequest(row.TaskId), cancellationToken).ConfigureAwait(true),
            TransferStatus.Failed or TransferStatus.Interrupted => await _transfers.RetryAsync(new RetryTransferTaskRequest(row.TaskId), cancellationToken).ConfigureAwait(true),
            _ => null
        };

        if (result is null)
        {
            _messages.Info(_localization.GetString("Transfer", "UnsupportedTaskAction"));
            return;
        }

        if (result.IsFailure)
        {
            SetStatus(new("Transfer", "TaskActionFailed"));
            return;
        }

        _statusBar.RequestQueueRefresh();
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task CancelPendingTasksAsync(CancellationToken cancellationToken)
    {
        var taskIds = Tasks
            .Where(task => task.Status == TransferStatus.Pending)
            .Select(task => task.TaskId)
            .ToArray();
        if (taskIds.Length == 0)
        {
            _messages.Info(_localization.GetString("Transfer", "NoPendingTasks"));
            return;
        }

        foreach (var taskId in taskIds)
        {
            var result = await _transfers.CancelAsync(
                new CancelTransferTaskRequest(taskId),
                cancellationToken).ConfigureAwait(true);
            if (result.IsFailure)
            {
                SetStatus(new("Transfer", "CancelTaskFailed"));
                _messages.Error(StatusMessage);
                break;
            }
        }

        _statusBar.RequestQueueRefresh();
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task RetryFailedTasksAsync(CancellationToken cancellationToken)
    {
        var taskIds = Tasks
            .Where(task => task.Status is TransferStatus.Failed or TransferStatus.Interrupted)
            .Select(task => task.TaskId)
            .ToArray();
        if (taskIds.Length == 0)
        {
            _messages.Info(_localization.GetString("Transfer", "NoRetryableTasks"));
            return;
        }

        foreach (var taskId in taskIds)
        {
            var result = await _transfers.RetryAsync(
                new RetryTransferTaskRequest(taskId),
                cancellationToken).ConfigureAwait(true);
            if (result.IsFailure)
            {
                SetStatus(new("Transfer", "RetryTaskFailed"));
                _messages.Error(StatusMessage);
                break;
            }
        }

        _statusBar.RequestQueueRefresh();
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private Task ShowTaskDetailsAsync(
        TransferTaskRowViewModel? parameter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parameter is not TransferTaskRowViewModel row)
        {
            return Task.CompletedTask;
        }

        return _dialogs.ShowErrorDetailsAsync(new ErrorDialogRequest(
            _localization.GetString("Transfer", "TaskDetails"),
            row.Reason ?? row.Progress,
            Details: new Dictionary<string, string>
            {
                [_localization.GetString("Common", "Task")] = row.FileName,
                [_localization.GetString("Common", "Direction")] = row.Direction,
                [_localization.GetString("Common", "TargetSource")] = row.TargetOrSource,
                [_localization.GetString("Common", "Status")] = row.Progress,
                [_localization.GetString("Common", "Reason")] = row.Reason ?? "-",
                [_localization.GetString("Common", "Retryable")] = _localization.GetString("Common", row.CanExecuteAction ? "Yes" : "No")
            }));
    }

    private void SetStatus(LocalizedText value)
    {
        _statusMessage = value;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void HandleLanguageChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusMessage));
        Tasks.Clear();
        foreach (var task in _currentSnapshots)
        {
            Tasks.Add(TransferTaskRowViewModel.From(task, ExecuteTaskActionCommand, ShowTaskDetailsCommand, _localization));
        }
        RaiseTaskCollectionStateChanged();
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
    string Action,
    bool CanExecuteAction,
    ICommand ActionCommand,
    ICommand DetailsCommand)
{
    public bool IsProgressTextVisible => !IsProgressBarVisible;

    public static TransferTaskRowViewModel From(
        TransferStateSnapshot snapshot,
        ICommand actionCommand,
        ICommand detailsCommand,
        IAtomBoxLocalizedText localization)
    {
        var task = snapshot.Task;
        var targetOrSource = task.Direction == TransferDirection.Upload
            ? task.RemotePath.ToString()
            : task.LocalPath.ToString();

        return new TransferTaskRowViewModel(
            task.Id,
            task.Status,
            GetFileName(task, localization),
            FormatDirection(task.Direction, localization),
            string.IsNullOrWhiteSpace(targetOrSource) ? "/" : targetOrSource,
            FormatProgress(task.Status, snapshot.Progress, localization),
            snapshot.Progress?.Percent ?? 0,
            task.Status == TransferStatus.Running && snapshot.Progress?.Percent is not null,
            FormatSpeed(task.Status, snapshot.Progress),
            task.StatusReason,
            !string.IsNullOrWhiteSpace(task.StatusReason) || task.Status is TransferStatus.Failed or TransferStatus.Interrupted,
            FormatAction(task.Status, localization),
            CanExecuteActionFor(task.Status, snapshot.CanRetry),
            actionCommand,
            detailsCommand);
    }

    private static string GetFileName(TransferTask task, IAtomBoxLocalizedText localization)
    {
        var name = task.Direction == TransferDirection.Upload
            ? task.LocalPath.GetFileName()
            : task.RemotePath.Name;

        return string.IsNullOrWhiteSpace(name) ? localization.GetString("Transfer", "Unnamed") : name;
    }

    private static string FormatDirection(TransferDirection direction, IAtomBoxLocalizedText localization)
    {
        return localization.GetString("Transfer", direction == TransferDirection.Upload ? "Upload" : "Download");
    }

    private static string FormatProgress(TransferStatus status, TransferProgress? progress, IAtomBoxLocalizedText localization)
    {
        return status switch
        {
            TransferStatus.Pending => localization.GetString("Transfer", "Pending"),
            TransferStatus.Running when progress?.Percent is { } percent => $"{percent:0}%",
            TransferStatus.Running => localization.GetString("Transfer", "Running"),
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

    private static string FormatAction(TransferStatus status, IAtomBoxLocalizedText localization)
    {
        return status switch
        {
            TransferStatus.Pending => localization.GetString("Common", "Cancel"),
            TransferStatus.Running => localization.GetString("Common", "Cancel"),
            TransferStatus.Paused => "-",
            TransferStatus.Interrupted => localization.GetString("Common", "Retry"),
            TransferStatus.Succeeded => "-",
            TransferStatus.Failed => localization.GetString("Common", "Retry"),
            TransferStatus.Canceled => "-",
            _ => "-"
        };
    }

    private static bool CanExecuteActionFor(TransferStatus status, bool canRetry)
    {
        return status is TransferStatus.Pending or TransferStatus.Running ||
               (status is TransferStatus.Failed or TransferStatus.Interrupted && canRetry);
    }
}
