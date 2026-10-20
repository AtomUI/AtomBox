using System.Globalization;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.ViewModels.Pages;
using AtomBox.Desktop.Views.Accounts;
using AtomBox.Desktop.Views.Home;
using AtomBox.Desktop.Views.RemoteBrowser;
using AtomBox.Desktop.Views.Settings;
using AtomBox.Desktop.Views.Transfers;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Transfer.Scheduling;
using AtomBox.Transfer.State;
using AtomUI.City.Core.Diagnostics;
using AtomUI.City.Presentation;
using AtomUI.City.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Desktop.Tests;

public sealed class CityHostIntegrationTests
{
    [Fact]
    public async Task TransferRuntimeStartupFailureFailsHostAndLeavesNoOpenAdmission()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AtomBox.Desktop.Tests",
            Guid.NewGuid().ToString("N"));
        var paths = new AtomBoxStoragePaths(root);
        Directory.CreateDirectory(paths.StateDirectory);
        await File.WriteAllTextAsync(paths.TransferTasksFile, "{ invalid json");

        try
        {
            await using var host = Program.CreateHost([], paths);

            await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());

            var runtime = host.Services.GetRequiredService<TransferRuntimeController>();
            Assert.False(runtime.Current.IsAcceptingSubmissions);
            Assert.NotEqual(TransferRuntimeLifecycleState.Running, runtime.Current.Lifecycle);
            Assert.Contains(
                host.Services.GetRequiredService<IHostDiagnostics>().Records,
                record => record.Code == HostDiagnosticIds.HostStartFailed);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CityLocalizationProjectsAndSwitchesOneCompleteSnapshot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AtomBox.Desktop.Tests",
            Guid.NewGuid().ToString("N"));
        var paths = new AtomBoxStoragePaths(root);
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUICulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            await using var host = Program.CreateHost([], paths);
            await host.StartAsync();

            var projection = host.Services.GetRequiredService<IAtomBoxLocalizedText>();
            var language = host.Services.GetRequiredService<IDesktopLanguageService>();
            await language.SetLanguageAsync(LanguagePreference.English);

            Assert.Equal("Cancel", projection.GetString("Common", "Cancel"));
            Assert.Equal("Home", projection.GetString("Shell", "Home"));
            var englishRevision = projection.Revision;
            (string Cancel, string Home, long Revision)? publishedSnapshot = null;
            projection.LanguageChanged += (_, _) =>
            {
                publishedSnapshot = (
                    projection.GetString("Common", "Cancel"),
                    projection.GetString("Shell", "Home"),
                    projection.Revision);
            };

            await language.SetLanguageAsync(LanguagePreference.SimplifiedChinese);

            Assert.Equal("zh-CN", projection.CurrentCulture.Name);
            Assert.Equal("取消", projection.GetString("Common", "Cancel"));
            Assert.Equal("首页", projection.GetString("Shell", "Home"));
            Assert.True(projection.Revision > englishRevision);
            Assert.True(publishedSnapshot.HasValue);
            Assert.Equal(
                ("取消", "首页", projection.Revision),
                publishedSnapshot.Value);
            Assert.Equal(LanguagePreference.SimplifiedChinese, language.CurrentPreference);
            Assert.Equal(
                LanguagePreference.SimplifiedChinese,
                (await host.Services.GetRequiredService<IDesktopPreferencesService>().GetAsync()).Language);

            await host.StopAsync();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUICulture;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

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
            Assert.NotNull(host.Services.GetRequiredService<AtomUI.City.Localization.ILocalizationService>());
            Assert.NotNull(host.Services.GetRequiredService<IAtomBoxLocalizedText>());
            Assert.NotNull(host.Services.GetRequiredService<IDesktopLanguageService>());
            Assert.NotNull(host.Services.GetRequiredService<IPresentationRuntime>());
            var runtime = host.Services.GetRequiredService<TransferRuntimeController>();
            Assert.Equal(TransferRuntimeLifecycleState.Created, runtime.Current.Lifecycle);
            Assert.Null(host.ApplicationScope);

            await host.StartAsync();

            Assert.NotNull(host.ApplicationScope);
            Assert.Equal(TransferRuntimeLifecycleState.Running, runtime.Current.Lifecycle);
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
            Assert.Equal(TransferRuntimeLifecycleState.Stopped, runtime.Current.Lifecycle);
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
