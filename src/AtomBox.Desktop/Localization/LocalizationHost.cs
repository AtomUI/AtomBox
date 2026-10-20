namespace AtomBox.Desktop.Localization;

public static class LocalizationHost
{
    public static IAtomBoxLocalizedText? Current { get; private set; }

    public static void Initialize(IAtomBoxLocalizedText service)
    {
        Current = service ?? throw new ArgumentNullException(nameof(service));
    }
}
