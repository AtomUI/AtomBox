namespace AtomBox.Desktop.Localization;

public sealed record SupportedLanguage(string Tag, string DisplayName);

public sealed class LanguageChangedEventArgs(
    string preference,
    System.Globalization.CultureInfo culture) : EventArgs
{
    public string Preference { get; } = preference;

    public System.Globalization.CultureInfo Culture { get; } = culture;
}
