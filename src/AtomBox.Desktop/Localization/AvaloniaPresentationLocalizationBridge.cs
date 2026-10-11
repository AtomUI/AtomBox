using System.Globalization;
using AtomUI.Controls;
using AtomUI.Theme.Language;
using Avalonia;
using Avalonia.Threading;
using AtomUI.City.Localization;

namespace AtomBox.Desktop.Localization;

public sealed class AvaloniaPresentationLocalizationBridge : IPresentationLocalizationBridge
{
    public async ValueTask<LocalizationResult> ApplyCultureAsync(
        CultureState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();

        if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            Apply(state.CurrentCulture);
            return LocalizationResult.Success();
        }

        await Dispatcher.UIThread.InvokeAsync(() => Apply(state.CurrentCulture));
        return LocalizationResult.Success();
    }

    private static void Apply(CultureInfo culture)
    {
        DesktopLanguageBootstrapper.ApplyCulture(culture);
        Avalonia.Application.Current?.SetLanguageVariant(
            culture.Name.Equals(LanguagePreference.SimplifiedChinese, StringComparison.OrdinalIgnoreCase)
                ? LanguageVariant.zh_CN
                : LanguageVariant.en_US);
    }
}
