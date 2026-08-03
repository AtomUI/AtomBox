using AtomBox.Desktop.Localization;

namespace AtomBox.Desktop.ViewModels;

public abstract class LocalizedViewModelBase : ViewModelBase
{
    protected LocalizedViewModelBase(ILocalizationService localization)
    {
        Localization = localization;
        Localization.LanguageChanged += HandleLanguageChanged;
    }

    protected ILocalizationService Localization { get; }

    public override void Dispose()
    {
        Localization.LanguageChanged -= HandleLanguageChanged;
        base.Dispose();
    }

    protected virtual void OnLanguageChanged()
    {
    }

    private void HandleLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        OnLanguageChanged();
    }
}
