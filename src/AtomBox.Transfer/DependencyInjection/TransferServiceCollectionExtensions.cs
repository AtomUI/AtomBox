using AtomBox.Core.Accounts;
using AtomBox.Core.Providers;
using AtomBox.Core.Transfers;
using AtomBox.Transfer.Queue;
using AtomBox.Transfer.Scheduling;
using AtomBox.Transfer.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Transfer.DependencyInjection;

public static class TransferServiceCollectionExtensions
{
    public static IServiceCollection AddAtomBoxTransfer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TransferQueue>();
        services.AddSingleton<TransferCancellationRegistry>();
        services.AddSingleton<ITransferCancellationState>(provider =>
            provider.GetRequiredService<TransferCancellationRegistry>());
        services.AddTransient<TransferWorker>();
        services.AddSingleton<Func<TransferWorker>>(provider =>
        {
            var accounts = provider.GetRequiredService<IStorageAccountRepository>();
            var providerFactory = provider.GetRequiredService<IStorageProviderFactory>();
            var localFiles = provider.GetRequiredService<ILocalTransferFileStore>();
            var stateStore = provider.GetRequiredService<ITransferStateStore>();
            var cancellationState = provider.GetRequiredService<ITransferCancellationState>();

            return () => new TransferWorker(accounts, providerFactory, localFiles, stateStore, cancellationState);
        });
        services.AddSingleton<ITransferTaskScheduler, TransferTaskScheduler>();
        services.AddSingleton<TransferRuntimeInitializer>();

        return services;
    }
}
