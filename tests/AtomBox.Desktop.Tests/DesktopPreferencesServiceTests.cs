using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Services;
using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Desktop.Tests;

public sealed class DesktopPreferencesServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AtomBox.Desktop.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetAsync_OldJsonWithoutLanguage_DefaultsToSystem()
    {
        var paths = new AtomBoxStoragePaths(_root);
        Directory.CreateDirectory(paths.ConfigurationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(paths.ConfigurationDirectory, "desktop-preferences.json"),
            """
            {
              "startupPage": 3,
              "closeWindowBehavior": 0,
              "defaultDownloadDirectory": "D:\\Downloads"
            }
            """);

        var result = await new DesktopPreferencesService(paths).GetAsync();

        Assert.Equal(LanguagePreference.System, result.Language);
    }

    [Fact]
    public async Task SaveAsync_PersistsStableLanguageTag()
    {
        var paths = new AtomBoxStoragePaths(_root);
        var service = new DesktopPreferencesService(paths);
        var preferences = DesktopPreferences.CreateDefault() with
        {
            Language = LanguagePreference.English
        };

        await service.SaveAsync(preferences);
        var result = await service.GetAsync();

        Assert.Equal(LanguagePreference.English, result.Language);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
