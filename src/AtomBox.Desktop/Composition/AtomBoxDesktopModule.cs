using AtomBox.Application.DependencyInjection;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.DependencyInjection;
using AtomBox.Providers.DependencyInjection;
using AtomBox.Transfer.DependencyInjection;
using AtomBox.Transfer.Scheduling;
using AtomUI.City.Core.Modularity;
using AtomUI.City.Presentation;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Desktop.Composition;

[Module("AtomBox.Desktop", Description = "Composes the AtomBox desktop application.")]
[DependsOn(typeof(PresentationModule))]
public sealed class AtomBoxDesktopModule : ModuleBase
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Services.AddAtomBoxInfrastructure();
        context.Services.AddAtomBoxProviders();
        context.Services.AddAtomBoxTransfer();
        context.Services.AddAtomBoxApplication();
        context.Services.AddAtomBoxDesktop();
    }

    public override async ValueTask OnApplicationInitializationAsync(
        ApplicationInitializationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var infrastructureResult = context.Services
            .GetRequiredService<InfrastructureInitializer>()
            .Initialize();
        if (infrastructureResult.IsFailure)
        {
            throw new InvalidOperationException(
                infrastructureResult.Error?.Message ?? "Infrastructure initialization failed.");
        }

        var transferResult = await context.Services
            .GetRequiredService<TransferRuntimeInitializer>()
            .InitializeAsync(cancellationToken)
            .ConfigureAwait(false);
        if (transferResult.IsFailure)
        {
            throw new InvalidOperationException(
                transferResult.Error?.Message ?? "Transfer runtime initialization failed.");
        }
    }
}
