using System.Globalization;
using AtomBox.Desktop.Localization;

namespace AtomBox.Desktop.Tests;

public sealed class LanguagePreferenceTests
{
    [Theory]
    [InlineData(null, LanguagePreference.System)]
    [InlineData("", LanguagePreference.System)]
    [InlineData("fr-FR", LanguagePreference.System)]
    [InlineData(LanguagePreference.System, LanguagePreference.System)]
    [InlineData(LanguagePreference.SimplifiedChinese, LanguagePreference.SimplifiedChinese)]
    [InlineData(LanguagePreference.English, LanguagePreference.English)]
    public void Normalize_ReturnsSupportedStableValue(string? value, string expected)
    {
        Assert.Equal(expected, LanguagePreference.Normalize(value));
    }

    [Theory]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-SG", "zh-CN")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("en-US", "en-US")]
    [InlineData("ja-JP", "en-US")]
    public void Resolve_SystemPreference_MapsSupportedCultures(string systemCulture, string expected)
    {
        var result = LanguagePreference.Resolve(
            LanguagePreference.System,
            CultureInfo.GetCultureInfo(systemCulture));

        Assert.Equal(expected, result.Name);
    }

    [Fact]
    public void Resolve_FixedPreference_IgnoresSystemCulture()
    {
        var result = LanguagePreference.Resolve(
            LanguagePreference.English,
            CultureInfo.GetCultureInfo("zh-CN"));

        Assert.Equal("en-US", result.Name);
    }
}
