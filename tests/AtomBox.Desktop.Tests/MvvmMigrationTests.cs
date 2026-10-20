using System.ComponentModel;
using System.Globalization;
using AtomBox.Application.Accounts;
using AtomBox.Application.State;
using AtomBox.Application.Transfers;
using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.Shell;
using AtomBox.Desktop.ViewModels;
using AtomBox.Desktop.ViewModels.Pages;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Providers.Common;
using AtomBox.Transfer;
using AtomBox.Transfer.Scheduling;
using AtomBox.Transfer.State;
using AtomUI.City.Mvvm;
using AtomUI.City.Presentation;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.Tests;

public sealed class MvvmMigrationTests
{
    [Fact]
    public void DesktopViewModelsUseCityBaseAndToolkitCommandContracts()
    {
        Type[] viewModelTypes =
        [
            typeof(MainWindowViewModel),
            typeof(StatusBarViewModel),
            typeof(HomeViewModel),
            typeof(RemoteBrowserViewModel),
            typeof(TransferQueueViewModel),
            typeof(TransferHistoryViewModel),
            typeof(SettingsViewModel),
            typeof(AccountManagementViewModel),
        ];

        Assert.All(viewModelTypes, type => Assert.True(type.IsSubclassOf(typeof(ViewModelBase))));
        Assert.Equal(
            typeof(IAsyncRelayCommand),
            typeof(HomeViewModel).GetProperty(nameof(HomeViewModel.OpenRemoteStorageCommand))!.PropertyType);
        Assert.Equal(
            typeof(IAsyncRelayCommand<TransferTaskRowViewModel>),
            typeof(TransferQueueViewModel).GetProperty(nameof(TransferQueueViewModel.ExecuteTaskActionCommand))!.PropertyType);
        Assert.Null(typeof(HomeViewModel).Assembly.GetType("AtomBox.Desktop.ViewModels.LocalizedViewModelBase"));
        Assert.Null(typeof(HomeViewModel).Assembly.GetType("AtomBox.Desktop.ViewModels.AsyncRelayCommand"));
        Assert.Null(typeof(HomeViewModel).Assembly.GetType("AtomBox.Desktop.ViewModels.RelayCommand"));
    }

    [Fact]
    public void CityStateDependencyIsOwnedByApplicationAndTransferWhileOtherBusinessModulesStayIndependent()
    {
        var desktopReferences = typeof(MainWindowViewModel).Assembly.GetReferencedAssemblies();
        Assert.Contains(desktopReferences, reference => reference.Name == "AtomUI.City.Mvvm");

        var applicationReferences = typeof(AccountAppService).Assembly.GetReferencedAssemblies();
        Assert.Contains(applicationReferences, reference => reference.Name == "AtomUI.City.State");
        Assert.DoesNotContain(
            applicationReferences,
            reference => reference.Name?.StartsWith("AtomUI.City", StringComparison.Ordinal) == true &&
                         reference.Name != "AtomUI.City.State");

        var transferReferences = typeof(AtomBoxTransferModule).Assembly.GetReferencedAssemblies();
        Assert.Contains(transferReferences, reference => reference.Name == "AtomUI.City.State");
        Assert.Contains(transferReferences, reference => reference.Name == "AtomUI.City.Core");
        Assert.DoesNotContain(
            transferReferences,
            reference => reference.Name?.StartsWith("AtomUI.City", StringComparison.Ordinal) == true &&
                         reference.Name is not ("AtomUI.City.State" or "AtomUI.City.Core"));

        var cityIndependentAssemblies = new[]
        {
            typeof(StorageAccount).Assembly,
            typeof(AtomBoxStoragePaths).Assembly,
            typeof(StorageProviderRegistry).Assembly,
        };

        Assert.All(
            cityIndependentAssemblies,
            assembly => Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                reference => reference.Name?.StartsWith("AtomUI.City", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task HomeNavigationCommandsFollowShellActivationWhileLocalizationFollowsPageActivation()
    {
        await using var context = await DesktopTestContext.CreateAsync();
        var localization = new TestLocalizedText();
        var dispatcher = new ImmediateUiDispatcher();
        var shellNavigation = new TestShellNavigationController();
        var viewModel = ActivatorUtilities.CreateInstance<HomeViewModel>(
            context.Services,
            localization,
            dispatcher,
            shellNavigation);
        var subtitleChanges = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HomeViewModel.Subtitle))
            {
                subtitleChanges++;
            }
        };

        Assert.False(viewModel.OpenRemoteStorageCommand.CanExecute(null));
        var pageScope = new ActivationScope();
        await viewModel.ActivateAsync(pageScope);
        Assert.False(viewModel.OpenRemoteStorageCommand.CanExecute(null));

        var shellScope = new ActivationScope();
        await shellNavigation.ActivateAsync(shellScope);
        Assert.True(viewModel.OpenRemoteStorageCommand.CanExecute(null));
        await viewModel.OpenRemoteStorageCommand.ExecuteAsync(null);
        Assert.Equal(NavigationMenuKeys.RemoteStorage, shellNavigation.LastMenuKey);

        localization.Publish();
        Assert.Equal(1, subtitleChanges);

        await viewModel.DeactivateAsync();
        Assert.True(viewModel.OpenRemoteStorageCommand.CanExecute(null));
        localization.Publish();
        Assert.Equal(1, subtitleChanges);

        await shellNavigation.DeactivateAsync();
        Assert.False(viewModel.OpenRemoteStorageCommand.CanExecute(null));
        await viewModel.DisposeAsync();
        await shellNavigation.DisposeAsync();
    }

