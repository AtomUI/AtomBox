using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using AtomBox.Desktop.Services;
using AtomUI.Controls;
using AtomUI.Theme.Language;
using Avalonia;
using Avalonia.Threading;

namespace AtomBox.Desktop.Localization;

public sealed class LocalizationService : ILocalizationService
{
    private static readonly IReadOnlyDictionary<string, ResourceManager> Catalogs =
        new Dictionary<string, ResourceManager>(StringComparer.OrdinalIgnoreCase)
        {
            ["Common"] = CreateManager("CommonStrings"),
            ["Shell"] = CreateManager("ShellStrings"),
            ["Account"] = CreateManager("AccountStrings"),
            ["RemoteBrowser"] = CreateManager("RemoteBrowserStrings"),
            ["Transfer"] = CreateManager("TransferStrings"),
            ["Settings"] = CreateManager("SettingsStrings")
        };

    private readonly IDesktopPreferencesService _preferences;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private CultureInfo _currentCulture;
    private string _currentPreference;
    private long _revision;

    public LocalizationService(
        IDesktopPreferencesService preferences,
        string preference,
        CultureInfo culture)
    {
        _preferences = preferences;
        _currentPreference = LanguagePreference.Normalize(preference);
        _currentCulture = culture;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    public CultureInfo CurrentCulture => _currentCulture;

    public string CurrentPreference => _currentPreference;

    public long Revision => _revision;

    public IReadOnlyList<SupportedLanguage> SupportedLanguages { get; } =
    [
        new(LanguagePreference.System, "Follow System"),
        new(LanguagePreference.SimplifiedChinese, "简体中文"),
        new(LanguagePreference.English, "English")
    ];

    public string GetString(string catalog, string key)
    {
        if (!Catalogs.TryGetValue(catalog, out var manager))
        {
            System.Diagnostics.Debug.WriteLine($"Unknown localization catalog: {catalog}");
            return $"[[{catalog}.{key}]]";
        }

        var value = manager.GetString(key, _currentCulture) ?? manager.GetString(key, CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        System.Diagnostics.Debug.WriteLine($"Missing localization resource: {catalog}.{key}");
        return $"[[{catalog}.{key}]]";
    }

    public string Format(string catalog, string key, params object[] arguments)
    {
        return string.Format(_currentCulture, GetString(catalog, key), arguments);
    }

    public async Task SetLanguageAsync(string languageTag, CancellationToken cancellationToken = default)
    {
        var normalized = LanguagePreference.Normalize(languageTag);
        if (!string.Equals(normalized, languageTag, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unsupported language preference.", nameof(languageTag));
        }

        await _switchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var systemCulture = CultureInfo.InstalledUICulture;
            var targetCulture = LanguagePreference.Resolve(normalized, systemCulture);
            if (_currentPreference == normalized && _currentCulture.Name == targetCulture.Name)
            {
                return;
            }

            var preferences = await _preferences.GetAsync(cancellationToken).ConfigureAwait(false);
            await _preferences.SaveAsync(preferences with { Language = normalized }, cancellationToken)
                .ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() => Apply(normalized, targetCulture));
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private void Apply(string preference, CultureInfo culture)
    {
        DesktopLanguageBootstrapper.Apply(preference, culture);
        Avalonia.Application.Current?.SetLanguageVariant(
            culture.Name.Equals(LanguagePreference.SimplifiedChinese, StringComparison.OrdinalIgnoreCase)
                ? LanguageVariant.zh_CN
                : LanguageVariant.en_US);

        _currentPreference = preference;
        _currentCulture = culture;
        _revision++;
        OnPropertyChanged(nameof(CurrentCulture));
        OnPropertyChanged(nameof(CurrentPreference));
        OnPropertyChanged(nameof(Revision));
        LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(preference, culture));
    }

    private static ResourceManager CreateManager(string name)
    {
        return new ResourceManager(
            $"AtomBox.Desktop.Localization.Resources.{name}",
            Assembly.GetExecutingAssembly());
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
