using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace AtomBox.Desktop.Tests;

public sealed class LocalizationResourceTests
{
    private static readonly string[] Catalogs =
    [
        "CommonStrings",
        "ShellStrings",
        "AccountStrings",
        "RemoteBrowserStrings",
        "TransferStrings",
        "SettingsStrings"
    ];

    [Theory]
    [MemberData(nameof(GetCatalogs))]
    public void EnglishAndChineseResources_HaveTheSameKeys(string catalog)
    {
        var manager = CreateManager(catalog);
        var english = ReadKeys(manager, CultureInfo.InvariantCulture);
        var chinese = ReadKeys(manager, CultureInfo.GetCultureInfo("zh-CN"));

        Assert.Equal(english, chinese);
    }

    [Theory]
    [MemberData(nameof(GetCatalogs))]
    public void EnglishBaseResources_DoNotContainEmptyValues(string catalog)
    {
        var manager = CreateManager(catalog);
        var resources = manager.GetResourceSet(CultureInfo.InvariantCulture, true, true);

        Assert.NotNull(resources);
        foreach (DictionaryEntry item in resources)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Value?.ToString()), item.Key?.ToString());
        }
    }

    public static IEnumerable<object[]> GetCatalogs()
    {
        return Catalogs.Select(catalog => new object[] { catalog });
    }

    private static ResourceManager CreateManager(string catalog)
    {
        return new ResourceManager(
            $"AtomBox.Desktop.Localization.Resources.{catalog}",
            typeof(AtomBox.Desktop.App).Assembly);
    }

    private static string[] ReadKeys(ResourceManager manager, CultureInfo culture)
    {
        var resources = manager.GetResourceSet(culture, true, true);
        Assert.NotNull(resources);
        return resources.Cast<DictionaryEntry>()
            .Select(item => item.Key.ToString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
