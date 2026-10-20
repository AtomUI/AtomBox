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
using CommunityToolkit.Mvvm.ComponentModel;
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
    private bool _taskCollectionStateInitialized;
    private bool _lastHasTasks;
    private bool _lastIsEmptyStateVisible;
    private bool _lastIsTaskListVisible;
    private bool _lastHasCancelableTasks;
    private bool _lastHasRetryableTasks;

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
        if (state.Status != TransferProjectionStatus.Ready && !state.IsStale)
        {
            Tasks.Clear();
            _currentSnapshots = [];
            IsLoading = false;
            SetStatus(state.Status == TransferProjectionStatus.Faulted
                ? new("Transfer", "QueueLoadFailed")
                : new("Transfer", "LoadingQueue"));
            RaiseTaskCollectionStateChanged();
            return;
        }

        _currentSnapshots = state.Tasks;
        ReconcileTasks(state.Tasks);

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

    private void ReconcileTasks(IReadOnlyList<TransferStateSnapshot> snapshots)
    {
        var commandStateChanged = false;
        var desiredTaskIds = snapshots
            .Select(snapshot => snapshot.Task.Id)
            .ToHashSet();
        for (var index = Tasks.Count - 1; index >= 0; index--)
        {
            if (!desiredTaskIds.Contains(Tasks[index].TaskId))
            {
                Tasks.RemoveAt(index);
                commandStateChanged = true;
            }
        }

        for (var targetIndex = 0; targetIndex < snapshots.Count; targetIndex++)
        {
            var snapshot = snapshots[targetIndex];
            var existingIndex = FindTaskIndex(snapshot.Task.Id);
            if (existingIndex < 0)
            {
                Tasks.Insert(targetIndex, TransferTaskRowViewModel.From(
                    snapshot,
                    ExecuteTaskActionCommand,
                    AbandonTaskCommand,
                    ShowTaskDetailsCommand,
                    _localization));
                commandStateChanged = true;
                continue;
            }

            var row = Tasks[existingIndex];
            var previousCanExecuteAction = row.CanExecuteAction;
            var previousCanAbandon = row.CanAbandon;
            var previousHasDetails = row.HasDetails;
            row.UpdateFrom(snapshot, _localization);
            commandStateChanged |= previousCanExecuteAction != row.CanExecuteAction ||
                previousCanAbandon != row.CanAbandon ||
                previousHasDetails != row.HasDetails;
            if (existingIndex != targetIndex)
            {
                Tasks.Move(existingIndex, targetIndex);
            }
        }

        if (commandStateChanged)
        {
            ExecuteTaskActionCommand.NotifyCanExecuteChanged();
            AbandonTaskCommand.NotifyCanExecuteChanged();
            ShowTaskDetailsCommand.NotifyCanExecuteChanged();
        }
    }

    private int FindTaskIndex(TransferTaskId taskId)
    {
        for (var index = 0; index < Tasks.Count; index++)
        {
            if (Tasks[index].TaskId == taskId)
            {
                return index;
            }
        }

        return -1;
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
        var hasTasks = HasTasks;
        var isEmptyStateVisible = IsEmptyStateVisible;
        var isTaskListVisible = IsTaskListVisible;
        var hasCancelableTasks = HasCancelableTasks;
        var hasRetryableTasks = HasRetryableTasks;
        if (!_taskCollectionStateInitialized || hasTasks != _lastHasTasks)
        {
            OnPropertyChanged(nameof(HasTasks));
        }

        if (!_taskCollectionStateInitialized || isEmptyStateVisible != _lastIsEmptyStateVisible)
        {
            OnPropertyChanged(nameof(IsEmptyStateVisible));
        }

        if (!_taskCollectionStateInitialized || isTaskListVisible != _lastIsTaskListVisible)
        {
            OnPropertyChanged(nameof(IsTaskListVisible));
        }

        if (!_taskCollectionStateInitialized || hasCancelableTasks != _lastHasCancelableTasks)
        {
            OnPropertyChanged(nameof(HasCancelableTasks));
            CancelPendingTasksCommand.NotifyCanExecuteChanged();
        }

        if (!_taskCollectionStateInitialized || hasRetryableTasks != _lastHasRetryableTasks)
        {
            OnPropertyChanged(nameof(HasRetryableTasks));
            RetryFailedTasksCommand.NotifyCanExecuteChanged();
        }

        _lastHasTasks = hasTasks;
        _lastIsEmptyStateVisible = isEmptyStateVisible;
        _lastIsTaskListVisible = isTaskListVisible;
        _lastHasCancelableTasks = hasCancelableTasks;
        _lastHasRetryableTasks = hasRetryableTasks;
        _taskCollectionStateInitialized = true;
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
        var previous = _statusMessage.Resolve(_localization);
        _statusMessage = value;
        if (!string.Equals(previous, value.Resolve(_localization), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    private void HandleLanguageChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusMessage));
        ReconcileTasks(_currentSnapshots);
        RaiseTaskCollectionStateChanged();
    }

}

