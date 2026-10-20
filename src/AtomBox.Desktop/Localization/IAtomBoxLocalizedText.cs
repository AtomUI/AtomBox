using System.ComponentModel;
using System.Globalization;

namespace AtomBox.Desktop.Localization;

public interface IAtomBoxLocalizedText : INotifyPropertyChanged
{
    CultureInfo CurrentCulture { get; }

    long Revision { get; }

    string GetString(string catalog, string key);

    string Format(string catalog, string key, params object[] arguments);

    event EventHandler? LanguageChanged;
}
