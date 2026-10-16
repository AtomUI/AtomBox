using AtomBox.Transfer.DependencyInjection;
using AtomBox.Transfer.Scheduling;
using AtomUI.City.Core.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Transfer;

[Module("AtomBox.Transfer", Description = "Owns the application-lifetime transfer runtime.")]
public sealed class AtomBoxTransferModule : ModuleBase
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.AddAtomBoxTransfer();
    }

    public override async ValueTask OnPostApplicationInitializationAsync(
        ApplicationInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.Services
            .GetRequiredService<TransferRuntimeController>()
            .StartAsync(context.ApplicationScope.CancellationToken, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async ValueTask OnApplicationShutdownAsync(
        ApplicationShutdownContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.Services
            .GetRequiredService<TransferRuntimeController>()
            .StopAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
