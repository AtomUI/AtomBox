using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AtomBox.Desktop.Services;
using CityLocalizationService = AtomUI.City.Localization.ILocalizationService;

namespace AtomBox.Desktop.Localization;

public sealed class DesktopLanguageService : IDesktopLanguageService, IDisposable
{
    private readonly CityLocalizationService _city;
    private readonly IDesktopPreferencesService _preferences;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private string _currentPreference;
    private bool _disposed;

    public DesktopLanguageService(
        CityLocalizationService city,
        IDesktopPreferencesService preferences,
        string initialPreference)
    {
        _city = city ?? throw new ArgumentNullException(nameof(city));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _currentPreference = LanguagePreference.Normalize(initialPreference);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentPreference => _currentPreference;

    public IReadOnlyList<SupportedLanguage> SupportedLanguages { get; } =
    [
        new(LanguagePreference.System, "Follow System"),
        new(LanguagePreference.SimplifiedChinese, "简体中文"),
        new(LanguagePreference.English, "English")
    ];

    public async Task SetLanguageAsync(
        string languageTag,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalized = LanguagePreference.Normalize(languageTag);
        if (!string.Equals(normalized, languageTag, StringComparison.Ordinal))
        {
            throw new ArgumentException("Unsupported language preference.", nameof(languageTag));
        }

        await _switchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var targetCulture = LanguagePreference.Resolve(normalized, CultureInfo.InstalledUICulture);
            if (_currentPreference == normalized &&
                _city.CurrentCulture.Name.Equals(targetCulture.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var preferences = await _preferences.GetAsync(cancellationToken).ConfigureAwait(false);
            await _preferences
                .SaveAsync(preferences with { Language = normalized }, cancellationToken)
                .ConfigureAwait(false);

            var result = await _city
                .SetCultureAsync(targetCulture.Name, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    result.Error?.Message ?? $"Failed to switch language to '{targetCulture.Name}'.");
            }

            _currentPreference = normalized;
            OnPropertyChanged(nameof(CurrentPreference));
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _switchLock.Dispose();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
