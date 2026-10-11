using System.Collections.ObjectModel;
using AtomBox.Application.Transfers;
using AtomBox.Core.Errors;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Core.Transfers;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.Shell;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class TransferHistoryViewModel : LocalizedViewModelBase
{
    private readonly TransferAppService _transfers;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly StatusBarViewModel _statusBar;
    private ErrorDialogRequest? _lastErrorDetails;
    private int _pageIndex = 1;
    private LocalizedText _statusMessage = new("Transfer", "LoadingHistory");
    private IReadOnlyList<TransferStateSnapshot> _currentSnapshots = [];
    private bool _isLoading;
    private TransferHistoryRowViewModel? _selectedTask;

    public TransferHistoryViewModel(
        TransferAppService transfers,
        IMessageService messages,
        IDialogService dialogs,
        StatusBarViewModel statusBar,
        IAtomBoxLocalizedText localization)
        : base(localization)
    {
        _transfers = transfers;
        _messages = messages;
        _dialogs = dialogs;
        _statusBar = statusBar;
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

    public string Title => Localization.GetString("Transfer", "Title");

    public ObservableCollection<TransferHistoryRowViewModel> Tasks { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ClearHistoryCommand { get; }

    public AsyncRelayCommand PreviousPageCommand { get; }

    public AsyncRelayCommand NextPageCommand { get; }

    public AsyncRelayCommand ShowErrorDetailsCommand { get; }

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
                OnPropertyChanged(nameof(PageText));
            }
        }
    }

    public string PageText => Localization.Format("Transfer", "CurrentPage", PageIndex);

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(StatusMessage));
        RebuildRows();
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
        SetStatus(new("Transfer", "LoadingHistory"));
        ClearErrorDetails();

        var result = await _transfers.GetHistoryAsync(new GetTransferHistoryRequest(PageIndex)).ConfigureAwait(true);
        Tasks.Clear();

        if (result.IsFailure)
        {
            SetStatus(new("Transfer", "HistoryLoadFailed"));
            SetErrorDetails(Localization.GetString("Transfer", "HistoryLoadFailed"), Localization.GetString("Transfer", "HistoryLoadFailed"), result.Error);
            IsLoading = false;
            RaiseTaskCollectionStateChanged();
            return;
        }

        var page = result.GetValueOrThrow();
        PageIndex = page.PageIndex;

        _currentSnapshots = page.Tasks;
        RebuildRows();

        SetStatus(Tasks.Count == 0
            ? new("Transfer", "NoHistory")
            : new("Transfer", "HistoryPageCount", Tasks.Count));
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
            Localization.GetString("Transfer", "ClearHistoryTitle"),
            Localization.GetString("Transfer", "ClearHistoryMessage"),
            Localization.GetString("Transfer", "Clear"),
            Localization.GetString("Common", "Cancel"))).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var result = await _transfers.ClearHistoryAsync(new ClearTransferHistoryRequest()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            SetStatus(new("Transfer", "ClearHistoryFailed"));
            SetErrorDetails(Localization.GetString("Transfer", "ClearHistoryFailed"), Localization.GetString("Transfer", "ClearHistoryFailed"), result.Error);
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
            return;
        }

        _messages.Info(Localization.GetString("Transfer", "HistoryCleared"));
        _statusBar.RequestQueueRefresh();
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
            Localization.GetString("Transfer", "HistoryDetails"),
            row.Reason ?? row.Result,
            Details: new Dictionary<string, string>
            {
                [Localization.GetString("Common", "Task")] = row.FileName,
                [Localization.GetString("Common", "Direction")] = row.Direction,
                [Localization.GetString("Common", "TargetSource")] = row.TargetOrSource,
                [Localization.GetString("Common", "Result")] = row.Result,
                [Localization.GetString("Common", "Reason")] = row.Reason ?? "-",
                [Localization.GetString("Common", "CompletedAt")] = row.CompletedAt
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

    private void SetStatus(LocalizedText value)
    {
        _statusMessage = value;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void RebuildRows()
    {
        Tasks.Clear();
        foreach (var task in _currentSnapshots)
        {
            Tasks.Add(TransferHistoryRowViewModel.From(task, ShowTaskDetailsCommand, Localization));
        }
    }
}

public sealed record TransferHistoryRowViewModel(
    string FileName,
    string Direction,
    string TargetOrSource,
    string Result,
    string ResultTagColor,
    string CompletedAt,
    string? Reason,
    bool HasDetails,
    System.Windows.Input.ICommand DetailsCommand)
{
    public static TransferHistoryRowViewModel From(
        TransferStateSnapshot snapshot,
        System.Windows.Input.ICommand detailsCommand,
        IAtomBoxLocalizedText localization)
    {
        var task = snapshot.Task;
        var targetOrSource = task.Direction == TransferDirection.Upload
            ? task.RemotePath.ToString()
            : task.LocalPath.ToString();

        return new TransferHistoryRowViewModel(
            GetFileName(task),
            localization.GetString("Transfer", task.Direction == TransferDirection.Upload ? "Upload" : "Download"),
            string.IsNullOrWhiteSpace(targetOrSource) ? "/" : targetOrSource,
            FormatResult(task.Status, localization),
            FormatResultTagColor(task.Status),
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

        return string.IsNullOrWhiteSpace(name) ? "-" : name;
    }

    private static string FormatResult(TransferStatus status, IAtomBoxLocalizedText localization)
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

    private static string FormatResultTagColor(TransferStatus status)
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
