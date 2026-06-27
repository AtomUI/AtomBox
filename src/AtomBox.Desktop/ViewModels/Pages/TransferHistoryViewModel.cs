using System.Collections.ObjectModel;
using AtomBox.Application.Transfers;
using AtomBox.Core.Errors;
using AtomBox.Desktop.Dialogs;
using AtomBox.Core.Transfers;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Services;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class TransferHistoryViewModel : ViewModelBase
{
    private readonly TransferAppService _transfers;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private ErrorDialogRequest? _lastErrorDetails;
    private int _pageIndex = 1;
    private string _statusMessage = "正在加载历史记录...";
    private bool _isLoading;
    private TransferHistoryRowViewModel? _selectedTask;

    public TransferHistoryViewModel(
        TransferAppService transfers,
        IMessageService messages,
        IDialogService dialogs)
    {
        _transfers = transfers;
        _messages = messages;
        _dialogs = dialogs;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        ClearHistoryCommand = new AsyncRelayCommand(_ => ClearHistoryAsync(), _ => HasTasks);
        ShowErrorDetailsCommand = new AsyncRelayCommand(_ => ShowErrorDetailsAsync(), _ => HasErrorDetails);
        ShowTaskDetailsCommand = new AsyncRelayCommand(
            ShowTaskDetailsAsync,
            parameter => parameter is TransferHistoryRowViewModel { HasDetails: true });
        PreviousPageCommand = new AsyncRelayCommand(_ => GoToPreviousPageAsync(), _ => PageIndex > 1);
        NextPageCommand = new AsyncRelayCommand(_ => GoToNextPageAsync());
        _ = RefreshAsync();
    }

    public string Title => "历史记录";

    public ObservableCollection<TransferHistoryRowViewModel> Tasks { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ClearHistoryCommand { get; }

    public AsyncRelayCommand PreviousPageCommand { get; }

    public AsyncRelayCommand NextPageCommand { get; }

    public AsyncRelayCommand ShowErrorDetailsCommand { get; }

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

    public bool HasErrorDetails => _lastErrorDetails is not null;

    public TransferHistoryRowViewModel? SelectedTask
    {
        get => _selectedTask;
        set => SetProperty(ref _selectedTask, value);
    }

    public bool IsTaskListVisible => !IsLoading && HasTasks;

    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (SetProperty(ref _pageIndex, value))
            {
                PreviousPageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void ApplyNavigationParameter(TransferHistoryNavigationParameter parameter)
    {
        PageIndex = Math.Max(parameter.PageIndex ?? 1, 1);
        _ = RefreshAsync();
    }

    private async Task GoToPreviousPageAsync()
    {
        if (PageIndex <= 1)
        {
            return;
        }

        PageIndex--;
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task GoToNextPageAsync()
    {
        PageIndex++;
        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = "正在加载历史记录...";
        ClearErrorDetails();

        var result = await _transfers.GetHistoryAsync(new GetTransferHistoryRequest(PageIndex)).ConfigureAwait(true);
        Tasks.Clear();

        if (result.IsFailure)
        {
            StatusMessage = result.Error?.Message ?? "历史记录加载失败。";
            SetErrorDetails("历史记录加载失败", "历史记录加载失败。", result.Error);
            IsLoading = false;
            RaiseTaskCollectionStateChanged();
            return;
        }

        var page = result.GetValueOrThrow();
        PageIndex = page.PageIndex;

        foreach (var task in page.Tasks)
        {
            Tasks.Add(TransferHistoryRowViewModel.From(task, ShowTaskDetailsCommand));
        }

        StatusMessage = Tasks.Count == 0
            ? "暂无历史记录。完成、失败或取消的传输任务会显示在这里。"
            : $"当前页历史记录 {Tasks.Count} 条。";
        IsLoading = false;
        RaiseTaskCollectionStateChanged();
        ClearHistoryCommand.RaiseCanExecuteChanged();
    }

    private void RaiseTaskCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(IsTaskListVisible));
    }

    private async Task ClearHistoryAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
            "清理历史记录",
            "确定要清理传输历史记录吗？\n\n当前历史视图中的已结束任务记录将被清理，此操作不可撤销。",
            "清理",
            "取消")).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var result = await _transfers.ClearHistoryAsync(new ClearTransferHistoryRequest()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = result.Error?.Message ?? "清理历史记录失败。";
            SetErrorDetails("清理历史记录失败", "清理历史记录失败。", result.Error);
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
            return;
        }

        _messages.Info("传输历史已清理。");
        PageIndex = 1;
        await RefreshAsync().ConfigureAwait(true);
    }

    private Task ShowErrorDetailsAsync()
    {
        return _lastErrorDetails is null
            ? Task.CompletedTask
            : _dialogs.ShowErrorDetailsAsync(_lastErrorDetails);
    }

    private Task ShowTaskDetailsAsync(object? parameter)
    {
        if (parameter is not TransferHistoryRowViewModel row)
        {
            return Task.CompletedTask;
        }

        return _dialogs.ShowErrorDetailsAsync(new ErrorDialogRequest(
            "传输历史详情",
            row.Reason ?? row.Result,
            Details: new Dictionary<string, string>
            {
                ["任务"] = row.FileName,
                ["方向"] = row.Direction,
                ["目标 / 来源"] = row.TargetOrSource,
                ["结果"] = row.Result,
                ["原因"] = row.Reason ?? "-",
                ["完成时间"] = row.CompletedAt
            }));
    }

    private void SetErrorDetails(string title, string fallbackSummary, StorageError? error)
    {
        _lastErrorDetails = ErrorDialogRequest.FromError(title, fallbackSummary, error);
        OnPropertyChanged(nameof(HasErrorDetails));
        ShowErrorDetailsCommand.RaiseCanExecuteChanged();
    }

    private void ClearErrorDetails()
    {
        if (_lastErrorDetails is null)
        {
            return;
        }

        _lastErrorDetails = null;
        OnPropertyChanged(nameof(HasErrorDetails));
        ShowErrorDetailsCommand.RaiseCanExecuteChanged();
    }
}

public sealed record TransferHistoryRowViewModel(
    string FileName,
    string Direction,
    string TargetOrSource,
    string Result,
    string CompletedAt,
    string? Reason,
    bool HasDetails,
    System.Windows.Input.ICommand DetailsCommand)
{
    public static TransferHistoryRowViewModel From(
        TransferStateSnapshot snapshot,
        System.Windows.Input.ICommand detailsCommand)
    {
        var task = snapshot.Task;
        var targetOrSource = task.Direction == TransferDirection.Upload
            ? task.RemotePath.ToString()
            : task.LocalPath.ToString();

        return new TransferHistoryRowViewModel(
            GetFileName(task),
            task.Direction == TransferDirection.Upload ? "上传" : "下载",
            string.IsNullOrWhiteSpace(targetOrSource) ? "/" : targetOrSource,
            FormatResult(task.Status),
            task.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
            task.StatusReason,
            !string.IsNullOrWhiteSpace(task.StatusReason) || task.Status is TransferStatus.Failed or TransferStatus.Interrupted,
            detailsCommand);
    }

    private static string GetFileName(TransferTask task)
    {
        var name = task.Direction == TransferDirection.Upload
            ? task.LocalPath.GetFileName()
            : task.RemotePath.Name;

        return string.IsNullOrWhiteSpace(name) ? "(未命名)" : name;
    }

    private static string FormatResult(TransferStatus status)
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
}