public sealed class TransferTaskRowViewModel : ObservableObject
{
    private TransferStatus _status;
    private string _fileName = string.Empty;
    private string _direction = string.Empty;
    private string _targetOrSource = string.Empty;
    private string _progress = string.Empty;
    private double _progressValue;
    private bool _isProgressBarVisible;
    private string _speed = string.Empty;
    private string? _reason;
    private bool _hasDetails;
    private string _action = string.Empty;
    private bool _canExecuteAction;
    private bool _canAbandon;

    private TransferTaskRowViewModel(
        TransferTaskId taskId,
        ICommand actionCommand,
        ICommand abandonCommand,
        ICommand detailsCommand)
    {
        TaskId = taskId;
        ActionCommand = actionCommand;
        AbandonCommand = abandonCommand;
        DetailsCommand = detailsCommand;
    }

    public TransferTaskId TaskId { get; }

    public TransferStatus Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string FileName
    {
        get => _fileName;
        private set => SetProperty(ref _fileName, value);
    }

    public string Direction
    {
        get => _direction;
        private set => SetProperty(ref _direction, value);
    }

    public string TargetOrSource
    {
        get => _targetOrSource;
        private set => SetProperty(ref _targetOrSource, value);
    }

    public string Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public bool IsProgressBarVisible
    {
        get => _isProgressBarVisible;
        private set
        {
            if (SetProperty(ref _isProgressBarVisible, value))
            {
                OnPropertyChanged(nameof(IsProgressTextVisible));
            }
        }
    }

    public bool IsProgressTextVisible => !IsProgressBarVisible;

    public string Speed
    {
        get => _speed;
        private set => SetProperty(ref _speed, value);
    }

    public string? Reason
    {
        get => _reason;
        private set => SetProperty(ref _reason, value);
    }

    public bool HasDetails
    {
        get => _hasDetails;
        private set => SetProperty(ref _hasDetails, value);
    }

    public string Action
    {
        get => _action;
        private set => SetProperty(ref _action, value);
    }

    public bool CanExecuteAction
    {
        get => _canExecuteAction;
        private set => SetProperty(ref _canExecuteAction, value);
    }

    public ICommand ActionCommand { get; }

    public bool CanAbandon
    {
        get => _canAbandon;
        private set => SetProperty(ref _canAbandon, value);
    }

    public ICommand AbandonCommand { get; }

    public ICommand DetailsCommand { get; }

    public static TransferTaskRowViewModel From(
        TransferStateSnapshot snapshot,
        ICommand actionCommand,
        ICommand abandonCommand,
        ICommand detailsCommand,
        IAtomBoxLocalizedText localization)
    {
        var row = new TransferTaskRowViewModel(
            snapshot.Task.Id,
            actionCommand,
            abandonCommand,
            detailsCommand);
        row.UpdateFrom(snapshot, localization);
        return row;
    }

    internal void UpdateFrom(
        TransferStateSnapshot snapshot,
        IAtomBoxLocalizedText localization)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(localization);
        if (snapshot.Task.Id != TaskId)
        {
            throw new ArgumentException("A transfer row cannot change its task identity.", nameof(snapshot));
        }

        var task = snapshot.Task;
        var targetOrSource = task.Direction == TransferDirection.Upload
            ? task.RemotePath.ToString()
            : task.LocalPath.ToString();
        Status = task.Status;
        FileName = GetFileName(task, localization);
        Direction = FormatDirection(task.Direction, localization);
        TargetOrSource = string.IsNullOrWhiteSpace(targetOrSource) ? "/" : targetOrSource;
        Progress = FormatProgress(task.Status, snapshot.Progress, localization);
        ProgressValue = snapshot.Progress?.Percent ?? 0;
        IsProgressBarVisible = task.Status == TransferStatus.Running && snapshot.Progress?.Percent is not null;
        Speed = FormatSpeed(task.Status, snapshot.Progress);
        Reason = task.StatusReason;
        HasDetails = !string.IsNullOrWhiteSpace(task.StatusReason) ||
            task.Status is TransferStatus.Failed or TransferStatus.Interrupted;
        Action = FormatAction(task.Status, localization);
        CanExecuteAction = CanExecuteActionFor(task.Status, snapshot.CanRetry);
        CanAbandon = task.Status == TransferStatus.Interrupted;
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
