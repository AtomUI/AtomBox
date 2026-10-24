using System.Collections.ObjectModel;
using AtomBox.Application.Transfers;
using AtomBox.Core.Errors;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Core.Transfers;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.ViewModels;
using AtomUI.City.Mvvm;
using CommunityToolkit.Mvvm.Input;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class TransferHistoryViewModel : DesktopViewModelBase
{
    private readonly TransferAppService _transfers;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly IAtomBoxLocalizedText _localization;
    private readonly CityUiDispatcher _dispatcher;
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
        IAtomBoxLocalizedText localization,
        DesktopCommandFactory commands,
        CityUiDispatcher dispatcher)
    {
        _transfers = transfers;
        _messages = messages;
        _dialogs = dialogs;
        _localization = localization;
        _dispatcher = dispatcher;
        RefreshCommand = commands.CreateAsync(this, nameof(RefreshCommand), RefreshAsync);
        ClearHistoryCommand = commands.CreateAsync(
            this,
            nameof(ClearHistoryCommand),
            ClearHistoryAsync,
            () => HasTasks);
        ShowErrorDetailsCommand = commands.CreateAsync(
            this,
            nameof(ShowErrorDetailsCommand),
            ShowErrorDetailsAsync,
            () => HasErrorDetails);
        ShowTaskDetailsCommand = commands.CreateAsync<TransferHistoryRowViewModel>(
            this,
            nameof(ShowTaskDetailsCommand),
            ShowTaskDetailsAsync,
            row => row is { HasDetails: true });
        PreviousPageCommand = commands.CreateAsync(
            this,
            nameof(PreviousPageCommand),
            GoToPreviousPageAsync,
            () => PageIndex > 1);
        NextPageCommand = commands.CreateAsync(this, nameof(NextPageCommand), GoToNextPageAsync);
    }

    public string Title => _localization.GetString("Transfer", "Title");

    public ObservableCollection<TransferHistoryRowViewModel> Tasks { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand ClearHistoryCommand { get; }

    public IAsyncRelayCommand PreviousPageCommand { get; }

    public IAsyncRelayCommand NextPageCommand { get; }

    public IAsyncRelayCommand ShowErrorDetailsCommand { get; }

    public IAsyncRelayCommand<TransferHistoryRowViewModel> ShowTaskDetailsCommand { get; }

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
                PreviousPageCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(PageText));
            }
        }
    }

    public string PageText => _localization.Format("Transfer", "CurrentPage", PageIndex);

    private void HandleLanguageChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(StatusMessage));
        RebuildRows();
    }

    public void ApplyNavigationParameter(TransferHistoryNavigationParameter parameter)
    {
        PageIndex = Math.Max(parameter.PageIndex ?? 1, 1);
    }

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

    private async Task GoToPreviousPageAsync(CancellationToken cancellationToken)
    {
        if (PageIndex <= 1)
        {
            return;
        }

        PageIndex--;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task GoToNextPageAsync(CancellationToken cancellationToken)
    {
        PageIndex++;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        SetStatus(new("Transfer", "LoadingHistory"));
        ClearErrorDetails();

        var result = await _transfers.GetHistoryAsync(
            new GetTransferHistoryRequest(PageIndex),
            cancellationToken).ConfigureAwait(true);
        Tasks.Clear();

        if (result.IsFailure)
        {
            SetStatus(new("Transfer", "HistoryLoadFailed"));
            SetErrorDetails(_localization.GetString("Transfer", "HistoryLoadFailed"), _localization.GetString("Transfer", "HistoryLoadFailed"), result.Error);
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
        ClearHistoryCommand.NotifyCanExecuteChanged();
    }

    private void RaiseTaskCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(IsTaskListVisible));
    }

    private async Task ClearHistoryAsync(CancellationToken cancellationToken)
    {
        var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
            _localization.GetString("Transfer", "ClearHistoryTitle"),
            _localization.GetString("Transfer", "ClearHistoryMessage"),
            _localization.GetString("Transfer", "Clear"),
            _localization.GetString("Common", "Cancel"))).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var result = await _transfers.ClearHistoryAsync(
            new ClearTransferHistoryRequest(),
            cancellationToken).ConfigureAwait(true);
        if (result.IsFailure)
        {
            SetStatus(new("Transfer", "ClearHistoryFailed"));
            SetErrorDetails(_localization.GetString("Transfer", "ClearHistoryFailed"), _localization.GetString("Transfer", "ClearHistoryFailed"), result.Error);
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
            return;
        }

        _messages.Info(_localization.GetString("Transfer", "HistoryCleared"));
        PageIndex = 1;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private Task ShowErrorDetailsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _lastErrorDetails is null
            ? Task.CompletedTask
            : _dialogs.ShowErrorDetailsAsync(_lastErrorDetails);
    }

    private Task ShowTaskDetailsAsync(
        TransferHistoryRowViewModel? parameter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parameter is not TransferHistoryRowViewModel row)
        {
            return Task.CompletedTask;
        }

        return _dialogs.ShowErrorDetailsAsync(new ErrorDialogRequest(
            _localization.GetString("Transfer", "HistoryDetails"),
            row.Reason ?? row.Result,
            Details: new Dictionary<string, string>
            {
                [_localization.GetString("Common", "Task")] = row.FileName,
                [_localization.GetString("Common", "Direction")] = row.Direction,
                [_localization.GetString("Common", "TargetSource")] = row.TargetOrSource,
                [_localization.GetString("Common", "Result")] = row.Result,
                [_localization.GetString("Common", "Reason")] = row.Reason ?? "-",
                [_localization.GetString("Common", "CompletedAt")] = row.CompletedAt
            }));
    }

    private void SetErrorDetails(string title, string fallbackSummary, StorageError? error)
    {
        _lastErrorDetails = ErrorDialogRequest.FromError(title, fallbackSummary, error);
        OnPropertyChanged(nameof(HasErrorDetails));
        ShowErrorDetailsCommand.NotifyCanExecuteChanged();
    }

    private void ClearErrorDetails()
    {
        if (_lastErrorDetails is null)
        {
            return;
        }

        _lastErrorDetails = null;
        OnPropertyChanged(nameof(HasErrorDetails));
        ShowErrorDetailsCommand.NotifyCanExecuteChanged();
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
            Tasks.Add(TransferHistoryRowViewModel.From(task, ShowTaskDetailsCommand, _localization));
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
            !string.IsNullOrWhiteSpace(task.StatusReason) || task.Status is TransferStatus.Failed or TransferStatus.Interrupted or TransferStatus.Abandoned,
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
            TransferStatus.Abandoned => localization.GetString("Transfer", "Abandoned"),
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
            TransferStatus.Abandoned => "Grey",
            _ => "Info"
        };
    }
}
