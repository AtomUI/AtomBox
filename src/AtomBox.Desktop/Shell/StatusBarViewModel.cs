using AtomBox.Application.Transfers;
using AtomBox.Core.Transfers;
using AtomBox.Desktop.ViewModels;
using AtomBox.Desktop.Localization;
using Avalonia.Threading;

namespace AtomBox.Desktop.Shell;

public sealed class StatusBarViewModel : LocalizedViewModelBase
{
    private readonly TransferAppService _transfers;
    private readonly DispatcherTimer _refreshTimer;
    private string _stateKey = "Ready";
    private int _activeTransferCount;
    private int _runningTransfers;
    private double _uploadSpeed;
    private double _downloadSpeed;
    private int _errorCount;
    private bool _isRefreshing;
    private bool _refreshPending;

    public StatusBarViewModel(TransferAppService transfers, ILocalizationService localization)
        : base(localization)
    {
        _transfers = transfers;
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += (_, _) => RequestQueueRefresh();
        _refreshTimer.Start();
        RequestQueueRefresh();
    }

    public string StateText => Localization.GetString("Shell", _stateKey);

    public int ActiveTransferCount
    {
        get => _activeTransferCount;
        private set
        {
            if (SetProperty(ref _activeTransferCount, value))
            {
            }
        }
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

    public string RunningTransfersText => Localization.Format("Shell", "RunningCount", RunningTransfers);

    public string UploadSpeedText => Localization.Format("Shell", "UploadSpeed", FormatSpeed(_uploadSpeed));

    public string DownloadSpeedText => Localization.Format("Shell", "DownloadSpeed", FormatSpeed(_downloadSpeed));

    public string ErrorCountText => Localization.Format("Shell", "ErrorCount", ErrorCount);

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

    public void RequestQueueRefresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RequestQueueRefresh);
            return;
        }

        _refreshPending = true;
        if (!_isRefreshing)
        {
            _ = RefreshUntilSettledAsync();
        }
    }

    private async Task RefreshUntilSettledAsync()
    {
        _isRefreshing = true;
        try
        {
            while (_refreshPending)
            {
                _refreshPending = false;
                var result = await _transfers.GetQueueAsync(new GetTransferQueueRequest()).ConfigureAwait(true);
                if (result.IsFailure)
                {
                    _stateKey = "TransferStatusReadFailed";
                    OnPropertyChanged(nameof(StateText));
                    continue;
                }

                var tasks = result.GetValueOrThrow().Tasks;
                ActiveTransferCount = tasks.Count;
                RunningTransfers = tasks.Count(item => item.Task.Status == TransferStatus.Running);
                ErrorCount = tasks.Count(item => item.Task.Status is TransferStatus.Failed or TransferStatus.Interrupted);
                _uploadSpeed = SumSpeed(tasks, TransferDirection.Upload);
                _downloadSpeed = SumSpeed(tasks, TransferDirection.Download);
                _stateKey = RunningTransfers > 0 ? "Transferring" : "Ready";
                OnPropertyChanged(nameof(UploadSpeedText));
                OnPropertyChanged(nameof(DownloadSpeedText));
                OnPropertyChanged(nameof(StateText));
            }
        }
        finally
        {
            _isRefreshing = false;
        }
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

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(RunningTransfersText));
        OnPropertyChanged(nameof(UploadSpeedText));
        OnPropertyChanged(nameof(DownloadSpeedText));
        OnPropertyChanged(nameof(ErrorCountText));
    }

    public override void Dispose()
    {
        _refreshTimer.Stop();
        base.Dispose();
    }
}
