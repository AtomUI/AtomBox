using System.Text.Json;
using System.Globalization;
using AtomBox.Core.Errors;
using AtomBox.Core.Results;

namespace AtomBox.Infrastructure.Storage;

internal sealed class JsonFileStore<T>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileStore(string path)
    {
        _path = path;
    }

    public async Task<OperationResult<T>> ReadAsync(T defaultValue, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return OperationResult<T>.Success(defaultValue);
            }

            await using var stream = File.OpenRead(_path);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            return OperationResult<T>.Success(value ?? defaultValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            TryBackupCorruptFile();
            return OperationResult<T>.Failure(new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                $"Unable to read local store '{Path.GetFileName(_path)}'.",
                StorageErrorCategory.Infrastructure));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationResult> WriteAsync(T value, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = $"{_path}.tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (File.Exists(_path))
            {
                var backupPath = $"{_path}.bak";
                File.Copy(_path, backupPath, overwrite: true);
            }

            File.Move(tempPath, _path, overwrite: true);
            return OperationResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return OperationResult.Failure(new StorageError(
                StorageErrorCode.InfrastructureUnavailable,
                $"Unable to write local store '{Path.GetFileName(_path)}'.",
                StorageErrorCategory.Infrastructure));
        }
        finally
        {
            _gate.Release();
        }
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Copy(_path, $"{_path}.{timestamp}.corrupt", overwrite: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
