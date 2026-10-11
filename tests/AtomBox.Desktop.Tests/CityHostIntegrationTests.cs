using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Navigation;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Transfer.Scheduling;
using AtomUI.City.Presentation;
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
            Assert.NotNull(host.Services.GetRequiredService<INavigationService>());
            Assert.NotNull(host.Services.GetRequiredService<ILocalizationService>());
            Assert.NotNull(host.Services.GetRequiredService<IPresentationRuntime>());
            Assert.NotNull(host.Services.GetRequiredService<TransferRuntimeInitializer>());
            Assert.Null(host.ApplicationScope);

            await host.StartAsync();

            Assert.NotNull(host.ApplicationScope);
            Assert.True(Directory.Exists(paths.ConfigurationDirectory));

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
}
