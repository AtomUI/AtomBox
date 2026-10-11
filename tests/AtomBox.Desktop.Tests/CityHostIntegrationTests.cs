using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.ViewModels.Pages;
using AtomBox.Desktop.Views.Accounts;
using AtomBox.Desktop.Views.Home;
using AtomBox.Desktop.Views.RemoteBrowser;
using AtomBox.Desktop.Views.Settings;
using AtomBox.Desktop.Views.Transfers;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Transfer.Scheduling;
using AtomUI.City.Presentation;
using AtomUI.City.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Desktop.Tests;

public sealed class CityHostIntegrationTests
{
    [Fact]
    public async Task HostStartsAndStopsWithDesktopModuleServices()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AtomBox.Desktop.Tests",
            Guid.NewGuid().ToString("N"));
        var paths = new AtomBoxStoragePaths(root);

        try
        {
            await using var host = Program.CreateHost([], paths);

            Assert.Same(paths, host.Services.GetRequiredService<AtomBoxStoragePaths>());
            Assert.IsType<NavigationService>(host.Services.GetRequiredService<INavigationService>());
            Assert.NotNull(host.Services.GetRequiredService<ILocalizationService>());
            Assert.NotNull(host.Services.GetRequiredService<IPresentationRuntime>());
            Assert.NotNull(host.Services.GetRequiredService<TransferRuntimeInitializer>());
            Assert.Null(host.ApplicationScope);

            await host.StartAsync();

            Assert.NotNull(host.ApplicationScope);
            Assert.True(Directory.Exists(paths.ConfigurationDirectory));

            var viewLocator = host.Services.GetRequiredService<IViewLocator>();
            AssertView<HomeViewModel, HomeView>(viewLocator);
            AssertView<RemoteBrowserViewModel, RemoteBrowserView>(viewLocator);
            AssertView<TransferQueueViewModel, TransferQueueView>(viewLocator);
            AssertView<TransferHistoryViewModel, TransferHistoryView>(viewLocator);
            AssertView<SettingsViewModel, SettingsView>(viewLocator);
            AssertView<AccountManagementViewModel, AccountManagementView>(viewLocator);

            var routeGraph = host.Services.GetRequiredService<IRouteGraphProvider>().CurrentSnapshot;
            Assert.Equal(6, routeGraph.Routes.Count);
            AssertRoute<HomeViewModel>(routeGraph, "AtomBox.Home", "home");
            AssertRoute<RemoteBrowserViewModel>(routeGraph, "AtomBox.RemoteBrowser", "remote");
            AssertRoute<TransferQueueViewModel>(routeGraph, "AtomBox.TransferQueue", "transfers/queue");
            AssertRoute<TransferHistoryViewModel>(routeGraph, "AtomBox.TransferHistory", "transfers/history");
            AssertRoute<SettingsViewModel>(routeGraph, "AtomBox.Settings", "settings");
            AssertRoute<AccountManagementViewModel>(routeGraph, "AtomBox.AccountManagement", "accounts");

            await host.StopAsync();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void AssertView<TViewModel, TView>(IViewLocator viewLocator)
    {
        var descriptor = viewLocator.Locate(typeof(TViewModel));

        Assert.Equal(typeof(TView), descriptor.ViewType);
        Assert.Null(descriptor.PluginId);
        Assert.Null(descriptor.ContributionId);
        Assert.True(typeof(IViewDataContextAware).IsAssignableFrom(descriptor.ViewType));
    }

    private static void AssertRoute<TViewModel>(
        RouteGraphSnapshot routeGraph,
        string routeId,
        string template)
    {
        var route = routeGraph.GetRequiredRoute(routeId);

        Assert.Equal(RouteDefinitionKind.Route, route.Kind);
        Assert.Equal(template, route.Template?.Pattern);
        Assert.Equal("primary", route.OutletName);
        Assert.Equal(typeof(TViewModel), route.ViewModelTarget?.ViewModelType);
    }
}
