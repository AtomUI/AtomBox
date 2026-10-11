namespace AtomBox.Desktop.Localization;

internal sealed record LocalizedText(string Catalog, string Key, params object[] Arguments)
{
    public string Resolve(IAtomBoxLocalizedText localization)
    {
        return localization.Format(Catalog, Key, Arguments);
    }
}
