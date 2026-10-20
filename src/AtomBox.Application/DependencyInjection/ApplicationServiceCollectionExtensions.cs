using AtomBox.Application.Accounts;
using AtomBox.Application.Browsing;
using AtomBox.Application.Settings;
using AtomBox.Application.State;
using AtomBox.Application.Transfers;
using AtomUI.City.State;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddAtomBoxApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddState();
        services.AddSingleton<AtomBoxApplicationState>();
        services.AddSingleton<IAtomBoxApplicationState>(
            provider => provider.GetRequiredService<AtomBoxApplicationState>());
        services.AddSingleton<ApplicationStateProjector>();
        services.AddSingleton<AtomBoxApplicationStateInitializer>();
        services.AddSingleton<AccountAppService>();
        services.AddSingleton<RemoteBrowserAppService>();
        services.AddSingleton<TransferAppService>();
        services.AddSingleton<SettingsAppService>();

        return services;
    }
}
