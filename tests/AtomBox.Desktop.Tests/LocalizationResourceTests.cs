using AtomUI.City.Generated;
using AtomUI.City.Localization;

namespace AtomBox.Desktop.Tests;

public sealed class LocalizationResourceTests
{
    private static readonly string[] Catalogs =
    [
        "Common",
        "Shell",
        "Account",
        "RemoteBrowser",
        "Transfer",
        "Settings"
    ];

    [Theory]
    [MemberData(nameof(GetCatalogs))]
    public async Task EnglishAndChineseLocPacksHaveTheSameNonEmptyKeys(string catalog)
    {
        var provider = new AssemblyLanguagePackageProvider();
        var descriptors = provider.Discover(typeof(AtomBox.Desktop.App).Assembly);
        var expectedKeys = GeneratedLocalizationManifest.ResourceKeys
            .Where(key => key.StartsWith($"{catalog}.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(expectedKeys);
        using var english = await LoadAsync(provider, descriptors, $"{catalog}.en-US");
        using var chinese = await LoadAsync(provider, descriptors, $"{catalog}.zh-CN");

        foreach (var key in expectedKeys)
        {
            Assert.True(english.TryGetString(key, out var englishValue), key);
            Assert.False(string.IsNullOrWhiteSpace(englishValue), key);
            Assert.True(chinese.TryGetString(key, out var chineseValue), key);
            Assert.False(string.IsNullOrWhiteSpace(chineseValue), key);
        }
    }

    public static IEnumerable<object[]> GetCatalogs()
    {
        return Catalogs.Select(catalog => new object[] { catalog });
    }

    private static async Task<LanguagePackage> LoadAsync(
        AssemblyLanguagePackageProvider provider,
        IReadOnlyList<LanguagePackageDescriptor> descriptors,
        string packageId)
    {
        var descriptor = Assert.Single(descriptors, item => item.PackageId == packageId);
        var result = await provider.LoadAsync(descriptor);
        Assert.True(result.Succeeded, result.Error?.Message);
        return Assert.IsType<LanguagePackage>(result.Package);
    }
}
