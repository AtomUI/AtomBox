using AtomBox.Core.Accounts;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Transfer.DependencyInjection;
using AtomBox.Transfer.Queue;
using AtomBox.Transfer.Scheduling;
using AtomBox.Transfer.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Transfer.Tests;

public sealed class TransferServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAtomBoxTransfer_Registers_TransferQueue()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();
        Assert.Single(services, s => s.ServiceType == typeof(TransferQueue));
    }

    [Fact]
    public void AddAtomBoxTransfer_Registers_TransferWorker()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();
        Assert.Single(services, s => s.ServiceType == typeof(TransferWorker));
    }

    [Fact]
    public void AddAtomBoxTransfer_Registers_FuncTransferWorkerFactory()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();
        var descriptor = Assert.Single(services, s => s.ServiceType == typeof(Func<TransferWorker>));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddAtomBoxTransfer_Registers_ITransferTaskScheduler()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();
        Assert.Single(services, s => s.ServiceType == typeof(ITransferTaskScheduler));
    }

    [Fact]
    public void AddAtomBoxTransfer_Registers_TransferRuntimeInitializer()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();
        Assert.Single(services, s => s.ServiceType == typeof(TransferRuntimeInitializer));
    }

    [Fact]
    public void FuncTransferWorker_CreatesNewInstanceEachTime()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStorageAccountRepository>(_ => new AnyAccountRepository());
        services.AddSingleton<IStorageProviderFactory>(_ => new FakeProviderFactory());
        services.AddSingleton<ILocalTransferFileStore>(_ => new MemoryLocalTransferFileStore());
        services.AddSingleton<ITransferStateStore>(_ => new MemoryTransferStore());
        services.AddAtomBoxTransfer();
        var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<Func<TransferWorker>>();
        var worker1 = factory();
        var worker2 = factory();

        Assert.NotNull(worker1);
        Assert.NotNull(worker2);
        Assert.NotSame(worker1, worker2);
    }

    [Fact]
    public void ServiceProvider_Resolves_ITransferTaskScheduler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStorageAccountRepository>(_ => new AnyAccountRepository());
        services.AddSingleton<IStorageProviderFactory>(_ => new FakeProviderFactory());
        services.AddSingleton<ILocalTransferFileStore>(_ => new MemoryLocalTransferFileStore());
        services.AddSingleton<ITransferStateStore>(_ => new MemoryTransferStore());
        services.AddSingleton<ITransferTaskStore>(_ => new MemoryTransferStore());
        services.AddSingleton<IApplicationSettingsRepository>(_ => new FixedApplicationSettingsRepository());
        services.AddAtomBoxTransfer();
        var provider = services.BuildServiceProvider();

        var scheduler = provider.GetRequiredService<ITransferTaskScheduler>();

        Assert.NotNull(scheduler);
        Assert.IsType<TransferTaskScheduler>(scheduler);
    }

    [Fact]
    public void AddAtomBoxTransfer_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TransferServiceCollectionExtensions.AddAtomBoxTransfer(null!));
    }

    [Fact]
    public void TransferQueue_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();
        var descriptor = services.Single(s => s.ServiceType == typeof(TransferQueue));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void TransferWorker_IsTransient()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();
        var descriptor = services.Single(s => s.ServiceType == typeof(TransferWorker));
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }

    [Fact]
    public void ITransferTaskScheduler_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();
        var descriptor = services.Single(s => s.ServiceType == typeof(ITransferTaskScheduler));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void TransferRuntimeInitializer_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();
        var descriptor = services.Single(s => s.ServiceType == typeof(TransferRuntimeInitializer));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    private sealed class FixedApplicationSettingsRepository : IApplicationSettingsRepository
    {
        private static readonly ApplicationSettings Settings = new(3, TransferOverwritePolicy.Ask, true);

        public Task<OperationResult<ApplicationSettings>> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<ApplicationSettings>.Success(Settings));
        }

        public Task<OperationResult> SaveAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success());
        }
    }
}
