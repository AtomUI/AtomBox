namespace AtomBox.Infrastructure.Configuration;

public sealed class AtomBoxStoragePaths
{
    private readonly string _rootDirectory;

    public AtomBoxStoragePaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _rootDirectory = Path.Combine(appData, "AtomBox");
    }

    public AtomBoxStoragePaths(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Storage root directory cannot be empty.", nameof(rootDirectory));
        }

        _rootDirectory = rootDirectory;
    }

    public string RootDirectory => _rootDirectory;

    public string ConfigurationDirectory => Path.Combine(_rootDirectory, "config");

    public string StateDirectory => Path.Combine(_rootDirectory, "state");

    public string CredentialDirectory => Path.Combine(_rootDirectory, "credentials");

    public string CacheDirectory => Path.Combine(_rootDirectory, "cache");

    public string LogDirectory => Path.Combine(_rootDirectory, "logs");

    public string AccountsFile => Path.Combine(ConfigurationDirectory, "accounts.json");

    public string SettingsFile => Path.Combine(ConfigurationDirectory, "settings.json");

    public string SchemaVersionFile => Path.Combine(ConfigurationDirectory, "schema-version.json");

    public string TransferTasksFile => Path.Combine(StateDirectory, "transfer-tasks.json");

    public string TransferProgressFile => Path.Combine(StateDirectory, "transfer-progress.json");

    public string FileFingerprintIndexFile => Path.Combine(StateDirectory, "fingerprints", "file-fingerprint-index.json");

    public string CredentialIndexFile => Path.Combine(CredentialDirectory, "credential-index.json");

    public string CredentialKeyFile => Path.Combine(CredentialDirectory, "credential-key.bin");
}
