using System.ComponentModel;
using System.Globalization;
using AtomBox.Application.Accounts;
using AtomBox.Application.Transfers;
using AtomBox.Core.Accounts;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.Shell;
using AtomBox.Desktop.ViewModels.Pages;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Providers.Common;
using AtomBox.Transfer.Scheduling;
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
    public void CityDependencyStopsAtDesktopBoundary()
    {
        var desktopReferences = typeof(MainWindowViewModel).Assembly.GetReferencedAssemblies();
        Assert.Contains(desktopReferences, reference => reference.Name == "AtomUI.City.Mvvm");

        var businessAssemblies = new[]
        {
            typeof(AccountAppService).Assembly,
            typeof(StorageAccountSummary).Assembly,
            typeof(AtomBoxStoragePaths).Assembly,
            typeof(StorageProviderRegistry).Assembly,
            typeof(TransferRuntimeInitializer).Assembly,
        };

        Assert.All(
            businessAssemblies,
            assembly => Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                reference => reference.Name?.StartsWith("AtomUI.City", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task HomeCommandAndLocalizationSubscriptionFollowActivation()
    {
        await using var context = await DesktopTestContext.CreateAsync();
        var localization = new TestLocalizedText();
        var dispatcher = new ImmediateUiDispatcher();
        var viewModel = ActivatorUtilities.CreateInstance<HomeViewModel>(
            context.Services,
            localization,
            dispatcher);
        var subtitleChanges = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HomeViewModel.Subtitle))
            {
                subtitleChanges++;
            }
        };

        Assert.False(viewModel.OpenRemoteStorageCommand.CanExecute(null));
        var scope = new ActivationScope();
        await viewModel.ActivateAsync(scope);
        Assert.True(viewModel.OpenRemoteStorageCommand.CanExecute(null));

        localization.Publish();
        Assert.Equal(1, subtitleChanges);

        await viewModel.DeactivateAsync();
        Assert.False(viewModel.OpenRemoteStorageCommand.CanExecute(null));
        localization.Publish();
        Assert.Equal(1, subtitleChanges);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task TransferQueueOwnsOnePeriodicWorkerPerActivation()
    {
        await using var context = await DesktopTestContext.CreateAsync();
        var viewModel = ActivatorUtilities.CreateInstance<TransferQueueViewModel>(
            context.Services,
            new TestLocalizedText(),
            new ImmediateUiDispatcher());

        var firstScope = new ActivationScope();
        await viewModel.ActivateAsync(firstScope);
        Assert.Equal(1, firstScope.ActiveTaskCount);

        await viewModel.DeactivateAsync();
        Assert.True(firstScope.IsDisposed);
        Assert.Equal(0, firstScope.ActiveTaskCount);

        var secondScope = new ActivationScope();
        await viewModel.ActivateAsync(secondScope);
        Assert.Equal(1, secondScope.ActiveTaskCount);

        await viewModel.DisposeAsync();
        Assert.True(secondScope.IsDisposed);
    }

    [Fact]
    public async Task ShellCompositionOwnsStatusBarChildAndCoalescesAccountSignals()
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
        Assert.Equal(1, root.ActivationScope.ActiveTaskCount);
        Assert.Equal(1, child.ActivationScope.ActiveTaskCount);

        for (var index = 0; index < 20; index++)
        {
            accounts.PublishAccountsChanged();
        }

        await WaitUntilAsync(() => Volatile.Read(ref refreshCount) >= 2);
        Assert.InRange(Volatile.Read(ref refreshCount), 2, 4);

        await owner.DisposeAsync();
        Assert.True(root.IsDisposed);
        Assert.True(child.IsDisposed);
        Assert.Equal(ActivationState.Deactivated, shell.ActivationState);
        Assert.Equal(ActivationState.Deactivated, statusBar.ActivationState);

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
        var statusBar = ActivatorUtilities.CreateInstance<StatusBarViewModel>(
            context.Services,
            new TestLocalizedText(),
            dispatcher);
        var viewModel = ActivatorUtilities.CreateInstance<RemoteBrowserViewModel>(
            context.Services,
            workflow,
            navigation,
            statusBar,
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
        await statusBar.DisposeAsync();
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
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
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

    private sealed class TestAccountDialogWorkflow : IAccountDialogWorkflow
    {
        public event EventHandler? AccountsChanged;

        public Task<OperationResult<StorageAccountSummary?>> AddAccountAsync(
            StorageProviderCategory? preferredCategory = null,
            StorageProviderId? preferredProviderId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<StorageAccountSummary?>.Success(null));

        public Task<OperationResult<StorageAccountSummary?>> EditAccountAsync(
            StorageAccountSummary account,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<StorageAccountSummary?>.Success(account));

        public Task<OperationResult> DeleteAccountAsync(
            StorageAccountSummary account,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Success());

        public void PublishAccountsChanged()
        {
            AccountsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
