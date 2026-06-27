using AtomBox.Core.Results;
using AtomBox.Infrastructure.Logging;

namespace AtomBox.Infrastructure.Configuration;

public sealed class InfrastructureInitializer
{
    private readonly AppConfigurationStore _configurationStore;
    private readonly ConfigurationMigration _configurationMigration;
    private readonly LoggingInitializer _loggingInitializer;

    public InfrastructureInitializer(
        AppConfigurationStore configurationStore,
        ConfigurationMigration configurationMigration,
        LoggingInitializer loggingInitializer)
    {
        _configurationStore = configurationStore;
        _configurationMigration = configurationMigration;
        _loggingInitializer = loggingInitializer;
    }

    public OperationResult Initialize()
    {
        var configurationResult = _configurationStore.EnsureCreated();
        if (configurationResult.IsFailure)
        {
            return configurationResult;
        }

        var migrationResult = _configurationMigration.EnsureCurrentSchema();
        if (migrationResult.IsFailure)
        {
            return migrationResult;
        }

        return _loggingInitializer.Initialize();
    }
}
