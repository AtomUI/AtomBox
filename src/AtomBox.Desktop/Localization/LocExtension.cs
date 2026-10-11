using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace AtomBox.Desktop.Localization;

public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string resource)
    {
        Resource = resource;
    }

    public string Resource { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var localization = LocalizationHost.Current
            ?? throw new InvalidOperationException("Localization has not been initialized.");
        var separator = Resource.IndexOf('.');
        if (separator <= 0 || separator == Resource.Length - 1)
        {
            throw new InvalidOperationException($"Invalid localization resource: {Resource}");
        }

        return new Binding(nameof(IAtomBoxLocalizedText.Revision))
        {
            Source = localization,
            Mode = BindingMode.OneWay,
            Converter = new ResourceConverter(
                localization,
                Resource[..separator],
                Resource[(separator + 1)..])
        };
    }

    private sealed class ResourceConverter(
        IAtomBoxLocalizedText localization,
        string catalog,
        string key) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return localization.GetString(catalog, key);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
