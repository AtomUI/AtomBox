using AtomBox.Core.Transfers;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.ViewModels;
using AtomBox.Transfer.State;
using AtomUI.City.Mvvm;
using AtomUI.City.State;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.Shell;

public sealed class StatusBarViewModel : ViewModelBase
{
    private readonly IAtomBoxTransferState _transferState;
    private readonly IAtomBoxLocalizedText _localization;
    private readonly CityUiDispatcher _dispatcher;
    private string _stateKey = "Ready";
    private int _activeTransferCount;
    private int _runningTransfers;
    private double _uploadSpeed;
    private double _downloadSpeed;
    private int _errorCount;

    public StatusBarViewModel(
        IAtomBoxTransferState transferState,
        IAtomBoxLocalizedText localization,
        CityUiDispatcher dispatcher)
    {
        _transferState = transferState;
        _localization = localization;
        _dispatcher = dispatcher;
    }

    public string StateText => _localization.GetString("Shell", _stateKey);

    public int ActiveTransferCount
    {
        get => _activeTransferCount;
        private set => SetProperty(ref _activeTransferCount, value);
    }

    public int RunningTransfers
    {
        get => _runningTransfers;
        private set
        {
            if (SetProperty(ref _runningTransfers, value))
            {
                OnPropertyChanged(nameof(RunningTransfersText));
            }
        }
    }

    public string RunningTransfersText => _localization.Format("Shell", "RunningCount", RunningTransfers);

    public string UploadSpeedText => _localization.Format("Shell", "UploadSpeed", FormatSpeed(_uploadSpeed));

    public string DownloadSpeedText => _localization.Format("Shell", "DownloadSpeed", FormatSpeed(_downloadSpeed));

    public string ErrorCountText => _localization.Format("Shell", "ErrorCount", ErrorCount);

    public int ErrorCount
    {
        get => _errorCount;
        private set
        {
            if (SetProperty(ref _errorCount, value))
            {
                OnPropertyChanged(nameof(ErrorCountText));
            }
        }
    }

    protected override ValueTask OnActivatedAsync(
        ActivationContext context,
        CancellationToken cancellationToken)
    {
        _localization.LanguageChanged += HandleLanguageChanged;
        context.Scope.Add(EventSubscription.Create(
            () => _localization.LanguageChanged -= HandleLanguageChanged));
        context.Scope.Add(_transferState.Queue.Observe(
            args => ApplyQueue(args.NewValue),
            StateSubscriptionOptions.Dispatcher(_dispatcher)));
        return ValueTask.CompletedTask;
    }

    private void ApplyQueue(TransferQueueState state)
    {
        if (state.Status != TransferProjectionStatus.Ready)
        {
            ActiveTransferCount = 0;
            RunningTransfers = 0;
            ErrorCount = 0;
            _uploadSpeed = 0;
            _downloadSpeed = 0;
            _stateKey = state.Status == TransferProjectionStatus.Faulted
                ? "TransferStatusReadFailed"
                : "Ready";
            RaiseStatusTextChanged();
            return;
        }

        var tasks = state.Tasks;
        ActiveTransferCount = tasks.Count;
        RunningTransfers = tasks.Count(item => item.Task.Status == TransferStatus.Running);
        ErrorCount = tasks.Count(item => item.Task.Status is TransferStatus.Failed or TransferStatus.Interrupted);
        _uploadSpeed = SumSpeed(tasks, TransferDirection.Upload);
        _downloadSpeed = SumSpeed(tasks, TransferDirection.Download);
        _stateKey = RunningTransfers > 0 ? "Transferring" : "Ready";
        RaiseStatusTextChanged();
    }

    private void RaiseStatusTextChanged()
    {
        OnPropertyChanged(nameof(UploadSpeedText));
        OnPropertyChanged(nameof(DownloadSpeedText));
        OnPropertyChanged(nameof(StateText));
    }

    private static double SumSpeed(IEnumerable<TransferStateSnapshot> tasks, TransferDirection direction)
    {
        return tasks
            .Where(item => item.Task.Direction == direction && item.Task.Status == TransferStatus.Running)
            .Sum(item => item.Progress?.SpeedBytesPerSecond ?? 0);
    }

    private static string FormatSpeed(double bytesPerSecond)
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

    private void HandleLanguageChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(RunningTransfersText));
        OnPropertyChanged(nameof(UploadSpeedText));
        OnPropertyChanged(nameof(DownloadSpeedText));
        OnPropertyChanged(nameof(ErrorCountText));
    }
}
