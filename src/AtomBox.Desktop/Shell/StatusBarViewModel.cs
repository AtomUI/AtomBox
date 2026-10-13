using AtomBox.Application.Transfers;
using AtomBox.Core.Transfers;
using AtomBox.Desktop.ViewModels;
using AtomBox.Desktop.Localization;
using AtomUI.City.Mvvm;
using System.Threading.Channels;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.Shell;

public sealed class StatusBarViewModel : ViewModelBase
{
    private readonly TransferAppService _transfers;
    private readonly IAtomBoxLocalizedText _localization;
    private readonly CityUiDispatcher _dispatcher;
    private Channel<byte>? _refreshRequests;
    private Task? _refreshWorker;
    private string _stateKey = "Ready";
    private int _activeTransferCount;
    private int _runningTransfers;
    private double _uploadSpeed;
    private double _downloadSpeed;
    private int _errorCount;

    public StatusBarViewModel(
        TransferAppService transfers,
        IAtomBoxLocalizedText localization,
        CityUiDispatcher dispatcher)
    {
        _transfers = transfers;
        _localization = localization;
        _dispatcher = dispatcher;
    }

    public string StateText => _localization.GetString("Shell", _stateKey);

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

    public void RequestQueueRefresh()
    {
        _refreshRequests?.Writer.TryWrite(0);
    }

    protected override ValueTask OnActivatedAsync(
        ActivationContext context,
        CancellationToken cancellationToken)
    {
        _localization.LanguageChanged += HandleLanguageChanged;
        context.Scope.Add(EventSubscription.Create(
            () => _localization.LanguageChanged -= HandleLanguageChanged));

        return _dispatcher.PostAsync(
            async token =>
            {
                var requests = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                });
                _refreshRequests = requests;
                context.Scope.Add(EventSubscription.Create(
                    () => ReleaseRefreshRequests(requests)));

                await RefreshAsync(token).ConfigureAwait(true);
                _refreshWorker = context.Scope.StartUniqueAsync(
                    "status-bar-refresh",
                    workerToken => RunRefreshWorkerAsync(requests, workerToken),
                    token);
            },
            cancellationToken);
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _refreshRequests, null)?.Writer.TryComplete();
        _refreshWorker = null;
        return ValueTask.CompletedTask;
    }

    private void ReleaseRefreshRequests(Channel<byte> requests)
    {
        Interlocked.CompareExchange(ref _refreshRequests, null, requests);
        requests.Writer.TryComplete();
        _refreshWorker = null;
    }

    private async Task RunRefreshWorkerAsync(
        Channel<byte> requests,
        CancellationToken cancellationToken)
    {
        var timerTask = PumpTimerAsync(requests.Writer, cancellationToken);
        try
        {
            while (await requests.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(true))
            {
                while (requests.Reader.TryRead(out _))
                {
                }

                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        finally
        {
            requests.Writer.TryComplete();
            try
            {
                await timerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private static async Task PumpTimerAsync(
        ChannelWriter<byte> writer,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            writer.TryWrite(0);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var result = await _transfers.GetQueueAsync(
            new GetTransferQueueRequest(),
            cancellationToken).ConfigureAwait(true);
        if (result.IsFailure)
        {
            _stateKey = "TransferStatusReadFailed";
            OnPropertyChanged(nameof(StateText));
            return;
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
