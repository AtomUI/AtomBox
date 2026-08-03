namespace AtomBox.Desktop.Localization;

public static class LocalizationHost
{
    public static ILocalizationService? Current { get; private set; }

    public static void Initialize(ILocalizationService service)
    {
        Current = service ?? throw new ArgumentNullException(nameof(service));
    }
}
