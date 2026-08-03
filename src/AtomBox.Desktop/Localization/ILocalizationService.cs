using System.ComponentModel;
using System.Globalization;

namespace AtomBox.Desktop.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    CultureInfo CurrentCulture { get; }

    string CurrentPreference { get; }

    long Revision { get; }

    IReadOnlyList<SupportedLanguage> SupportedLanguages { get; }

    string GetString(string catalog, string key);

    string Format(string catalog, string key, params object[] arguments);

    Task SetLanguageAsync(string languageTag, CancellationToken cancellationToken = default);

    event EventHandler<LanguageChangedEventArgs>? LanguageChanged;
}
