using System.Globalization;

namespace AtomBox.Desktop.Localization;

public static class LanguagePreference
{
    public const string System = "system";
    public const string SimplifiedChinese = "zh-CN";
    public const string English = "en-US";

    public static string Normalize(string? value)
    {
        return value switch
        {
            SimplifiedChinese => SimplifiedChinese,
            English => English,
            System => System,
            _ => System
        };
    }

    public static CultureInfo Resolve(string? preference, CultureInfo systemCulture)
    {
        var normalized = Normalize(preference);
        if (normalized == SimplifiedChinese)
        {
            return CultureInfo.GetCultureInfo(SimplifiedChinese);
        }

        if (normalized == English)
        {
            return CultureInfo.GetCultureInfo(English);
        }

        var name = systemCulture.Name;
        var isSimplifiedChinese = name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
                                  name.Equals("zh-SG", StringComparison.OrdinalIgnoreCase) ||
                                  name.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase);
        return CultureInfo.GetCultureInfo(isSimplifiedChinese ? SimplifiedChinese : English);
    }
}
