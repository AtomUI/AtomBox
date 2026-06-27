using System.Text.Json;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;

namespace AtomBox.Infrastructure.Configuration;

public sealed class ConfigurationMigration
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AtomBoxStoragePaths _paths;

    public ConfigurationMigration(AtomBoxStoragePaths paths)
    {
        _paths = paths;
    }

    public OperationResult EnsureCurrentSchema()
    {
        try
        {
            if (File.Exists(_paths.SchemaVersionFile))
            {
                return OperationResult.Success();
            }

            var document = new SchemaVersionDocument(CurrentSchemaVersion, DateTimeOffset.UtcNow);
            WriteAtomic(_paths.SchemaVersionFile, JsonSerializer.Serialize(document, SerializerOptions));
            return OperationResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return OperationResult.Failure(new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                "Unable to prepare AtomBox configuration schema.",
                StorageErrorCategory.Infrastructure));
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var tempPath = $"{path}.tmp";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, path, overwrite: true);
    }

    private sealed record SchemaVersionDocument(int Version, DateTimeOffset CreatedAt);
}
