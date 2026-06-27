using AtomBox.Application.Accounts;
using AtomBox.Application.Browsing;
using AtomBox.Application.Settings;
using AtomBox.Application.Transfers;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddAtomBoxApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<AccountAppService>();
        services.AddSingleton<RemoteBrowserAppService>();
        services.AddSingleton<TransferAppService>();
        services.AddSingleton<SettingsAppService>();

        return services;
    }
}
