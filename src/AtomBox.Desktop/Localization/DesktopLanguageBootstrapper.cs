using System.Globalization;
using System.Text.Json;
using AtomBox.Infrastructure.Configuration;

namespace AtomBox.Desktop.Localization;

public static class DesktopLanguageBootstrapper
{
    public static string ReadPreference(AtomBoxStoragePaths paths)
    {
        var file = Path.Combine(paths.ConfigurationDirectory, "desktop-preferences.json");
        if (!File.Exists(file))
        {
            return LanguagePreference.System;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            return document.RootElement.TryGetProperty("language", out var language)
                ? LanguagePreference.Normalize(language.GetString())
                : LanguagePreference.System;
        }
        catch
        {
            return LanguagePreference.System;
        }
    }

    public static CultureInfo Apply(string preference, CultureInfo systemCulture)
    {
        var culture = LanguagePreference.Resolve(preference, systemCulture);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        return culture;
    }
}
