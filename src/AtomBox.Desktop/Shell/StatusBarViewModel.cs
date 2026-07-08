using AtomBox.Application.Transfers;
using AtomBox.Core.Transfers;
using AtomBox.Desktop.ViewModels;
using Avalonia.Threading;

namespace AtomBox.Desktop.Shell;

public sealed class StatusBarViewModel : ViewModelBase
{
    private readonly TransferAppService _transfers;
    private readonly DispatcherTimer _refreshTimer;
    private string _stateText = "就绪";
    private int _activeTransferCount;
    private int _runningTransfers;
    private string _uploadSpeedText = "上传 0 KB/s";
    private string _downloadSpeedText = "下载 0 KB/s";
    private int _errorCount;
    private bool _isRefreshing;

    public StatusBarViewModel(TransferAppService transfers)
    {
        _transfers = transfers;
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    public string StateText
    {
        get => _stateText;
        private set => SetProperty(ref _stateText, value);
    }

    public int ActiveTransferCount
    {
        get => _activeTransferCount;
        private set
        {
            if (SetProperty(ref _activeTransferCount, value))
            {
                OnPropertyChanged(nameof(IsActiveTransferBadgeVisible));
                OnPropertyChanged(nameof(ActiveTransferBadgeText));
            }
        }
    }

    public bool IsActiveTransferBadgeVisible => ActiveTransferCount > 0;

    public string ActiveTransferBadgeText => ActiveTransferCount > 99 ? "99+" : ActiveTransferCount.ToString();

    public int RunningTransfers
    {
        get => _runningTransfers;
        private set => SetProperty(ref _runningTransfers, value);
    }

    public string UploadSpeedText
    {
        get => _uploadSpeedText;
        private set => SetProperty(ref _uploadSpeedText, value);
    }

    public string DownloadSpeedText
    {
        get => _downloadSpeedText;
        private set => SetProperty(ref _downloadSpeedText, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        private set => SetProperty(ref _errorCount, value);
    }

    public void ReportTransferTasksCreated(int createdCount)
    {
        if (createdCount <= 0)
        {
            return;
        }

        ActiveTransferCount += createdCount;
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var result = await _transfers.GetQueueAsync(new GetTransferQueueRequest()).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StateText = result.Error?.Message ?? "传输状态读取失败";
                ActiveTransferCount = 0;
                return;
            }

            var tasks = result.GetValueOrThrow().Tasks;
            ActiveTransferCount = tasks.Count;
            RunningTransfers = tasks.Count(item => item.Task.Status == TransferStatus.Running);
            ErrorCount = tasks.Count(item => item.Task.Status is TransferStatus.Failed or TransferStatus.Interrupted);
            UploadSpeedText = $"上传 {FormatSpeed(SumSpeed(tasks, TransferDirection.Upload))}";
            DownloadSpeedText = $"下载 {FormatSpeed(SumSpeed(tasks, TransferDirection.Download))}";
            StateText = RunningTransfers > 0 ? "传输中" : "就绪";
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
}