    [Fact]
    public async Task DesktopCommandFactoryDispatchesOwnerStateNotificationsToUi()
    {
        var dispatcher = new QueueingUiDispatcher();
        var owner = new TestCommandViewModel();
        var services = new ServiceCollection();
        services.AddSingleton<CityUiDispatcher>(dispatcher);
        services.AddPresentation();
        await using var serviceProvider = services.BuildServiceProvider();
        var factory = new DesktopCommandFactory(
            new NoopCommandExceptionHandler(),
            serviceProvider.GetRequiredService<IUiCommandFactory>());
        var command = factory.CreateAsync(owner, "refresh", _ => Task.CompletedTask);
        var notifications = 0;
        command.CanExecuteChanged += (_, _) => notifications++;

        await owner.ActivateAsync(new ActivationScope());

        Assert.Equal(0, notifications);
        Assert.True(dispatcher.PendingCount > 0);
        var pendingNotifications = dispatcher.PendingCount;

        dispatcher.Drain();

        Assert.Equal(pendingNotifications, notifications);

        await owner.DisposeAsync();
        dispatcher.Drain();
    }

    [Fact]
    public async Task TransferQueueObservesStateWithoutOwningAPeriodicWorker()
    {
        await using var context = await DesktopTestContext.CreateAsync();
        var viewModel = ActivatorUtilities.CreateInstance<TransferQueueViewModel>(
            context.Services,
            new TestLocalizedText(),
            new ImmediateUiDispatcher());

        var firstScope = new ActivationScope();
        await viewModel.ActivateAsync(firstScope);
        Assert.Equal(0, firstScope.ActiveTaskCount);

        await viewModel.DeactivateAsync();
        Assert.True(firstScope.IsDisposed);
        Assert.Equal(0, firstScope.ActiveTaskCount);

        var secondScope = new ActivationScope();
        await viewModel.ActivateAsync(secondScope);
        Assert.Equal(0, secondScope.ActiveTaskCount);

        await viewModel.DisposeAsync();
        Assert.True(secondScope.IsDisposed);
    }

