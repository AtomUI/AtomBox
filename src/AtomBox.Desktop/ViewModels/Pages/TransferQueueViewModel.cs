using System.Collections.ObjectModel;
using System.Windows.Input;
using AtomBox.Application.Transfers;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Services;
using AtomBox.Transfer.State;
using AtomUI.City.Mvvm;
using AtomUI.City.State;
using CommunityToolkit.Mvvm.Input;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class TransferQueueViewModel : ViewModelBase
{
    private readonly TransferAppService _transfers;
    private readonly IAtomBoxTransferState _transferState;
    private readonly ITransferStateSynchronizer _stateSynchronizer;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly IAtomBoxLocalizedText _localization;
    private readonly CityUiDispatcher _dispatcher;
    private static readonly Lock CompletionNotificationLock = new();
    private static readonly HashSet<TransferTaskId> CompletionNotificationsShown = [];
    private LocalizedText _statusMessage = new("Transfer", "LoadingQueue");
    private IReadOnlyList<TransferStateSnapshot> _currentSnapshots = [];
    private TransferQueueState _queueState = TransferQueueState.Uninitialized;
    private TransferRuntimeSnapshot _runtimeState = TransferRuntimeSnapshot.Initial;
    private bool _isLoading;
    private TransferTaskRowViewModel? _selectedTask;

    public TransferQueueViewModel(
        TransferAppService transfers,
        IAtomBoxTransferState transferState,
        ITransferStateSynchronizer stateSynchronizer,
        IMessageService messages,
        IDialogService dialogs,
        IAtomBoxLocalizedText localization,
        DesktopCommandFactory commands,
        CityUiDispatcher dispatcher)
    {
        _transfers = transfers;
        _transferState = transferState;
        _stateSynchronizer = stateSynchronizer;
        _messages = messages;
        _dialogs = dialogs;
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
        AbandonTaskCommand = commands.CreateAsync<TransferTaskRowViewModel>(
            this,
            nameof(AbandonTaskCommand),
            AbandonTaskAsync,
            row => row is { CanAbandon: true },
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

    public IAsyncRelayCommand<TransferTaskRowViewModel> AbandonTaskCommand { get; }

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
        context.Scope.Add(_transferState.Queue.Observe(
            args =>
            {
                ApplyQueue(args.NewValue);
                if (!args.IsInitial)
                {
                    _ = context.Scope.StartAsync(
                        $"transfer-completion-notification-{args.Version}",
                        token => NotifyCompletedTasksAsync(
                            args.OldValue.Tasks,
                            args.NewValue.Tasks,
                            token),
                        cancellationToken);
                }
            },
            StateSubscriptionOptions.Dispatcher(_dispatcher)));
        context.Scope.Add(_transferState.Runtime.Observe(
            args =>
            {
                _runtimeState = args.NewValue;
                ApplyQueue(_queueState);
            },
            StateSubscriptionOptions.Dispatcher(_dispatcher)));
        return ValueTask.CompletedTask;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        SetStatus(new("Transfer", "LoadingQueue"));
        RaiseTaskCollectionStateChanged();
        await _stateSynchronizer.RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private void ApplyQueue(TransferQueueState state)
    {
        _queueState = state;
        Tasks.Clear();
        if (state.Status != TransferProjectionStatus.Ready && !state.IsStale)
        {
            _currentSnapshots = [];
            IsLoading = false;
            SetStatus(state.Status == TransferProjectionStatus.Faulted
                ? new("Transfer", "QueueLoadFailed")
                : new("Transfer", "LoadingQueue"));
            RaiseTaskCollectionStateChanged();
            return;
        }

        _currentSnapshots = state.Tasks;
        foreach (var task in state.Tasks)
        {
            Tasks.Add(TransferTaskRowViewModel.From(
                task,
                ExecuteTaskActionCommand,
                AbandonTaskCommand,
                ShowTaskDetailsCommand,
                _localization));
        }

        SetStatus(state.Status == TransferProjectionStatus.Faulted
            ? new("Transfer", "QueueStale")
            : _runtimeState.Health == TransferRuntimeHealthState.Faulted
                ? new("Transfer", "RuntimeFaulted")
                : _runtimeState.Health == TransferRuntimeHealthState.Degraded
                    ? new("Transfer", "RuntimeDegraded")
                    : Tasks.Count == 0
                        ? new("Transfer", "NoQueueTasksLong")
                        : new("Transfer", "QueueCount", Tasks.Count));
        IsLoading = false;
        RaiseTaskCollectionStateChanged();
    }

    private async Task NotifyCompletedTasksAsync(
        IReadOnlyList<TransferStateSnapshot> previousSnapshots,
        IReadOnlyList<TransferStateSnapshot> currentSnapshots,
        CancellationToken cancellationToken)
    {
        if (previousSnapshots.Count == 0)
        {
            return;
        }

        var currentTaskIds = currentSnapshots
            .Select(snapshot => snapshot.Task.Id)
            .ToHashSet();
        var completedCandidates = previousSnapshots
            .Select(snapshot => snapshot.Task)
            .Where(task => task.Status is TransferStatus.Pending or TransferStatus.Running or TransferStatus.Canceling &&
                           !currentTaskIds.Contains(task.Id) &&
                           !IsCompletionNotificationShown(task.Id))
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
            if (!succeededTasks.TryGetValue(candidate.Id, out var snapshot))
            {
                continue;
            }

            if (!TryMarkCompletionNotificationShown(candidate.Id))
            {
                continue;
            }

            var completed = TransferTaskRowViewModel.From(
                snapshot,
                ExecuteTaskActionCommand,
                AbandonTaskCommand,
                ShowTaskDetailsCommand,
                _localization);
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

    }

    private async Task AbandonTaskAsync(
        TransferTaskRowViewModel? parameter,
        CancellationToken cancellationToken)
    {
        if (parameter is not { CanAbandon: true } row)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
            _localization.GetString("Transfer", "AbandonTaskTitle"),
            _localization.Format("Transfer", "AbandonTaskMessage", row.FileName),
            _localization.GetString("Common", "Abandon"),
            _localization.GetString("Common", "Cancel"))).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await _transfers.AbandonAsync(
            new AbandonTransferTaskRequest(row.TaskId),
            cancellationToken).ConfigureAwait(true);
        if (result.IsFailure)
        {
            SetStatus(new("Transfer", "AbandonTaskFailed"));
            _messages.Error(StatusMessage);
            return;
        }

        _messages.Success(_localization.GetString("Transfer", "TaskAbandoned"));
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
            Tasks.Add(TransferTaskRowViewModel.From(
                task,
                ExecuteTaskActionCommand,
                AbandonTaskCommand,
                ShowTaskDetailsCommand,
                _localization));
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
    bool CanAbandon,
    ICommand AbandonCommand,
    ICommand DetailsCommand)
{
    public bool IsProgressTextVisible => !IsProgressBarVisible;

    public static TransferTaskRowViewModel From(
        TransferStateSnapshot snapshot,
        ICommand actionCommand,
        ICommand abandonCommand,
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
            task.Status == TransferStatus.Interrupted,
            abandonCommand,
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
            TransferStatus.Canceling => localization.GetString("Transfer", "Canceling"),
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
            TransferStatus.Canceling => "-",
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
