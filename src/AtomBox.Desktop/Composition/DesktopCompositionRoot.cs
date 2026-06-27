using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.ViewModels.Pages;
using AtomBox.Application.DependencyInjection;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.DependencyInjection;
using AtomBox.Providers.DependencyInjection;
using AtomBox.Desktop.Shell;
using AtomBox.Transfer.DependencyInjection;
using AtomBox.Transfer.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Desktop.Composition;

public sealed class DesktopCompositionRoot : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    private DesktopCompositionRoot(ServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public static DesktopCompositionRoot Create()
    {
        var services = new ServiceCollection();

        services.AddAtomBoxInfrastructure();
        services.AddAtomBoxProviders();
        services.AddAtomBoxTransfer();
        services.AddAtomBoxApplication();
        services.AddAtomBoxDesktop();
        AddDesktopCompositionServices(services);

        var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        var initializeResult = serviceProvider.GetRequiredService<InfrastructureInitializer>().Initialize();
        if (initializeResult.IsFailure)
        {
            throw new InvalidOperationException(initializeResult.Error?.Message ?? "Infrastructure initialization failed.");
        }

        var transferInitializeResult = serviceProvider
            .GetRequiredService<TransferRuntimeInitializer>()
            .InitializeAsync()
            .GetAwaiter()
            .GetResult();
        if (transferInitializeResult.IsFailure)
        {
            throw new InvalidOperationException(
                transferInitializeResult.Error?.Message ?? "Transfer runtime initialization failed.");
        }

        return new DesktopCompositionRoot(serviceProvider);
    }

    public MainWindow CreateMainWindow()
    {
        return _serviceProvider.GetRequiredService<MainWindow>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    private static IServiceCollection AddDesktopCompositionServices(IServiceCollection services)
    {
        services.AddSingleton<IPageViewModelFactory>(provider =>
            new PageViewModelFactory(
                provider.GetRequiredService<HomeViewModel>,
                provider.GetRequiredService<RemoteBrowserViewModel>,
                provider.GetRequiredService<TransferQueueViewModel>,
                provider.GetRequiredService<TransferHistoryViewModel>,
                provider.GetRequiredService<SettingsViewModel>,
                provider.GetRequiredService<AccountManagementViewModel>));

        services.AddSingleton<INavigationService, NavigationService>();

        return services;
    }
}