    [Fact]
    public async Task TransferQueueAndStatusBarObserveCommittedTransferState()
    {
        await using var context = await DesktopTestContext.CreateAsync();
        var localization = new TestLocalizedText();
        var dispatcher = new ImmediateUiDispatcher();
        var queue = ActivatorUtilities.CreateInstance<TransferQueueViewModel>(
            context.Services,
            localization,
            dispatcher);
        var statusBar = ActivatorUtilities.CreateInstance<StatusBarViewModel>(
            context.Services,
            localization,
            dispatcher);
        await queue.ActivateAsync(new ActivationScope());
        await statusBar.ActivateAsync(new ActivationScope());
        var now = DateTimeOffset.UtcNow;
        var task = new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\state-observation.txt"),
            new RemotePath("bucket/state-observation.txt"),
            TransferStatus.Pending,
            new TransferOptions(TransferOverwritePolicy.Ask),
            now,
            now);
        var scheduler = context.Services.GetRequiredService<ITransferTaskScheduler>();

        var submitResult = await scheduler.SubmitAsync(task);
        await WaitUntilAsync(() => queue.Tasks.Any(item => item.TaskId == task.Id));

        Assert.True(submitResult.IsSuccess);
        Assert.Equal(1, statusBar.ActiveTransferCount);
        Assert.InRange(statusBar.RunningTransfers, 0, 1);

        var cancelResult = await scheduler.CancelAsync(task.Id);
        await WaitUntilAsync(() =>
            queue.Tasks.All(item => item.TaskId != task.Id) &&
            statusBar.ActiveTransferCount == 0);

        Assert.True(
            cancelResult.IsSuccess || cancelResult.Error?.Category == StorageErrorCategory.Conflict,
            "The task must either accept cancellation or have already crossed a terminal state boundary.");
        Assert.Equal(0, statusBar.ActiveTransferCount);

