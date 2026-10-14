using AtomBox.Core.Accounts;
using AtomBox.Core.Providers;
using AtomBox.Core.Transfers;
using AtomBox.Transfer.Queue;
using AtomBox.Transfer.Scheduling;
using AtomBox.Transfer.State;
using AtomBox.Transfer.Workers;
using AtomUI.City.State;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Transfer.DependencyInjection;

public static class TransferServiceCollectionExtensions
{
    public static IServiceCollection AddAtomBoxTransfer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddState();
        services.AddSingleton<AtomBoxTransferState>();
        services.AddSingleton<IAtomBoxTransferState>(provider =>
            provider.GetRequiredService<AtomBoxTransferState>());
        services.AddSingleton<TransferStateProjector>();
        services.AddSingleton<ITransferStateSynchronizer>(provider =>
            provider.GetRequiredService<TransferStateProjector>());
        services.AddSingleton<TransferQueue>();
        services.AddSingleton<TransferCancellationRegistry>();
        services.AddSingleton<ITransferCancellationState>(provider =>
            provider.GetRequiredService<TransferCancellationRegistry>());
        services.AddTransient<TransferWorker>(provider =>
        {
            var accounts = provider.GetRequiredService<IStorageAccountRepository>();
            var providerFactory = provider.GetRequiredService<IStorageProviderFactory>();
            var localFiles = provider.GetRequiredService<ILocalTransferFileStore>();
            var stateStore = new ProjectingTransferStateStore(
                provider.GetRequiredService<ITransferStateStore>(),
                provider.GetRequiredService<TransferStateProjector>());
            var cancellationState = provider.GetRequiredService<ITransferCancellationState>();

            return new TransferWorker(accounts, providerFactory, localFiles, stateStore, cancellationState);
        });
        services.AddSingleton<Func<TransferWorker>>(provider =>
            () => provider.GetRequiredService<TransferWorker>());
        services.AddSingleton<ITransferTaskScheduler, TransferTaskScheduler>();
        services.AddSingleton<TransferRuntimeInitializer>();

        return services;
    }
}
