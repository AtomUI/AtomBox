using AtomBox.Core.Accounts;
using AtomBox.Core.Providers;
using AtomBox.Core.Transfers;
using AtomBox.Transfer.Scheduling;
using AtomBox.Transfer.State;
using AtomBox.Transfer.Workers;
using AtomUI.City.Core.Diagnostics;
using AtomUI.City.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddTransient<TransferOperationExecutor>();
        services.AddSingleton<Func<TransferOperationExecutor>>(provider =>
            () => provider.GetRequiredService<TransferOperationExecutor>());
        services.AddSingleton<TransferRuntimeOptions>();
        services.TryAddSingleton<ITransferRuntimeDiagnostics>(provider =>
            new CityTransferRuntimeDiagnostics(provider.GetService<IHostDiagnostics>()));
        services.AddSingleton<TransferRuntimeController>();
        services.AddSingleton<ITransferTaskScheduler>(provider =>
            provider.GetRequiredService<TransferRuntimeController>());

        return services;
    }
}
