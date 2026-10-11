using System.ComponentModel;

namespace AtomBox.Desktop.Localization;

public interface IDesktopLanguageService : INotifyPropertyChanged
{
    string CurrentPreference { get; }

    IReadOnlyList<SupportedLanguage> SupportedLanguages { get; }

    Task SetLanguageAsync(string languageTag, CancellationToken cancellationToken = default);
}
