using AtomBox.Desktop.Localization;

namespace AtomBox.Desktop.ViewModels;

public abstract class LocalizedViewModelBase : ViewModelBase
{
    protected LocalizedViewModelBase(IAtomBoxLocalizedText localization)
    {
        Localization = localization;
        Localization.LanguageChanged += HandleLanguageChanged;
    }

    protected IAtomBoxLocalizedText Localization { get; }

    public override void Dispose()
    {
        Localization.LanguageChanged -= HandleLanguageChanged;
        base.Dispose();
    }

    protected virtual void OnLanguageChanged()
    {
    }

    private void HandleLanguageChanged(object? sender, EventArgs e)
    {
        OnLanguageChanged();
    }
}