        await queue.DisposeAsync();
        await statusBar.DisposeAsync();
    }

    [Fact]
    public async Task TransferQueueUpdatesProgressWithoutReplacingTheRunningRow()
    {
        await using var context = await DesktopTestContext.CreateAsync();
        var queue = ActivatorUtilities.CreateInstance<TransferQueueViewModel>(
            context.Services,
            new TestLocalizedText(),
            new ImmediateUiDispatcher());
        await queue.ActivateAsync(new ActivationScope());
        var taskStore = context.Services.GetRequiredService<ITransferTaskStore>();
        var stateStore = context.Services.GetRequiredService<ITransferStateStore>();
        var synchronizer = context.Services.GetRequiredService<ITransferStateSynchronizer>();
        var now = DateTimeOffset.UtcNow;
        var task = new TransferTask(
            TransferTaskId.New(),
            StorageAccountId.New(),
            TransferDirection.Upload,
            new LocalPath(@"C:\stable-row.bin"),
            new RemotePath("uploads/stable-row.bin"),
            TransferStatus.Running,
            new TransferOptions(TransferOverwritePolicy.Ask),
            now,
            now);
        Assert.True((await taskStore.SaveAsync(task)).IsSuccess);
        Assert.True((await stateStore.SaveProgressBatchAsync(
        [
            new TransferProgressUpdate(
                task.Id,
                task.Revision,
                sequence: 1,
                new TransferProgress(10, 100, 1024))
        ])).IsSuccess);
        Assert.True((await synchronizer.RefreshAsync()).IsSuccess);
        var originalRow = Assert.Single(queue.Tasks);
        var collectionChanges = 0;
        var changedProperties = new List<string?>();
        var queuePropertyChanges = new List<string?>();
        queue.Tasks.CollectionChanged += (_, _) => collectionChanges++;
        originalRow.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        queue.PropertyChanged += (_, args) => queuePropertyChanges.Add(args.PropertyName);

        Assert.True((await stateStore.SaveProgressBatchAsync(
        [
            new TransferProgressUpdate(
                task.Id,
                task.Revision,
                sequence: 2,
                new TransferProgress(40, 100, 4096))
        ])).IsSuccess);
        Assert.True((await synchronizer.RefreshAsync()).IsSuccess);

        var updatedRow = Assert.Single(queue.Tasks);
        Assert.Same(originalRow, updatedRow);
        Assert.Equal(0, collectionChanges);
        Assert.Empty(queuePropertyChanges);
        Assert.Equal(40, updatedRow.ProgressValue);
        Assert.Equal("4 KB/s", updatedRow.Speed);
        Assert.Contains(nameof(TransferTaskRowViewModel.ProgressValue), changedProperties);
        Assert.Contains(nameof(TransferTaskRowViewModel.Speed), changedProperties);
        Assert.True(queue.ExecuteTaskActionCommand.CanExecute(updatedRow));
        await queue.DisposeAsync();
    }

    [Fact]
    public async Task ShellCompositionOwnsStatusBarChildAndObservesApplicationAccountState()
    {
        await using var context = await DesktopTestContext.CreateAsync();
        var localization = new TestLocalizedText();
        var dispatcher = new ImmediateUiDispatcher();
        var navigation = new TestNavigationService();
        var accounts = new TestAccountDialogWorkflow();
        var statusBar = ActivatorUtilities.CreateInstance<StatusBarViewModel>(
            context.Services,
            localization,
            dispatcher);
        var shell = ActivatorUtilities.CreateInstance<MainWindowViewModel>(
            context.Services,
            navigation,
            accounts,
            statusBar,
            dispatcher);
        var refreshCount = 0;
        shell.RemoteAccountMenuRefreshed += (_, _) => Interlocked.Increment(ref refreshCount);

        await using var owner = new ViewModelCompositionOwner("test-window");
        var root = await owner.AttachRootAsync("shell", ViewModelLease.Borrowed(shell));
        var child = await root.AttachChildAsync(
            "shell.status-bar",
            ViewModelLease.Borrowed(statusBar));

        Assert.True(root.IsActive);
        Assert.True(child.IsActive);
        Assert.Equal(root.CompositionId, child.ParentId);
        Assert.NotEqual(root.ActivationScope.Id, child.ActivationScope.Id);
        Assert.Single(root.Children);
        Assert.Equal(0, root.ActivationScope.ActiveTaskCount);
        Assert.Equal(0, child.ActivationScope.ActiveTaskCount);

        var accountService = context.Services.GetRequiredService<AccountAppService>();
        var beforeRefresh = Volatile.Read(ref refreshCount);
        var addResult = await accountService.AddAsync(new AddStorageAccountRequest(
            StorageProviderCategory.ObjectStorage,
            StorageProviderId.From("test-state-provider"),
            "State observation account",
            null,
            null,
            CredentialRef.From("test-state-credential")));
        Assert.True(addResult.IsSuccess);
        await WaitUntilAsync(() => Volatile.Read(ref refreshCount) > beforeRefresh);
        Assert.Equal(beforeRefresh + 1, Volatile.Read(ref refreshCount));

        await owner.DisposeAsync();
        Assert.True(root.IsDisposed);
        Assert.True(child.IsDisposed);
        Assert.Equal(ActivationState.Deactivated, shell.ActivationState);
        Assert.Equal(ActivationState.Deactivated, statusBar.ActivationState);

        await shell.DisposeAsync();
        await statusBar.DisposeAsync();
    }

    [Fact]
    public async Task MenuNavigationCancelsSupersededIntentAndCommitsLatestSelection()
    {
        await using var context = await DesktopTestContext.CreateAsync();
        var dispatcher = new ImmediateUiDispatcher();
        var navigation = new SupersedingNavigationService();
        var statusBar = ActivatorUtilities.CreateInstance<StatusBarViewModel>(
            context.Services,
            new TestLocalizedText(),
            dispatcher);
        var shell = ActivatorUtilities.CreateInstance<MainWindowViewModel>(
            context.Services,
            navigation,
            new TestAccountDialogWorkflow(),
            statusBar,
            dispatcher);

        var firstNavigation = shell.NavigateByMenuKeyAsync(NavigationMenuKeys.Home);
        await navigation.FirstNavigationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var latestNavigation = shell.NavigateByMenuKeyAsync(NavigationMenuKeys.Settings);
        await Task.WhenAll(firstNavigation, latestNavigation).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(navigation.FirstNavigationObservedCancellation);
        Assert.Equal(NavigationTarget.Settings, navigation.CommittedTarget);

        await shell.DisposeAsync();
        await statusBar.DisposeAsync();
    }

    [Fact]
    public async Task RemoteBrowserAppliesFinalNavigationParameterBeforeFirstActivationLoad()
    {
        await using var context = await DesktopTestContext.CreateAsync();
        var dispatcher = new ImmediateUiDispatcher();
        var workflow = new TestAccountDialogWorkflow();
        var navigation = new TestNavigationService();
        var viewModel = ActivatorUtilities.CreateInstance<RemoteBrowserViewModel>(
            context.Services,
            workflow,
            navigation,
            new TestLocalizedText(),
            dispatcher);

        viewModel.ApplyNavigationParameter(new RemoteBrowserNavigationParameter(
            NavigationResourceGroup.WebDav));

        Assert.Equal(ActivationState.Constructed, viewModel.ActivationState);
        Assert.Equal("WebDAV", viewModel.Title);
        Assert.False(viewModel.IsLoading);

        var scope = new ActivationScope();
        await viewModel.ActivateAsync(scope);
        Assert.Equal("WebDAV", viewModel.Title);
        Assert.Equal(1, scope.ActiveTaskCount);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task RemoteBrowserActivationDoesNotWaitForInitialRefreshWork()
    {
        await using var context = await DesktopTestContext.CreateAsync();
        var dispatcher = new BlockingPostUiDispatcher();
        var viewModel = ActivatorUtilities.CreateInstance<RemoteBrowserViewModel>(
            context.Services,
            new TestAccountDialogWorkflow(),
            new TestNavigationService(),
            new TestLocalizedText(),
            dispatcher);
        var scope = new ActivationScope();

        await viewModel.ActivateAsync(scope).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.PostStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ActivationState.Active, viewModel.ActivationState);
        Assert.Equal(1, scope.ActiveTaskCount);

        await viewModel.DisposeAsync();
        Assert.True(scope.IsDisposed);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, cancellation.Token);
        }
    }

    private sealed class DesktopTestContext : IAsyncDisposable
    {
        private readonly string _root;
        private readonly AtomUI.City.Core.Hosting.IApplicationHost _host;

        private DesktopTestContext(
            string root,
            AtomUI.City.Core.Hosting.IApplicationHost host)
        {
            _root = root;
            _host = host;
        }

        public IServiceProvider Services => _host.Services;

        public static async Task<DesktopTestContext> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "AtomBox.Desktop.Tests",
                Guid.NewGuid().ToString("N"));
            var host = Program.CreateHost([], new AtomBoxStoragePaths(root));
            await host.StartAsync();
            return new DesktopTestContext(root, host);
        }

        public async ValueTask DisposeAsync()
        {
            await _host.DisposeAsync();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (Directory.Exists(_root))
                    {
                        Directory.Delete(_root, recursive: true);
                    }

                    return;
                }
                catch (IOException) when (attempt < 19)
                {
                    await Task.Delay(25);
                }
            }
        }
    }

    private sealed class ImmediateUiDispatcher : CityUiDispatcher
    {
        public bool CheckAccess() => true;

        public ValueTask InvokeAsync(Action callback, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            callback();
            return ValueTask.CompletedTask;
        }

        public ValueTask<T> InvokeAsync<T>(Func<T> callback, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(callback());
        }

        public ValueTask PostAsync(
            Func<CancellationToken, ValueTask> callback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return callback(cancellationToken);
        }
    }

    private sealed class QueueingUiDispatcher : CityUiDispatcher
    {
        private readonly Queue<Func<ValueTask>> _queue = [];

        public int PendingCount => _queue.Count;

        public bool CheckAccess() => false;

        public ValueTask InvokeAsync(Action callback, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    callback();
                    completion.TrySetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }

                return ValueTask.CompletedTask;
            });
            return new ValueTask(completion.Task);
        }

        public ValueTask<T> InvokeAsync<T>(Func<T> callback, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(callback());
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }

                return ValueTask.CompletedTask;
            });
            return new ValueTask<T>(completion.Task);
        }

        public ValueTask PostAsync(
            Func<CancellationToken, ValueTask> callback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await callback(cancellationToken).ConfigureAwait(false);
                    completion.TrySetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
            return new ValueTask(completion.Task);
        }

        public void Drain()
        {
            while (_queue.TryDequeue(out var callback))
            {
                callback().GetAwaiter().GetResult();
            }
        }
    }

    private sealed class BlockingPostUiDispatcher : CityUiDispatcher
    {
        public TaskCompletionSource<bool> PostStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CheckAccess() => false;

        public ValueTask InvokeAsync(Action callback, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            callback();
            return ValueTask.CompletedTask;
        }

        public ValueTask<T> InvokeAsync<T>(Func<T> callback, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(callback());
        }

        public ValueTask PostAsync(
            Func<CancellationToken, ValueTask> callback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PostStarted.TrySetResult(true);
            return new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        }
    }

    private sealed class NoopCommandExceptionHandler : ICommandExceptionHandler
    {
        public ValueTask HandleAsync(
            CommandExceptionContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    public sealed class TestCommandViewModel : ViewModelBase;

    public sealed class TestShellNavigationController : ViewModelBase, IShellNavigationController
    {
        public string? LastMenuKey { get; private set; }

        public Task NavigateByMenuKeyAsync(
            string? itemKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastMenuKey = itemKey;
            return Task.CompletedTask;
        }
    }

    private sealed class TestLocalizedText : IAtomBoxLocalizedText
    {
        private long _revision;

        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler? LanguageChanged;

        public CultureInfo CurrentCulture { get; } = CultureInfo.InvariantCulture;

        public long Revision => Interlocked.Read(ref _revision);

        public string GetString(string catalog, string key) => $"{catalog}.{key}";

        public string Format(string catalog, string key, params object[] arguments) =>
            $"{catalog}.{key}:{string.Join(',', arguments)}";

        public void Publish()
        {
            Interlocked.Increment(ref _revision);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Revision)));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TestNavigationService : INavigationService
    {
        public event EventHandler? CurrentMenuKeyChanged;

        public string? CurrentMenuKey { get; private set; }

        public Task NavigateAsync(
            NavigationTarget target,
            object? parameter = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrentMenuKey = target.ToString();
            CurrentMenuKeyChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class SupersedingNavigationService : INavigationService
    {
        private readonly SemaphoreSlim _navigationGate = new(1, 1);
        private int _navigationCount;
        private int _firstNavigationObservedCancellation;

        public event EventHandler? CurrentMenuKeyChanged;

        public TaskCompletionSource<bool> FirstNavigationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? CurrentMenuKey { get; private set; }

        public NavigationTarget? CommittedTarget { get; private set; }

        public bool FirstNavigationObservedCancellation =>
            Volatile.Read(ref _firstNavigationObservedCancellation) != 0;

        public async Task NavigateAsync(
            NavigationTarget target,
            object? parameter = null,
            CancellationToken cancellationToken = default)
        {
            await _navigationGate.WaitAsync(cancellationToken);
            try
            {
                if (Interlocked.Increment(ref _navigationCount) == 1)
                {
                    FirstNavigationStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        Volatile.Write(ref _firstNavigationObservedCancellation, 1);
                        throw;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                CommittedTarget = target;
                CurrentMenuKey = target.ToString();
                CurrentMenuKeyChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _navigationGate.Release();
            }
        }
    }

    private sealed class TestAccountDialogWorkflow : IAccountDialogWorkflow
    {
        public Task<OperationResult<StorageAccountSummary>?> AddAccountAsync(
            StorageProviderCategory? preferredCategory = null,
            StorageProviderId? preferredProviderId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OperationResult<StorageAccountSummary>?>(null);

        public Task<OperationResult<StorageAccountSummary>?> EditAccountAsync(
            StorageAccountSummary account,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OperationResult<StorageAccountSummary>?>(
                OperationResult<StorageAccountSummary>.Success(account));

        public Task<OperationResult> DeleteAccountAsync(
            StorageAccountSummary account,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());
    }
}
