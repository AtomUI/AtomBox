using AtomBox.Application.Accounts;
using AtomBox.Application.Browsing;
using AtomBox.Application.DependencyInjection;
using AtomBox.Application.Settings;
using AtomBox.Application.Transfers;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Application.Tests;

public sealed class ApplicationServiceCollectionExtensionsContractTests
{
    [Fact]
    public void AddAtomBoxApplication_RegistersAllServices()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxApplication();

        var descriptors = services.ToList();
        Assert.Contains(descriptors, d => d.ServiceType == typeof(AccountAppService) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(descriptors, d => d.ServiceType == typeof(RemoteBrowserAppService) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(descriptors, d => d.ServiceType == typeof(TransferAppService) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(descriptors, d => d.ServiceType == typeof(SettingsAppService) && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddAtomBoxApplication_AllServicesAreSingleton()
    {
        var services = new ServiceCollection();
        services.AddAtomBoxApplication();

        var descriptors = services.ToList();
        foreach (var descriptor in descriptors)
        {
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }
    }

    [Fact]
    public void AddAtomBoxApplication_ThrowsOnNullServices()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddAtomBoxApplication());
    }
}
