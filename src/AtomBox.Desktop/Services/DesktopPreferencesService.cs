using System.Text.Json;
using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Desktop.Services;

public sealed class DesktopPreferencesService : IDesktopPreferencesService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _preferencesFile;

    public DesktopPreferencesService(AtomBoxStoragePaths paths)
    {
        _preferencesFile = Path.Combine(paths.ConfigurationDirectory, "desktop-preferences.json");
    }

    public async Task<DesktopPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_preferencesFile))
        {
            return DesktopPreferences.CreateDefault();
        }

        try
        {
            await using var stream = File.OpenRead(_preferencesFile);
            var preferences = await JsonSerializer.DeserializeAsync<DesktopPreferences>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            return Normalize(preferences);
        }
        catch
        {
            return DesktopPreferences.CreateDefault();
        }
    }

    public async Task SaveAsync(DesktopPreferences preferences, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(preferences);
        Directory.CreateDirectory(Path.GetDirectoryName(_preferencesFile)!);

        var temporaryFile = $"{_preferencesFile}.tmp";
        await using (var stream = File.Create(temporaryFile))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                normalized,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryFile, _preferencesFile, true);
    }

    public async Task<DesktopPreferences> ResetAsync(CancellationToken cancellationToken = default)
    {
        var defaults = DesktopPreferences.CreateDefault();
        await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
        return defaults;
    }

    private static DesktopPreferences Normalize(DesktopPreferences? preferences)
    {
        var defaults = DesktopPreferences.CreateDefault();
        if (preferences is null)
        {
            return defaults;
        }

        var startupPage = Enum.IsDefined(preferences.StartupPage)
            ? preferences.StartupPage
            : defaults.StartupPage;
        var closeWindowBehavior = Enum.IsDefined(preferences.CloseWindowBehavior)
            ? preferences.CloseWindowBehavior
            : defaults.CloseWindowBehavior;
        var defaultDownloadDirectory = string.IsNullOrWhiteSpace(preferences.DefaultDownloadDirectory)
            ? defaults.DefaultDownloadDirectory
            : preferences.DefaultDownloadDirectory;

        return new DesktopPreferences(startupPage, closeWindowBehavior, defaultDownloadDirectory);
    }
}
