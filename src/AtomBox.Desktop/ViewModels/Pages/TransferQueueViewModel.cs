using System.Collections.ObjectModel;
using System.Windows.Input;
using AtomBox.Application.Transfers;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.Shell;
using Avalonia.Threading;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class TransferQueueViewModel : ViewModelBase
{
    private readonly TransferAppService _transfers;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly StatusBarViewModel _statusBar;
    private readonly DispatcherTimer _refreshTimer;
    private static readonly Lock CompletionNotificationLock = new();
    private static readonly HashSet<TransferTaskId> CompletionNotificationsShown = [];
    private readonly Dictionary<TransferTaskId, TransferTaskRowViewModel> _lastObservedTasks = [];
    private string _statusMessage = "正在加载传输队列...";
    private bool _isLoading;
    private bool _isRefreshing;
    private TransferTaskRowViewModel? _selectedTask;

    public TransferQueueViewModel(
        TransferAppService transfers,
        IMessageService messages,
        IDialogService dialogs,
        StatusBarViewModel statusBar)
    {
        _transfers = transfers;
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

    public string Title => "传输队列";

    public ObservableCollection<TransferTaskRowViewModel> Tasks { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ExecuteTaskActionCommand { get; }

    public AsyncRelayCommand CancelPendingTasksCommand { get; }

    public AsyncRelayCommand RetryFailedTasksCommand { get; }

    public AsyncRelayCommand ShowTaskDetailsCommand { get; }

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
            StatusMessage = "正在加载传输队列...";
        }

        try
        {
            var result = await _transfers.GetQueueAsync(new GetTransferQueueRequest()).ConfigureAwait(true);
            Tasks.Clear();

            if (result.IsFailure)
            {
                StatusMessage = result.Error?.Message ?? "传输队列加载失败。";
                IsLoading = false;
                RaiseTaskCollectionStateChanged();
                return;
            }

            var snapshots = result.GetValueOrThrow().Tasks;
            await NotifyCompletedTasksAsync(snapshots).ConfigureAwait(true);

            foreach (var task in snapshots)
            {
                Tasks.Add(TransferTaskRowViewModel.From(task, ExecuteTaskActionCommand, ShowTaskDetailsCommand));
            }

            StatusMessage = Tasks.Count == 0
                ? "暂无传输任务。从远程浏览页上传或下载文件后，任务会显示在这里。"
                : $"当前队列任务 {Tasks.Count} 个。";
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
            .Where(task => task.Status is TransferStatus.Pending or TransferStatus.Running &&
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

            var completed = TransferTaskRowViewModel.From(snapshot, ExecuteTaskActionCommand, ShowTaskDetailsCommand);
            completedTasks.Add(completed);
        }

        foreach (var group in completedTasks.GroupBy(task => task.Direction))
        {
            var tasks = group.ToArray();
            var direction = group.Key;
            var message = tasks.Length == 1
                ? $"{tasks[0].FileName}{direction}完毕"
                : $"{tasks.Length}个文件{direction}完毕";
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

        var result = row.Status switch
        {
            TransferStatus.Pending => await _transfers.CancelAsync(new CancelTransferTaskRequest(row.TaskId)).ConfigureAwait(true),
            TransferStatus.Running => await _transfers.CancelAsync(new CancelTransferTaskRequest(row.TaskId)).ConfigureAwait(true),
            TransferStatus.Failed or TransferStatus.Interrupted => await _transfers.RetryAsync(new RetryTransferTaskRequest(row.TaskId)).ConfigureAwait(true),
            _ => null
        };

        if (result is null)
        {
            _messages.Info("当前任务状态暂不支持该操作。");
            return;
        }

        if (result.IsFailure)
        {
            StatusMessage = result.Error?.Message ?? "任务操作失败。";
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
            _messages.Info("当前没有等待中的任务。");
            return;
        }

        foreach (var taskId in taskIds)
        {
            var result = await _transfers.CancelAsync(new CancelTransferTaskRequest(taskId)).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error?.Message ?? "取消任务失败。";
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
            _messages.Info("当前没有可重试的任务。");
            return;
        }

        foreach (var taskId in taskIds)
        {
            var result = await _transfers.RetryAsync(new RetryTransferTaskRequest(taskId)).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error?.Message ?? "重试任务失败。";
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
            "传输任务详情",
            row.Reason ?? row.Progress,
            Details: new Dictionary<string, string>
            {
                ["任务"] = row.FileName,
                ["方向"] = row.Direction,
                ["目标 / 来源"] = row.TargetOrSource,
                ["状态"] = row.Progress,
                ["原因"] = row.Reason ?? "-",
                ["可重试"] = row.CanExecuteAction ? "是" : "否"
            }));
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
        ICommand detailsCommand)
    {
        var task = snapshot.Task;
        var targetOrSource = task.Direction == TransferDirection.Upload
            ? task.RemotePath.ToString()
            : task.LocalPath.ToString();

        return new TransferTaskRowViewModel(
            task.Id,
            task.Status,
            GetFileName(task),
            FormatDirection(task.Direction),
            string.IsNullOrWhiteSpace(targetOrSource) ? "/" : targetOrSource,
            FormatProgress(task.Status, snapshot.Progress),
            snapshot.Progress?.Percent ?? 0,
            task.Status == TransferStatus.Running && snapshot.Progress?.Percent is not null,
            FormatSpeed(task.Status, snapshot.Progress),
            task.StatusReason,
            !string.IsNullOrWhiteSpace(task.StatusReason) || task.Status is TransferStatus.Failed or TransferStatus.Interrupted,
            FormatAction(task.Status),
            CanExecuteActionFor(task.Status, snapshot.CanRetry),
            actionCommand,
            detailsCommand);
    }

    private static string GetFileName(TransferTask task)
    {
        var name = task.Direction == TransferDirection.Upload
            ? task.LocalPath.GetFileName()
            : task.RemotePath.Name;

        return string.IsNullOrWhiteSpace(name) ? "(未命名)" : name;
    }

    private static string FormatDirection(TransferDirection direction)
    {
        return direction == TransferDirection.Upload ? "上传" : "下载";
    }

    private static string FormatProgress(TransferStatus status, TransferProgress? progress)
    {
        return status switch
        {
            TransferStatus.Pending => "等待中",
            TransferStatus.Running when progress?.Percent is { } percent => $"{percent:0}%",
            TransferStatus.Running => "运行中",
            TransferStatus.Paused when progress?.Percent is { } percent => $"暂停 {percent:0}%",
            TransferStatus.Paused => "暂停",
            TransferStatus.Interrupted => "已中断",
            TransferStatus.Succeeded => "100%",
            TransferStatus.Failed => "失败",
            TransferStatus.Canceled => "取消",
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

    private static string FormatAction(TransferStatus status)
    {
        return status switch
        {
            TransferStatus.Pending => "取消",
            TransferStatus.Running => "取消",
            TransferStatus.Paused => "-",
            TransferStatus.Interrupted => "重试",
            TransferStatus.Succeeded => "-",
            TransferStatus.Failed => "重试",
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
