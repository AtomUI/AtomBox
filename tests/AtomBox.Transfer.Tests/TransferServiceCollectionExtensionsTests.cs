using AtomBox.Core.Accounts;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Transfer.DependencyInjection;
using AtomBox.Transfer.Scheduling;
using AtomBox.Transfer.State;
using AtomBox.Transfer.Workers;
using AtomUI.City.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Transfer.Tests;

public sealed class TransferServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAtomBoxTransfer_Registers_ITransferTaskScheduler()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();
        Assert.Single(services, s => s.ServiceType == typeof(ITransferTaskScheduler));
    }

    [Fact]
    public void AddAtomBoxTransfer_Registers_TransferRuntimeController()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();
        Assert.Single(services, s => s.ServiceType == typeof(TransferRuntimeController));
        Assert.Single(services, s => s.ServiceType == typeof(ITransferRuntimeDiagnostics));
    }

    [Fact]
    public void AddAtomBoxTransfer_Registers_ReadOnlyTransferStateAndSynchronizer()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();

        Assert.Single(services, service => service.ServiceType == typeof(IAtomBoxTransferState));
        Assert.Single(services, service => service.ServiceType == typeof(ITransferStateSynchronizer));
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
        Assert.IsType<TransferRuntimeController>(scheduler);
    }

    [Fact]
    public void AddAtomBoxTransfer_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TransferServiceCollectionExtensions.AddAtomBoxTransfer(null!));
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
    public void TransferRuntimeController_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxTransfer();
        var descriptor = services.Single(s => s.ServiceType == typeof(TransferRuntimeController));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void RuntimeDiagnosticsPreservesExceptionChainInCityHostDiagnostics()
    {
        var services = new ServiceCollection();
        using var hostDiagnostics = new InMemoryHostDiagnostics();
        services.AddSingleton<IHostDiagnostics>(hostDiagnostics);
        services.AddAtomBoxTransfer();
        using var provider = services.BuildServiceProvider();
        var exception = new InvalidOperationException(
            "outer failure",
            new IOException("inner failure"));

        provider.GetRequiredService<ITransferRuntimeDiagnostics>().Write(
            new TransferRuntimeDiagnostic(
                "TRANSFER_TEST_FAILURE",
                "Transfer test failed.",
                TransferRuntimeDiagnosticSeverity.Error,
                exception));

        var record = Assert.Single(hostDiagnostics.Records);
        Assert.Equal(HostDiagnosticSeverity.Error, record.Severity);
        Assert.Contains("outer failure", record.Message);
        Assert.Contains("inner failure", record.Message);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.Context["exceptionType"]);
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
