using AtomBox.Core.Accounts;
using AtomBox.Core.Credentials;
using AtomBox.Core.Fingerprints;
using AtomBox.Core.Settings;
using AtomBox.Core.Transfers;
using AtomBox.Infrastructure.Caching;
using AtomBox.Infrastructure.Configuration;
using AtomBox.Infrastructure.Credentials;
using AtomBox.Infrastructure.Logging;
using AtomBox.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAtomBoxInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<AtomBoxStoragePaths>();
        services.AddSingleton<AppConfigurationStore>();
        services.AddSingleton<ConfigurationMigration>();
        services.AddSingleton<InfrastructureInitializer>();

        services.AddSingleton<LoggingInitializer>();
        services.AddSingleton<LogOptions>();

        services.AddSingleton<CredentialProtectionService>();
        services.AddSingleton<ICredentialStore, CredentialStore>();

        services.AddSingleton<IStorageAccountRepository, StorageAccountRepository>();
        services.AddSingleton<IApplicationSettingsRepository, ApplicationSettingsRepository>();
        services.AddSingleton<ITransferTaskStore, TransferTaskStore>();
        services.AddSingleton<TransferStateStore>();
        services.AddSingleton<IFileFingerprintIndexStore, JsonFileFingerprintIndexStore>();
        services.AddSingleton<ITransferStateStore, FingerprintAwareTransferStateStoreDecorator>();
        services.AddSingleton<ILocalTransferFileStore, LocalTransferFileStore>();

        services.AddSingleton<MetadataCache>();
        services.AddSingleton<ThumbnailCache>();

        return services;
    }
}
