using System.Globalization;
using AtomBox.Desktop.Composition;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Services;
using AtomBox.Infrastructure.Configuration;
using AtomUI;
using AtomUI.City.Core.Hosting;
using AtomUI.City.Core.Modularity;
using AtomUI.City.Localization;
using AtomUI.City.Presentation;
using AtomUI.City.Routing;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithAtomUIDefaultOptions()
            .LogToTrace();
    }

    internal static IApplicationHost CreateHost(
        string[] args,
        AtomBoxStoragePaths? storagePaths = null)
    {
        var paths = storagePaths ?? new AtomBoxStoragePaths();
        var preference = DesktopLanguageBootstrapper.ReadPreference(paths);
        var culture = DesktopLanguageBootstrapper.Apply(
            preference,
            CultureInfo.InstalledUICulture);
        IDesktopPreferencesService preferences = new DesktopPreferencesService(paths);

        var builder = ApplicationHost.CreateBuilder(args);
        builder.ConfigureHost(options =>
        {
            options.ApplicationId = "AtomBox";
            options.ApplicationName = "AtomBox";
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(paths);
            services.AddSingleton(preferences);
            services.AddLocalization(options =>
            {
                options.DefaultCulture = culture;
                options.DefaultUICulture = culture;
            });
            services.AddSingleton<IPresentationLocalizationBridge, AvaloniaPresentationLocalizationBridge>();
            services.AddSingleton<AtomBoxLocalizedTextProjection>();
            services.AddSingleton<IAtomBoxLocalizedText>(serviceProvider =>
                serviceProvider.GetRequiredService<AtomBoxLocalizedTextProjection>());
            services.AddSingleton<IDesktopLanguageService>(serviceProvider =>
                new DesktopLanguageService(
                    serviceProvider.GetRequiredService<AtomUI.City.Localization.ILocalizationService>(),
                    preferences,
                    preference));
        });
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });
        builder
            .UseModule<PresentationModule>()
            .UseModule<RoutingModule>()
            .UseModule<AtomBoxDesktopModule>();

        return builder.Build();
    }

    private static int Run(string[] args)
    {
        using var host = CreateHost(args);
        Exception? startupFailure = null;
        try
        {
            host.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            startupFailure = exception;
        }

        var attached = false;
        try
        {
            DesktopBootstrap.Attach(host, startupFailure);
            attached = true;
            return BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            if (attached)
            {
                DesktopBootstrap.Detach(host);
            }

            Task.Run(async () => await host.StopAsync().ConfigureAwait(false))
                .GetAwaiter()
                .GetResult();
        }
    }
}
